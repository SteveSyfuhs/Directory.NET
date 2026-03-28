using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Directory.Dns.Dnssec;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Dns;

/// <summary>
/// DNS server for Active Directory service discovery.
/// Responds to SRV queries for _ldap._tcp, _kerberos._tcp, etc.
/// Supports DNS dynamic updates (RFC 2136), SOA/NS/PTR records, site-aware SRV resolution,
/// TCP fallback (RFC 1035/5966), EDNS0 (RFC 6891), and conditional forwarding.
/// </summary>
public class DnsServer : IHostedService, IDisposable
{
    private readonly DnsOptions _options;
    private readonly ILogger<DnsServer> _logger;
    private readonly DnsZoneStore _zoneStore;
    private readonly DnsDynamicUpdateHandler _dynamicUpdateHandler;
    private readonly DnsSiteService _siteService;
    private readonly DnssecService _dnssecService;
    private UdpClient _udpClient;
    private TcpListener _tcpListener;
    private CancellationTokenSource _cts;
    private Task _udpListenTask;
    private Task _tcpListenTask;

    // Default tenant for DNS operations
    private const string DefaultTenantId = "default";

    // Standard DNS UDP message limit without EDNS0
    private const int DefaultUdpPayloadSize = 512;

    public DnsServer(
        IOptions<DnsOptions> options,
        ILogger<DnsServer> logger,
        DnsZoneStore zoneStore,
        DnsDynamicUpdateHandler dynamicUpdateHandler,
        DnsSiteService siteService,
        DnssecService dnssecService)
    {
        _options = options.Value;
        _logger = logger;
        _zoneStore = zoneStore;
        _dynamicUpdateHandler = dynamicUpdateHandler;
        _siteService = siteService;
        _dnssecService = dnssecService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start UDP listener
        _udpClient = new UdpClient(_options.Port);
        _udpListenTask = ListenUdpAsync(_cts.Token);

        // Start TCP listener on the same port
        _tcpListener = new TcpListener(IPAddress.Any, _options.Port);
        _tcpListener.Start();
        _tcpListenTask = ListenTcpAsync(_cts.Token);

        _logger.LogInformation("DNS server listening on UDP+TCP port {Port} for domains: {Domains}",
            _options.Port, string.Join(", ", _options.Domains));

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DNS server stopping");
        _cts?.Cancel();
        _udpClient?.Close();
        _tcpListener?.Stop();

        var tasks = new List<Task>();
        if (_udpListenTask is not null) tasks.Add(_udpListenTask);
        if (_tcpListenTask is not null) tasks.Add(_tcpListenTask);

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    #region UDP Listener

    private async Task ListenUdpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                _ = Task.Run(() => HandleUdpQueryAsync(result.Buffer, result.RemoteEndPoint, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving UDP DNS query");
            }
        }
    }

    private async Task HandleUdpQueryAsync(byte[] data, IPEndPoint remoteEp, CancellationToken ct)
    {
        try
        {
            var responseBytes = await ProcessDnsMessageAsync(data, remoteEp, ct);
            if (responseBytes == null) return;

            // Determine the UDP payload limit: use EDNS0 client size if available, else 512
            var query = DnsPacket.Parse(data);
            var udpLimit = query.HasEdns
                ? Math.Min(query.ClientUdpPayloadSize, _options.EdnsUdpPayloadSize)
                : DefaultUdpPayloadSize;

            if (responseBytes.Length > udpLimit)
            {
                // Response too large for UDP; set TC (truncation) flag to prompt TCP retry
                _logger.LogDebug("UDP response truncated ({Size} > {Limit} bytes) for {Endpoint}",
                    responseBytes.Length, udpLimit, remoteEp);
                responseBytes = BuildTruncatedResponse(data, query);
            }

            await _udpClient.SendAsync(responseBytes, responseBytes.Length, remoteEp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling UDP DNS query from {Endpoint}", remoteEp);
        }
    }

    /// <summary>
    /// Builds a minimal truncated response preserving the query ID and question section,
    /// with the TC flag set to instruct the client to retry via TCP.
    /// </summary>
    private static byte[] BuildTruncatedResponse(byte[] originalQuery, DnsPacket query)
    {
        var tcResponse = new DnsPacket
        {
            Id = query.Id,
            Questions = query.Questions,
            Flags = DnsFlags.CreateResponse(authoritative: true, truncated: true),
        };
        return tcResponse.Encode();
    }

    #endregion

    #region TCP Listener

    private async Task ListenTcpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleTcpClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting TCP DNS connection");
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                var stream = client.GetStream();
                var remoteEp = client.Client.RemoteEndPoint as IPEndPoint ?? new IPEndPoint(IPAddress.Any, 0);

                // TCP DNS: messages are prefixed with a 2-byte big-endian length
                // Support multiple queries on the same connection (RFC 7766)
                while (!ct.IsCancellationRequested)
                {
                    // Read 2-byte length prefix
                    var lengthBuf = new byte[2];
                    var bytesRead = await ReadExactAsync(stream, lengthBuf, 0, 2, ct);
                    if (bytesRead < 2) break; // Connection closed

                    var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuf);
                    if (messageLength == 0 || messageLength > 65535) break;

                    // Read exactly messageLength bytes
                    var messageBuf = new byte[messageLength];
                    bytesRead = await ReadExactAsync(stream, messageBuf, 0, messageLength, ct);
                    if (bytesRead < messageLength) break;

                    _logger.LogDebug("TCP DNS query ({Size} bytes) from {Endpoint}", messageLength, remoteEp);

                    var responseBytes = await ProcessDnsMessageAsync(messageBuf, remoteEp, ct);
                    if (responseBytes == null) continue;

                    // Write 2-byte length prefix + response
                    var responseLengthBuf = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(responseLengthBuf, (ushort)responseBytes.Length);
                    await stream.WriteAsync(responseLengthBuf, ct);
                    await stream.WriteAsync(responseBytes, ct);
                    await stream.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { /* Client disconnected */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling TCP DNS client");
            }
        }
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from the stream.
    /// Returns the number of bytes actually read (less than count means EOF).
    /// </summary>
    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) break; // EOF
            totalRead += read;
        }
        return totalRead;
    }

    #endregion

    #region Shared Query Processing

    /// <summary>
    /// Processes a DNS message (query or dynamic update) and returns the response bytes.
    /// Shared by both UDP and TCP listeners.
    /// </summary>
    private async Task<byte[]> ProcessDnsMessageAsync(byte[] data, IPEndPoint remoteEp, CancellationToken ct)
    {
        // Check for DNS UPDATE packets (opcode 5) before full parsing
        if (data.Length >= 4)
        {
            var flags = (ushort)((data[2] << 8) | data[3]);
            var opcode = (flags >> 11) & 0xF;
            if (opcode == 5)
            {
                _logger.LogDebug("DNS dynamic update from {Endpoint}", remoteEp);
                var updateResult = await _dynamicUpdateHandler.ProcessUpdateAsync(data, DefaultTenantId, ct);
                return updateResult.BuildResponse();
            }
        }

        var query = DnsPacket.Parse(data);

        if (!query.Flags.IsQuery || query.Questions.Count == 0)
            return null;

        var question = query.Questions[0];
        _logger.LogDebug("DNS query: {Name} {Type} from {Endpoint}", question.Name, question.Type, remoteEp);

        var response = new DnsPacket
        {
            Id = query.Id,
            Questions = query.Questions,
        };

        var answered = false;
        var isLocalZone = IsLocalZone(question.Name);

        switch (question.Type)
        {
            case DnsRecordType.SRV:
                answered = await HandleSrvQueryAsync(question, response, remoteEp, ct);
                break;

            case DnsRecordType.A:
                answered = await HandleAQueryAsync(question, response, ct);
                break;

            case DnsRecordType.AAAA:
                answered = await HandleAaaaQueryAsync(question, response, ct);
                break;

            case DnsRecordType.SOA:
                answered = HandleSoaQuery(question, response);
                break;

            case DnsRecordType.NS:
                answered = HandleNsQuery(question, response);
                break;

            case DnsRecordType.PTR:
                answered = await HandlePtrQueryAsync(question, response, ct);
                break;

            case DnsRecordType.DNSKEY:
                answered = HandleDnskeyQuery(question, response);
                break;

            case DnsRecordType.DS:
                answered = HandleDsQuery(question, response);
                break;
        }

        // If not answered and not a local zone, try conditional forwarding
        if (!answered && !isLocalZone)
        {
            var forwarded = await TryForwardQueryAsync(data, question.Name, ct);
            if (forwarded != null)
                return forwarded;
        }

        response.Flags = answered
            ? DnsFlags.CreateResponse(authoritative: true)
            : DnsFlags.CreateNxDomainResponse();

        // Include RRSIG records when DO bit is set and zone has DNSSEC enabled
        if (query.DnssecOk && answered && isLocalZone)
        {
            AppendDnssecRecords(question, response);
        }

        // Add EDNS0 OPT record to response if the query included one
        if (query.HasEdns)
        {
            var optTtl = query.DnssecOk ? 0x8000u : 0u; // Echo DO bit back
            response.Additional.Add(new DnsOptRecord
            {
                UdpPayloadSize = _options.EdnsUdpPayloadSize,
                Class = _options.EdnsUdpPayloadSize,
                Ttl = optTtl,
            });
        }

        return response.Encode();
    }

    #endregion

    #region PTR Record Handling

    /// <summary>
    /// Handle reverse DNS (PTR) lookups for in-addr.arpa and ip6.arpa zones.
    /// Extracts the IP from the reversed name, looks up which host has that IP,
    /// and returns a PTR record with the FQDN.
    /// </summary>
    private async Task<bool> HandlePtrQueryAsync(DnsQuestion question, DnsPacket response, CancellationToken ct)
    {
        var name = question.Name;

        // Try to extract an IP address from the reverse lookup name
        if (TryParseReverseIPv4(name, out var ipv4Address))
        {
            return await ResolvePtrAsync(ipv4Address, name, response, ct);
        }

        if (TryParseReverseIPv6(name, out var ipv6Address))
        {
            return await ResolvePtrAsync(ipv6Address, name, response, ct);
        }

        return false;
    }

    private async Task<bool> ResolvePtrAsync(IPAddress address, string ptrName, DnsPacket response, CancellationToken ct)
    {
        var ipString = address.ToString();

        // Check if this is our own server IP
        foreach (var serverIp in _options.ServerIpAddresses)
        {
            if (serverIp.Equals(ipString, StringComparison.OrdinalIgnoreCase))
            {
                response.Answers.Add(new DnsPtrRecord
                {
                    Name = ptrName,
                    DomainName = _options.ServerHostname,
                    Ttl = (uint)_options.DefaultTtl,
                });
                return true;
            }
        }

        // Search AD-integrated zones for A/AAAA records matching this IP
        var recordType = address.AddressFamily == AddressFamily.InterNetwork ? DnsRecordType.A : DnsRecordType.AAAA;
        foreach (var domain in GetAllLocalZones())
        {
            var allRecords = await _zoneStore.GetAllRecordsAsync(DefaultTenantId, domain, recordType, ct);
            foreach (var record in allRecords)
            {
                if (record.Data.Equals(ipString, StringComparison.OrdinalIgnoreCase))
                {
                    response.Answers.Add(new DnsPtrRecord
                    {
                        Name = ptrName,
                        DomainName = record.Name,
                        Ttl = (uint)record.Ttl,
                    });
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Parse an IPv4 reverse lookup name (e.g., "4.3.2.1.in-addr.arpa") to an IPAddress.
    /// </summary>
    private static bool TryParseReverseIPv4(string name, out IPAddress address)
    {
        address = IPAddress.None;
        const string suffix = ".in-addr.arpa";

        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var ipPart = name[..^suffix.Length];
        var octets = ipPart.Split('.');
        if (octets.Length != 4) return false;

        // Reverse the octets to get the normal IP order
        Array.Reverse(octets);
        return IPAddress.TryParse(string.Join('.', octets), out address);
    }

    /// <summary>
    /// Parse an IPv6 reverse lookup name (e.g., nibble format under ip6.arpa) to an IPAddress.
    /// </summary>
    private static bool TryParseReverseIPv6(string name, out IPAddress address)
    {
        address = IPAddress.None;
        const string suffix = ".ip6.arpa";

        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var nibblePart = name[..^suffix.Length];
        var nibbles = nibblePart.Split('.');
        if (nibbles.Length != 32) return false;

        // Reverse nibbles and reassemble into IPv6 address
        Array.Reverse(nibbles);
        var hex = string.Concat(nibbles);
        // Insert colons every 4 characters
        var parts = new string[8];
        for (int i = 0; i < 8; i++)
        {
            parts[i] = hex.Substring(i * 4, 4);
        }
        return IPAddress.TryParse(string.Join(':', parts), out address);
    }

    #endregion

    #region Conditional Forwarding

    /// <summary>
    /// Attempts to forward a DNS query to a configured conditional forwarder or default upstream.
    /// Returns the raw response bytes from the upstream, or null if no forwarder matches.
    /// </summary>
    private async Task<byte[]> TryForwardQueryAsync(byte[] originalQuery, string queryName, CancellationToken ct)
    {
        // Find the best matching conditional forwarder (longest zone name match)
        DnsForwarderEntry bestMatch = null;
        var bestMatchLength = 0;

        foreach (var forwarder in _options.Forwarders)
        {
            if (string.IsNullOrEmpty(forwarder.ZoneName)) continue;

            if (queryName.Equals(forwarder.ZoneName, StringComparison.OrdinalIgnoreCase)
                || queryName.EndsWith($".{forwarder.ZoneName}", StringComparison.OrdinalIgnoreCase))
            {
                if (forwarder.ZoneName.Length > bestMatchLength)
                {
                    bestMatch = forwarder;
                    bestMatchLength = forwarder.ZoneName.Length;
                }
            }
        }

        if (bestMatch != null && bestMatch.ForwarderAddresses.Count > 0)
        {
            return await ForwardToUpstreamAsync(originalQuery, bestMatch.ForwarderAddresses, queryName, ct);
        }

        // Try default forwarder if configured
        if (!string.IsNullOrEmpty(_options.DefaultForwarder))
        {
            return await ForwardToUpstreamAsync(originalQuery, [_options.DefaultForwarder], queryName, ct);
        }

        return null; // No forwarder; caller will return NXDOMAIN
    }

    /// <summary>
    /// Forwards a DNS query to one of the specified upstream DNS servers via UDP.
    /// Tries each address in order until one responds.
    /// </summary>
    private async Task<byte[]> ForwardToUpstreamAsync(
        byte[] query, List<string> upstreamAddresses, string queryName, CancellationToken ct)
    {
        foreach (var addressStr in upstreamAddresses)
        {
            if (!IPAddress.TryParse(addressStr, out var upstreamIp))
                continue;

            try
            {
                using var forwarderClient = new UdpClient();
                var endpoint = new IPEndPoint(upstreamIp, 53);

                await forwarderClient.SendAsync(query, query.Length, endpoint);

                // Wait up to 3 seconds for a response
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                var result = await forwarderClient.ReceiveAsync(timeoutCts.Token);
                _logger.LogDebug("Forwarded query for {Name} to {Upstream}, got {Size} bytes",
                    queryName, addressStr, result.Buffer.Length);
                return result.Buffer;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Forwarding timeout for {Name} to {Upstream}", queryName, addressStr);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to forward query for {Name} to {Upstream}", queryName, addressStr);
            }
        }

        return null;
    }

    #endregion

    #region Standard Query Handlers

    /// <summary>
    /// Checks whether a query name falls under one of the locally managed zones.
    /// </summary>
    private bool IsLocalZone(string queryName)
    {
        foreach (var zone in GetAllLocalZones())
        {
            if (queryName.Equals(zone, StringComparison.OrdinalIgnoreCase)
                || queryName.EndsWith($".{zone}", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Reverse lookup zones for our server IPs are also local
        if (queryName.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase)
            || queryName.EndsWith(".ip6.arpa", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task<bool> HandleSrvQueryAsync(DnsQuestion question, DnsPacket response, IPEndPoint remoteEp, CancellationToken ct)
    {
        // Determine which zone this query belongs to
        string matchedZone = null;
        foreach (var zone in GetAllLocalZones())
        {
            if (question.Name.EndsWith($".{zone}", StringComparison.OrdinalIgnoreCase)
                || question.Name.Equals(zone, StringComparison.OrdinalIgnoreCase))
            {
                matchedZone = zone;
                break;
            }
        }

        if (matchedZone == null) return false;

        // Check for site-specific SRV records first when client maps to a site
        var site = _siteService.GetSiteForAddress(remoteEp.Address);
        if (site != null)
        {
            var siteRecordNames = _siteService.GetSiteSpecificSrvRecords(site, matchedZone);
            foreach (var siteRecordName in siteRecordNames)
            {
                if (question.Name.Equals(siteRecordName, StringComparison.OrdinalIgnoreCase))
                {
                    var siteRecords = await _zoneStore.GetRecordsByNameAsync(
                        DefaultTenantId, matchedZone, question.Name, DnsRecordType.SRV, ct);

                    if (siteRecords.Count > 0)
                    {
                        foreach (var rec in siteRecords)
                        {
                            if (TryParseSrvData(rec.Data, out var priority, out var weight, out var port, out var target))
                            {
                                response.Answers.Add(new DnsSrvRecord
                                {
                                    Name = question.Name,
                                    Priority = priority,
                                    Weight = weight,
                                    Port = port,
                                    Target = target,
                                    Ttl = (uint)rec.Ttl,
                                });
                                AddAdditionalARecords(response, target);
                            }
                        }

                        _logger.LogDebug("Resolved site-specific SRV from zone store: {Name} -> site {Site} ({Count} records)",
                            question.Name, site, siteRecords.Count);
                        return true;
                    }
                }
            }
        }

        // Query zone store for SRV records
        var records = await _zoneStore.GetRecordsByNameAsync(
            DefaultTenantId, matchedZone, question.Name, DnsRecordType.SRV, ct);

        if (records.Count > 0)
        {
            foreach (var rec in records)
            {
                if (TryParseSrvData(rec.Data, out var priority, out var weight, out var port, out var target))
                {
                    response.Answers.Add(new DnsSrvRecord
                    {
                        Name = question.Name,
                        Priority = priority,
                        Weight = weight,
                        Port = port,
                        Target = target,
                        Ttl = (uint)rec.Ttl,
                    });
                    AddAdditionalARecords(response, target);
                }
            }

            _logger.LogDebug("Resolved SRV from zone store: {Name} ({Count} records)", question.Name, records.Count);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses SRV record data in the format "priority weight port target".
    /// </summary>
    private static bool TryParseSrvData(string data, out ushort priority, out ushort weight, out ushort port, out string target)
    {
        priority = 0;
        weight = 100;
        port = 0;
        target = "";

        var parts = data.Split(' ');
        if (parts.Length < 4) return false;

        if (!ushort.TryParse(parts[0], out priority)) return false;
        if (!ushort.TryParse(parts[1], out weight)) return false;
        if (!ushort.TryParse(parts[2], out port)) return false;
        target = parts[3];
        return !string.IsNullOrEmpty(target);
    }

    private async Task<bool> HandleAQueryAsync(DnsQuestion question, DnsPacket response, CancellationToken ct)
    {
        // Try AD-integrated zone store first (includes DC-registered A records)
        foreach (var domain in GetAllLocalZones())
        {
            if (question.Name.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)
                || question.Name.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                var records = await _zoneStore.GetRecordsByNameAsync(DefaultTenantId, domain, question.Name, DnsRecordType.A, ct);
                if (records.Count > 0)
                {
                    foreach (var record in records)
                    {
                        if (IPAddress.TryParse(record.Data, out var addr))
                        {
                            response.Answers.Add(new DnsARecord
                            {
                                Name = question.Name,
                                Address = addr,
                                Ttl = (uint)record.Ttl,
                            });
                        }
                    }
                    return true;
                }
            }
        }

        // Fall back to configured server hostname/IP
        if (question.Name.Equals(_options.ServerHostname, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ip in _options.ServerIpAddresses)
            {
                if (IPAddress.TryParse(ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    response.Answers.Add(new DnsARecord
                    {
                        Name = question.Name,
                        Address = addr,
                        Ttl = (uint)_options.DefaultTtl,
                    });
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<bool> HandleAaaaQueryAsync(DnsQuestion question, DnsPacket response, CancellationToken ct)
    {
        // Try AD-integrated zone store first (includes DC-registered AAAA records)
        foreach (var domain in GetAllLocalZones())
        {
            if (question.Name.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase)
                || question.Name.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                var records = await _zoneStore.GetRecordsByNameAsync(DefaultTenantId, domain, question.Name, DnsRecordType.AAAA, ct);
                if (records.Count > 0)
                {
                    foreach (var record in records)
                    {
                        if (IPAddress.TryParse(record.Data, out var addr))
                        {
                            response.Answers.Add(new DnsAaaaRecord
                            {
                                Name = question.Name,
                                Address = addr,
                                Ttl = (uint)record.Ttl,
                            });
                        }
                    }
                    return true;
                }
            }
        }

        // Fall back to configured server hostname/IP
        if (question.Name.Equals(_options.ServerHostname, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ip in _options.ServerIpAddresses)
            {
                if (IPAddress.TryParse(ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    response.Answers.Add(new DnsAaaaRecord
                    {
                        Name = question.Name,
                        Address = addr,
                        Ttl = (uint)_options.DefaultTtl,
                    });
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Handle SOA record queries for zone apex.
    /// Returns authoritative SOA for managed domains.
    /// </summary>
    private bool HandleSoaQuery(DnsQuestion question, DnsPacket response)
    {
        foreach (var domain in _options.Domains)
        {
            if (question.Name.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                response.Answers.Add(new DnsSoaRecord
                {
                    Name = domain,
                    PrimaryNs = _options.ServerHostname,
                    AdminMailbox = $"hostmaster.{domain}",
                    Serial = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60),
                    Refresh = 900,
                    Retry = 600,
                    Expire = 86400,
                    MinimumTtl = 60,
                    Ttl = (uint)_options.DefaultTtl,
                });
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Handle NS record queries.
    /// Returns NS records pointing to this server for managed domains.
    /// </summary>
    private bool HandleNsQuery(DnsQuestion question, DnsPacket response)
    {
        foreach (var domain in _options.Domains)
        {
            if (question.Name.Equals(domain, StringComparison.OrdinalIgnoreCase))
            {
                response.Answers.Add(new DnsNsRecord
                {
                    Name = domain,
                    NsName = _options.ServerHostname,
                    Ttl = (uint)_options.DefaultTtl,
                });

                AddAdditionalARecords(response, _options.ServerHostname);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns all locally-managed zone names, including the forest DNS name
    /// when configured and different from the domain zones. This ensures
    /// forest-level records (GC SRV, _msdcs) are resolved authoritatively.
    /// </summary>
    private IEnumerable<string> GetAllLocalZones()
    {
        foreach (var domain in _options.Domains)
        {
            yield return domain;
        }

        if (!string.IsNullOrEmpty(_options.ForestDnsName)
            && !_options.Domains.Contains(_options.ForestDnsName, StringComparer.OrdinalIgnoreCase))
        {
            yield return _options.ForestDnsName;
        }
    }

    private void AddAdditionalARecords(DnsPacket response, string hostname)
    {
        foreach (var ip in _options.ServerIpAddresses)
        {
            if (IPAddress.TryParse(ip, out var addr))
            {
                response.Additional.Add(new DnsARecord
                {
                    Name = hostname,
                    Address = addr,
                    Ttl = (uint)_options.DefaultTtl,
                });
            }
        }
    }

    #endregion

    #region DNSSEC Query Handlers

    /// <summary>
    /// Handle DNSKEY queries — return zone public keys for DNSSEC validation.
    /// </summary>
    private bool HandleDnskeyQuery(DnsQuestion question, DnsPacket response)
    {
        var zoneName = FindMatchingZone(question.Name);
        if (zoneName == null) return false;

        var settings = _dnssecService.GetZoneSettings(zoneName);
        if (!settings.DnssecEnabled) return false;

        var dnskeys = _dnssecService.GetDnskeyRecords(zoneName);
        if (dnskeys.Count == 0) return false;

        foreach (var dnskey in dnskeys)
        {
            response.Answers.Add(dnskey);
        }

        return true;
    }

    /// <summary>
    /// Handle DS queries — return delegation signer records.
    /// </summary>
    private bool HandleDsQuery(DnsQuestion question, DnsPacket response)
    {
        var zoneName = FindMatchingZone(question.Name);
        if (zoneName == null) return false;

        var dsRecord = _dnssecService.GenerateDsRecord(zoneName);
        if (dsRecord == null) return false;

        response.Answers.Add(new DnsDsRecord
        {
            Name = question.Name,
            KeyTag = (ushort)dsRecord.KeyTag,
            Algorithm = (byte)dsRecord.Algorithm,
            DigestType = (byte)dsRecord.DigestType,
            Digest = Convert.FromHexString(dsRecord.Digest),
            Ttl = 3600,
        });

        return true;
    }

    /// <summary>
    /// Append RRSIG records to the response for each answer RRset
    /// when the query has the DO bit set and the zone has DNSSEC enabled.
    /// </summary>
    private void AppendDnssecRecords(DnsQuestion question, DnsPacket response)
    {
        var zoneName = FindMatchingZone(question.Name);
        if (zoneName == null) return;

        var settings = _dnssecService.GetZoneSettings(zoneName);
        if (!settings.DnssecEnabled) return;

        // Add RRSIG records for each answer
        var answeredTypes = response.Answers.Select(a => a.Type).Distinct().ToList();
        foreach (var answerType in answeredTypes)
        {
            var rrsigs = _dnssecService.GetRrsigRecords(zoneName, question.Name, answerType);
            foreach (var rrsig in rrsigs)
            {
                response.Answers.Add(new DnsRrsigRecord
                {
                    Name = rrsig.OwnerName,
                    TypeCovered = rrsig.TypeCovered,
                    Algorithm = (byte)rrsig.Algorithm,
                    Labels = (byte)rrsig.Labels,
                    OriginalTtl = rrsig.OriginalTtl,
                    SignatureExpiration = (uint)rrsig.SignatureExpiration.ToUnixTimeSeconds(),
                    SignatureInception = (uint)rrsig.SignatureInception.ToUnixTimeSeconds(),
                    KeyTag = (ushort)rrsig.KeyTag,
                    SignerName = rrsig.SignerName,
                    Signature = rrsig.Signature,
                    Ttl = rrsig.OriginalTtl,
                });
            }
        }
    }

    /// <summary>
    /// Find which locally-managed zone a query name belongs to.
    /// </summary>
    private string FindMatchingZone(string queryName)
    {
        foreach (var zone in GetAllLocalZones())
        {
            if (queryName.Equals(zone, StringComparison.OrdinalIgnoreCase)
                || queryName.EndsWith($".{zone}", StringComparison.OrdinalIgnoreCase))
                return zone;
        }
        return null;
    }

    #endregion

    public void Dispose()
    {
        _udpClient?.Dispose();
        _tcpListener?.Stop();
        _cts?.Dispose();
    }
}
