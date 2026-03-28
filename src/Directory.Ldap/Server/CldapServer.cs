using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Directory.Ldap.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Server;

/// <summary>
/// CLDAP (Connectionless LDAP) server for DC discovery — MS-ADTS section 6.3.3.
/// Windows clients send UDP LDAP searches to port 389 to locate domain controllers
/// before establishing a TCP LDAP connection. This is the "DC Locator" protocol.
///
/// The client searches for (&amp;(DnsDomain=domain)(Host=name)(NtVer=flags)) on the
/// rootDSE, and the DC responds with a NETLOGON structure containing DC capabilities.
/// </summary>
public class CldapServer : IHostedService, IDisposable
{
    private readonly LdapServerOptions _options;
    private readonly ILogger<CldapServer> _logger;
    private UdpClient _udpClient;
    private CancellationTokenSource _cts;
    private Task _listenTask;

    // DS_FLAG values — see MS-ADTS 6.3.3
    [Flags]
    private enum DsFlag : uint
    {
        DS_PDC_FLAG = 0x00000001,
        DS_GC_FLAG = 0x00000004,
        DS_LDAP_FLAG = 0x00000008,
        DS_DS_FLAG = 0x00000010,
        DS_KDC_FLAG = 0x00000020,
        DS_TIMESERV_FLAG = 0x00000040,
        DS_CLOSEST_FLAG = 0x00000080,
        DS_WRITABLE_FLAG = 0x00000100,
        DS_GOOD_TIMESERV_FLAG = 0x00000200,
        DS_FULL_SECRET_DOMAIN_6_FLAG = 0x00001000,
        DS_WS_FLAG = 0x00002000,
        DS_DS_8_FLAG = 0x00004000,
        DS_DS_9_FLAG = 0x00008000,
        DS_DS_10_FLAG = 0x00010000,
        DS_KEY_LIST_FLAG = 0x00020000,
    }

    public CldapServer(IOptions<LdapServerOptions> options, ILogger<CldapServer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _udpClient = new UdpClient(_options.Port);
        _listenTask = ListenAsync(_cts.Token);
        _logger.LogInformation("CLDAP DC Locator listening on UDP port {Port}", _options.Port);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _udpClient?.Close();
        if (_listenTask != null)
            await _listenTask;
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                _ = ProcessCldapRequestAsync(result.Buffer, result.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CLDAP receive error");
            }
        }
    }

    private async Task ProcessCldapRequestAsync(byte[] data, IPEndPoint remoteEp, CancellationToken ct)
    {
        try
        {
            // Parse the LDAP search request from BER
            var (messageId, dnsDomain, ntVer) = ParseCldapSearch(data);

            if (string.IsNullOrEmpty(dnsDomain))
            {
                _logger.LogDebug("CLDAP request without DnsDomain filter from {RemoteEP}", remoteEp);
                return;
            }

            _logger.LogDebug("CLDAP DC Locator query from {RemoteEP}: DnsDomain={Domain}, NtVer=0x{NtVer:X8}",
                remoteEp, dnsDomain, ntVer);

            // Build the NETLOGON response
            var response = BuildNetlogonResponse(messageId, dnsDomain);
            await _udpClient.SendAsync(response, response.Length, remoteEp);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to process CLDAP request from {RemoteEP}", remoteEp);
        }
    }

    /// <summary>
    /// Parse a CLDAP search request to extract the DnsDomain and NtVer filter values.
    /// The request is a standard LDAP SearchRequest message sent over UDP.
    /// </summary>
    private static (int messageId, string dnsDomain, uint ntVer) ParseCldapSearch(byte[] data)
    {
        try
        {
            var reader = new AsnReader(data, AsnEncodingRules.BER);
            var seq = reader.ReadSequence();

            // Message ID
            var messageId = (int)seq.ReadInteger();

            // SearchRequest (APPLICATION 3)
            var searchTag = new Asn1Tag(TagClass.Application, 3, true);
            var searchReq = seq.ReadSequence(searchTag);

            // Skip baseDN, scope, derefAliases, sizeLimit, timeLimit, typesOnly
            searchReq.ReadOctetString(); // baseDN
            searchReq.ReadEncodedValue(); // scope (ENUMERATED)
            searchReq.ReadEncodedValue(); // derefAliases (ENUMERATED)
            searchReq.ReadInteger(); // sizeLimit
            searchReq.ReadInteger(); // timeLimit
            searchReq.ReadBoolean(); // typesOnly

            // Parse the filter to find DnsDomain and NtVer
            string dnsDomain = null;
            uint ntVer = 0;

            var filterTag = searchReq.PeekTag();
            if (filterTag.TagClass == TagClass.ContextSpecific && filterTag.TagValue == 0)
            {
                // AND filter
                var andFilter = searchReq.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                while (andFilter.HasData)
                {
                    var itemTag = andFilter.PeekTag();
                    if (itemTag.TagClass == TagClass.ContextSpecific && itemTag.TagValue == 3)
                    {
                        // Equality match
                        var eqSeq = andFilter.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 3, true));
                        var attrName = Encoding.UTF8.GetString(eqSeq.ReadOctetString());
                        var attrValue = eqSeq.ReadOctetString();

                        if (attrName.Equals("DnsDomain", StringComparison.OrdinalIgnoreCase))
                            dnsDomain = Encoding.UTF8.GetString(attrValue);
                        else if (attrName.Equals("NtVer", StringComparison.OrdinalIgnoreCase) && attrValue.Length >= 4)
                            ntVer = BitConverter.ToUInt32(attrValue, 0);
                    }
                    else
                    {
                        // Skip other filter items
                        andFilter.ReadEncodedValue();
                    }
                }
            }

            return (messageId, dnsDomain, ntVer);
        }
        catch
        {
            return (0, null, 0);
        }
    }

    /// <summary>
    /// Build a CLDAP SearchResultEntry containing the Netlogon attribute with
    /// a LOGON_SAM_LOGON_RESPONSE_EX structure per MS-ADTS 6.3.1.9.
    /// </summary>
    private byte[] BuildNetlogonResponse(int messageId, string dnsDomain)
    {
        var domainDn = _options.DefaultDomain;
        var domainDns = GetDomainDnsName();
        var machineName = Environment.MachineName.ToUpperInvariant();
        var siteName = "Default-First-Site-Name";

        // Build LOGON_SAM_LOGON_RESPONSE_EX (type 23 = 0x17)
        var netlogon = BuildNetlogonExResponse(
            dnsDomain, domainDns, domainDn, machineName, siteName);

        // Wrap in LDAP SearchResultEntry + SearchResultDone
        var writer = new AsnWriter(AsnEncodingRules.BER);

        // Outer SEQUENCE (LDAP message envelope)
        using (writer.PushSequence())
        {
            writer.WriteInteger(messageId);

            // SearchResultEntry (APPLICATION 4)
            var entryTag = new Asn1Tag(TagClass.Application, 4, true);
            using (writer.PushSequence(entryTag))
            {
                writer.WriteOctetString(Array.Empty<byte>()); // objectName = ""

                // Attributes SEQUENCE OF
                using (writer.PushSequence())
                {
                    // Single attribute: Netlogon
                    using (writer.PushSequence())
                    {
                        writer.WriteOctetString(Encoding.UTF8.GetBytes("Netlogon"));
                        using (writer.PushSetOf())
                        {
                            writer.WriteOctetString(netlogon);
                        }
                    }
                }
            }
        }

        var entryBytes = writer.Encode();

        // Now write SearchResultDone
        var doneWriter = new AsnWriter(AsnEncodingRules.BER);
        using (doneWriter.PushSequence())
        {
            doneWriter.WriteInteger(messageId);
            var doneTag = new Asn1Tag(TagClass.Application, 5, true);
            using (doneWriter.PushSequence(doneTag))
            {
                doneWriter.WriteInteger(0); // success (resultCode)
                doneWriter.WriteOctetString(Array.Empty<byte>()); // matchedDN
                doneWriter.WriteOctetString(Array.Empty<byte>()); // diagnosticMessage
            }
        }

        var doneBytes = doneWriter.Encode();

        // Combine both messages
        var result = new byte[entryBytes.Length + doneBytes.Length];
        Buffer.BlockCopy(entryBytes, 0, result, 0, entryBytes.Length);
        Buffer.BlockCopy(doneBytes, 0, result, entryBytes.Length, doneBytes.Length);
        return result;
    }

    /// <summary>
    /// Build a LOGON_SAM_LOGON_RESPONSE_EX (opcode 23) binary structure.
    /// Per MS-ADTS 6.3.1.9 — this is what nltest /dsgetdc and the DC locator parse.
    /// </summary>
    private byte[] BuildNetlogonExResponse(
        string queryDomain, string domainDns, string domainDn,
        string machineName, string siteName)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Opcode: LOGON_SAM_LOGON_RESPONSE_EX = 23
        bw.Write((ushort)23);

        // Sbz (padding)
        bw.Write((ushort)0);

        // Flags
        var flags = DsFlag.DS_LDAP_FLAG | DsFlag.DS_DS_FLAG | DsFlag.DS_KDC_FLAG |
                    DsFlag.DS_WRITABLE_FLAG | DsFlag.DS_GC_FLAG | DsFlag.DS_TIMESERV_FLAG |
                    DsFlag.DS_GOOD_TIMESERV_FLAG | DsFlag.DS_FULL_SECRET_DOMAIN_6_FLAG |
                    DsFlag.DS_WS_FLAG | DsFlag.DS_DS_8_FLAG | DsFlag.DS_DS_9_FLAG |
                    DsFlag.DS_DS_10_FLAG | DsFlag.DS_KEY_LIST_FLAG |
                    DsFlag.DS_PDC_FLAG; // Claim PDC role
        bw.Write((uint)flags);

        // DomainGuid — 16 bytes, deterministic from domain name
        var domainGuid = GuidFromDomainName(domainDns);
        bw.Write(domainGuid.ToByteArray());

        // DNS-compressed strings: DnsForestName, DnsDomainName, DnsHostName,
        // NetbiosDomainName, NetbiosComputerName, UserName, DcSiteName, ClientSiteName
        var stringOffset = ms.Position;
        WriteDnsCompressedString(bw, domainDns);       // DnsForestName
        WriteDnsCompressedString(bw, domainDns);       // DnsDomainName
        WriteDnsCompressedString(bw, $"{machineName.ToLowerInvariant()}.{domainDns}"); // DnsHostName
        WriteDnsCompressedString(bw, ExtractNetbiosName(domainDn)); // NetbiosDomainName
        WriteDnsCompressedString(bw, machineName);     // NetbiosComputerName
        WriteDnsCompressedString(bw, "");              // UserName (empty for DC locator)
        WriteDnsCompressedString(bw, siteName);        // DcSiteName
        WriteDnsCompressedString(bw, siteName);        // ClientSiteName

        // NtVersion
        bw.Write((uint)5); // NETLOGON_NT_VERSION_5EX | NETLOGON_NT_VERSION_5

        // LmNtToken
        bw.Write((ushort)0xFFFF);

        // Lm20Token
        bw.Write((ushort)0xFFFF);

        return ms.ToArray();
    }

    private static void WriteDnsCompressedString(BinaryWriter bw, string value)
    {
        // Write as null-terminated UTF-8 with DNS label compression format
        // Simplified: write each label as length-prefixed, then 0 terminator
        if (string.IsNullOrEmpty(value))
        {
            bw.Write((byte)0);
            return;
        }

        var labels = value.Split('.');
        foreach (var label in labels)
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            bw.Write((byte)bytes.Length);
            bw.Write(bytes);
        }
        bw.Write((byte)0);
    }

    private static Guid GuidFromDomainName(string domainDns)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(domainDns.ToLowerInvariant()));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string ExtractNetbiosName(string domainDn)
    {
        // Extract first DC= component as NETBIOS name
        var parts = domainDn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..].ToUpperInvariant();
        }
        return "DIRECTORY";
    }

    private string GetDomainDnsName()
    {
        var parts = _options.DefaultDomain.Split(',');
        var labels = parts
            .Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);
        return string.Join(".", labels);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _udpClient?.Dispose();
    }
}
