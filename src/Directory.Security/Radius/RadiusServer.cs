using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Radius;

/// <summary>
/// RADIUS authentication server (RFC 2865) providing network device authentication
/// against the directory service. Implements PAP authentication with shared-secret
/// password decryption and MD5 response authenticator computation.
///
/// Runs as an IHostedService, listening on UDP port 1812 (configurable).
/// </summary>
public class RadiusServer : IHostedService, IDisposable
{
    private const string RadiusAttributeName = "msDS-RadiusData";

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IAuditService _audit;
    private readonly ILogger<RadiusServer> _logger;
    private UdpClient _udpClient;
    private CancellationTokenSource _cts;
    private Task _listenTask;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public RadiusServer(
        IDirectoryStore store,
        INamingContextService ncService,
        IPasswordPolicy passwordPolicy,
        IAuditService audit,
        ILogger<RadiusServer> logger)
    {
        _store = store;
        _ncService = ncService;
        _passwordPolicy = passwordPolicy;
        _audit = audit;
        _logger = logger;
    }

    // ── IHostedService ───────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var data = await LoadRadiusDataAsync(CancellationToken.None);
        if (!data.Settings.Enabled)
        {
            _logger.LogInformation("RADIUS server is disabled");
            return;
        }

        StartListening(data.Settings.Port);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopListening();
        return Task.CompletedTask;
    }

    // ── Settings Management ──────────────────────────────────────

    public async Task<RadiusSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);
        return data.Settings;
    }

    public async Task<RadiusSettings> UpdateSettingsAsync(RadiusSettings settings, CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);
        var wasEnabled = data.Settings.Enabled;
        var oldPort = data.Settings.Port;

        data.Settings.Enabled = settings.Enabled;
        data.Settings.Port = settings.Port;
        data.Settings.AccountingPort = settings.AccountingPort;

        await SaveRadiusDataAsync(data, ct);

        // Restart listener if needed
        if (settings.Enabled && (!wasEnabled || settings.Port != oldPort))
        {
            StopListening();
            StartListening(settings.Port);
        }
        else if (!settings.Enabled && wasEnabled)
        {
            StopListening();
        }

        _logger.LogInformation("RADIUS settings updated: Enabled={Enabled}, Port={Port}", settings.Enabled, settings.Port);
        return data.Settings;
    }

    // ── Client Management ────────────────────────────────────────

    public async Task<List<RadiusClient>> GetClientsAsync(CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);
        return data.Settings.Clients;
    }

    public async Task<RadiusClient> AddClientAsync(RadiusClient client, CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);

        client.Id = Guid.NewGuid().ToString();
        data.Settings.Clients.Add(client);

        await SaveRadiusDataAsync(data, ct);
        _logger.LogInformation("RADIUS client added: {Name} ({Ip})", client.Name, client.IpAddress);

        return client;
    }

    public async Task<RadiusClient> UpdateClientAsync(string clientId, RadiusClient updated, CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);
        var existing = data.Settings.Clients.FirstOrDefault(c => c.Id == clientId)
            ?? throw new InvalidOperationException($"RADIUS client not found: {clientId}");

        existing.Name = updated.Name;
        existing.IpAddress = updated.IpAddress;
        existing.SharedSecret = updated.SharedSecret;
        existing.Description = updated.Description;
        existing.IsEnabled = updated.IsEnabled;

        await SaveRadiusDataAsync(data, ct);
        _logger.LogInformation("RADIUS client updated: {Name}", existing.Name);

        return existing;
    }

    public async Task<bool> DeleteClientAsync(string clientId, CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);
        var client = data.Settings.Clients.FirstOrDefault(c => c.Id == clientId);
        if (client == null) return false;

        data.Settings.Clients.Remove(client);
        await SaveRadiusDataAsync(data, ct);
        _logger.LogInformation("RADIUS client deleted: {Name}", client.Name);

        return true;
    }

    // ── Log ──────────────────────────────────────────────────────

    public async Task<List<RadiusLogEntry>> GetLogAsync(CancellationToken ct = default)
    {
        var data = await LoadRadiusDataAsync(ct);
        return data.Log.OrderByDescending(l => l.Timestamp).ToList();
    }

    // ── UDP Listener ─────────────────────────────────────────────

    private void StartListening(int port)
    {
        _cts = new CancellationTokenSource();
        try
        {
            _udpClient = new UdpClient(port);
            _listenTask = ListenLoopAsync(_cts.Token);
            _logger.LogInformation("RADIUS server listening on UDP port {Port}", port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start RADIUS listener on port {Port}", port);
        }
    }

    private void StopListening()
    {
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                _ = ProcessPacketAsync(result.Buffer, result.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RADIUS listener error");
            }
        }
    }

    private async Task ProcessPacketAsync(byte[] packet, IPEndPoint remoteEp, CancellationToken ct)
    {
        try
        {
            if (packet.Length < RadiusConstants.MinPacketLength)
            {
                _logger.LogWarning("RADIUS: Dropping malformed packet from {Ip} (too short)", remoteEp.Address);
                return;
            }

            var code = packet[0];
            var identifier = packet[1];
            var length = (packet[2] << 8) | packet[3];
            var authenticator = packet[4..20];

            if (code != RadiusConstants.AccessRequest)
            {
                _logger.LogDebug("RADIUS: Ignoring non-Access-Request (code={Code}) from {Ip}", code, remoteEp.Address);
                return;
            }

            // Find matching client by IP
            var data = await LoadRadiusDataAsync(ct);
            var clientIp = remoteEp.Address.ToString();
            var client = data.Settings.Clients.FirstOrDefault(c =>
                c.IsEnabled && c.IpAddress == clientIp);

            if (client == null)
            {
                _logger.LogWarning("RADIUS: Unknown client IP: {Ip}", clientIp);
                return;
            }

            // Parse attributes
            string username = null;
            byte[] encryptedPassword = null;

            int offset = RadiusConstants.HeaderLength;
            while (offset < length && offset < packet.Length)
            {
                if (offset + 2 > packet.Length) break;
                var attrType = packet[offset];
                var attrLen = packet[offset + 1];
                if (attrLen < 2 || offset + attrLen > packet.Length) break;

                var attrValue = packet[(offset + 2)..(offset + attrLen)];

                switch (attrType)
                {
                    case RadiusConstants.AttrUserName:
                        username = Encoding.UTF8.GetString(attrValue);
                        break;
                    case RadiusConstants.AttrUserPassword:
                        encryptedPassword = attrValue;
                        break;
                }

                offset += attrLen;
            }

            if (string.IsNullOrEmpty(username) || encryptedPassword == null)
            {
                _logger.LogWarning("RADIUS: Missing User-Name or User-Password from {Ip}", clientIp);
                var rejectPkt = BuildResponse(RadiusConstants.AccessReject, identifier, authenticator,
                    client.SharedSecret, "Missing credentials");
                await _udpClient.SendAsync(rejectPkt, rejectPkt.Length, remoteEp);
                return;
            }

            // Decrypt PAP password: XOR password blocks with MD5(secret + authenticator + prev_block)
            var password = DecryptPapPassword(encryptedPassword, client.SharedSecret, authenticator);

            // Authenticate against directory
            var domainDn = _ncService.GetDomainNc().Dn;
            var userObj = await _store.GetBySamAccountNameAsync("default", domainDn, username, ct);

            bool authResult = false;
            string failReason = null;

            if (userObj == null)
            {
                failReason = "User not found";
            }
            else
            {
                try
                {
                    authResult = await _passwordPolicy.ValidatePasswordAsync("default", userObj.DistinguishedName, password, ct);
                    if (!authResult) failReason = "Invalid password";
                }
                catch (Exception ex)
                {
                    failReason = $"Auth error: {ex.Message}";
                }
            }

            // Build response
            byte[] response;
            if (authResult)
            {
                response = BuildResponse(RadiusConstants.AccessAccept, identifier, authenticator,
                    client.SharedSecret, "Access granted");
                _logger.LogInformation("RADIUS: Access-Accept for {User} from {Client}", username, client.Name);
            }
            else
            {
                response = BuildResponse(RadiusConstants.AccessReject, identifier, authenticator,
                    client.SharedSecret, failReason ?? "Authentication failed");
                _logger.LogInformation("RADIUS: Access-Reject for {User} from {Client}: {Reason}",
                    username, client.Name, failReason);
            }

            await _udpClient.SendAsync(response, response.Length, remoteEp);

            // Log the attempt
            await LogAuthAttemptAsync(data, clientIp, client.Name, username, authResult, failReason, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RADIUS: Error processing packet from {Ip}", remoteEp.Address);
        }
    }

    // ── RADIUS Protocol Helpers ──────────────────────────────────

    /// <summary>
    /// Decrypt a PAP-encrypted password per RFC 2865 Section 5.2.
    /// The password is XOR'd with MD5(SharedSecret + RequestAuthenticator) in 16-byte blocks.
    /// </summary>
    private static string DecryptPapPassword(byte[] encrypted, string sharedSecret, byte[] authenticator)
    {
        var secretBytes = Encoding.UTF8.GetBytes(sharedSecret);
        var result = new byte[encrypted.Length];

        byte[] prevBlock = authenticator;

        for (int i = 0; i < encrypted.Length; i += 16)
        {
            var blockLen = Math.Min(16, encrypted.Length - i);

            var hashInput = new byte[secretBytes.Length + prevBlock.Length];
            Buffer.BlockCopy(secretBytes, 0, hashInput, 0, secretBytes.Length);
            Buffer.BlockCopy(prevBlock, 0, hashInput, secretBytes.Length, prevBlock.Length);

            var hash = MD5.HashData(hashInput);

            for (int j = 0; j < blockLen; j++)
                result[i + j] = (byte)(encrypted[i + j] ^ hash[j]);

            prevBlock = encrypted[i..(i + 16)];
        }

        // Trim null padding
        var end = Array.IndexOf(result, (byte)0);
        if (end < 0) end = result.Length;

        return Encoding.UTF8.GetString(result, 0, end);
    }

    /// <summary>
    /// Build a RADIUS response packet with Response Authenticator = MD5(Code+ID+Length+RequestAuth+Attributes+Secret).
    /// </summary>
    private static byte[] BuildResponse(byte code, byte identifier, byte[] requestAuthenticator,
        string sharedSecret, string replyMessage = null)
    {
        var attributes = new List<byte>();

        // Add Reply-Message attribute if provided
        if (!string.IsNullOrEmpty(replyMessage))
        {
            var msgBytes = Encoding.UTF8.GetBytes(replyMessage);
            var attrLen = (byte)(2 + msgBytes.Length);
            attributes.Add(RadiusConstants.AttrReplyMessage);
            attributes.Add(attrLen);
            attributes.AddRange(msgBytes);
        }

        var totalLength = (ushort)(RadiusConstants.HeaderLength + attributes.Count);

        // Build packet with zeroed authenticator for hash computation
        var packet = new byte[totalLength];
        packet[0] = code;
        packet[1] = identifier;
        packet[2] = (byte)(totalLength >> 8);
        packet[3] = (byte)(totalLength & 0xFF);

        // Copy request authenticator temporarily for hash computation
        Buffer.BlockCopy(requestAuthenticator, 0, packet, 4, RadiusConstants.AuthenticatorLength);

        // Copy attributes
        if (attributes.Count > 0)
            Buffer.BlockCopy(attributes.ToArray(), 0, packet, RadiusConstants.HeaderLength, attributes.Count);

        // Compute Response Authenticator = MD5(Code+ID+Length+RequestAuth+Attributes+Secret)
        var secretBytes = Encoding.UTF8.GetBytes(sharedSecret);
        var hashInput = new byte[packet.Length + secretBytes.Length];
        Buffer.BlockCopy(packet, 0, hashInput, 0, packet.Length);
        Buffer.BlockCopy(secretBytes, 0, hashInput, packet.Length, secretBytes.Length);

        var responseAuth = MD5.HashData(hashInput);
        Buffer.BlockCopy(responseAuth, 0, packet, 4, RadiusConstants.AuthenticatorLength);

        return packet;
    }

    // ── Persistence ──────────────────────────────────────────────

    internal async Task<RadiusData> LoadRadiusDataAsync(CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var obj = await _store.GetByDnAsync("default", domainDn, ct);
        if (obj == null) return new RadiusData();

        if (obj.Attributes.TryGetValue(RadiusAttributeName, out var attr) &&
            attr.Values.Count > 0 &&
            attr.Values[0] is string val && !string.IsNullOrWhiteSpace(val))
        {
            try
            {
                return JsonSerializer.Deserialize<RadiusData>(val, JsonOpts) ?? new RadiusData();
            }
            catch
            {
                return new RadiusData();
            }
        }

        return new RadiusData();
    }

    internal async Task SaveRadiusDataAsync(RadiusData data, CancellationToken ct)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var obj = await _store.GetByDnAsync("default", domainDn, ct);
        if (obj == null) return;

        var json = JsonSerializer.Serialize(data, JsonOpts);
        obj.Attributes[RadiusAttributeName] = new DirectoryAttribute(RadiusAttributeName, json);
        obj.WhenChanged = DateTimeOffset.UtcNow;

        await _store.UpdateAsync(obj, ct);
    }

    private async Task LogAuthAttemptAsync(
        RadiusData data, string clientIp, string clientName, string username, bool success, string reason, CancellationToken ct)
    {
        data.Log.Add(new RadiusLogEntry
        {
            ClientIp = clientIp,
            ClientName = clientName,
            Username = username,
            Success = success,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow,
        });

        // Trim log to max entries
        while (data.Log.Count > RadiusConstants.MaxLogEntries)
            data.Log.RemoveAt(0);

        await SaveRadiusDataAsync(data, ct);

        try
        {
            await _audit.LogAsync(new AuditEntry
            {
                TenantId = "default",
                Action = success ? "RadiusAuthSuccess" : "RadiusAuthFailure",
                TargetDn = username,
                TargetObjectClass = "radiusAuth",
                ActorIdentity = clientName ?? clientIp,
                Details = new()
                {
                    ["clientIp"] = clientIp,
                    ["username"] = username,
                    ["reason"] = reason ?? "",
                },
            }, ct);
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        StopListening();
    }
}
