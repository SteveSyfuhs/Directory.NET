using System.Net;
using System.Net.Sockets;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Kerberos.NET;
using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Kerberos;

/// <summary>
/// Kerberos password change service (kpasswd) on port 464.
/// Implements RFC 3244 (Microsoft Windows 2000 Kerberos Change Password and Set Password Protocols).
/// Listens on both UDP and TCP as required by the protocol.
/// </summary>
public class KpasswdService : IHostedService, IDisposable
{
    private const int MaxMessageSize = 65535;
    private const int MinMessageSize = 6;

    // RFC 3244 result codes
    private const int KRB5_KPASSWD_SUCCESS = 0;
    private const int KRB5_KPASSWD_MALFORMED = 1;
    private const int KRB5_KPASSWD_HARDERROR = 2;
    private const int KRB5_KPASSWD_AUTHERROR = 3;
    private const int KRB5_KPASSWD_SOFTERROR = 4;
    private const int KRB5_KPASSWD_ACCESSDENIED = 5;
    private const int KRB5_KPASSWD_BAD_VERSION = 6;
    private const int KRB5_KPASSWD_INITIAL_FLAG_NEEDED = 7;

    private readonly KerberosOptions _options;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly IDirectoryStore _store;
    private readonly ILogger<KpasswdService> _logger;
    private UdpClient _udpListener;
    private TcpListener _tcpListener;
    private CancellationTokenSource _cts;

    public KpasswdService(
        IOptions<KerberosOptions> options,
        IPasswordPolicy passwordPolicy,
        IDirectoryStore store,
        ILogger<KpasswdService> logger)
    {
        _options = options.Value;
        _passwordPolicy = passwordPolicy;
        _store = store;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var kpasswdPort = _options.KpasswdPort;

        _udpListener = new UdpClient(new IPEndPoint(IPAddress.IPv6Any, kpasswdPort));
        _udpListener.Client.DualMode = true;

        _tcpListener = new TcpListener(IPAddress.IPv6Any, kpasswdPort);
        _tcpListener.Server.DualMode = true;
        _tcpListener.Start();

        _logger.LogInformation("Kpasswd service started on port {Port} (UDP+TCP)", kpasswdPort);

        _ = Task.Run(() => ListenUdpAsync(_cts.Token), _cts.Token);
        _ = Task.Run(() => ListenTcpAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    private async Task ListenUdpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpListener.ReceiveAsync(ct);
                _logger.LogDebug("Kpasswd UDP request from {Remote} ({Length} bytes)",
                    result.RemoteEndPoint, result.Buffer.Length);

                var response = await ProcessKpasswdRequestAsync(result.Buffer, ct);
                if (response != null)
                    await _udpListener.SendAsync(response, result.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing kpasswd UDP request");
            }
        }
    }

    private async Task ListenTcpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener.AcceptTcpClientAsync(ct);
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            await HandleTcpClientAsync(client, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error handling kpasswd TCP client");
                        }
                    }
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting kpasswd TCP connection");
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();

        // TCP framing: 4-byte big-endian length prefix
        var lenBuf = new byte[4];
        if (await ReadExactAsync(stream, lenBuf, ct) != 4) return;

        var len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf, 0));
        if (len <= 0 || len > MaxMessageSize) return;

        var data = new byte[len];
        if (await ReadExactAsync(stream, data, ct) != len) return;

        var response = await ProcessKpasswdRequestAsync(data, ct);
        if (response != null)
        {
            var respLen = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(response.Length));
            await stream.WriteAsync(respLen, ct);
            await stream.WriteAsync(response, ct);
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0) return read;
            read += n;
        }
        return read;
    }

    private async Task<byte[]> ProcessKpasswdRequestAsync(byte[] data, CancellationToken ct)
    {
        // RFC 3244 kpasswd message format:
        //   message length (2 bytes, big-endian)
        //   protocol version (2 bytes) — 1 for RFC 3244, 0xFF80 for set-password
        //   AP-REQ length (2 bytes)
        //   AP-REQ data
        //   KRB-PRIV data (contains the new password)

        if (data.Length < MinMessageSize)
            return BuildErrorResponse(KRB5_KPASSWD_MALFORMED, "Request too short");

        var offset = 0;
        var msgLen = (data[offset] << 8) | data[offset + 1]; offset += 2;
        var version = (data[offset] << 8) | data[offset + 1]; offset += 2;
        var apReqLen = (data[offset] << 8) | data[offset + 1]; offset += 2;

        if (version != 1 && version != 0xFF80)
            return BuildErrorResponse(KRB5_KPASSWD_BAD_VERSION, "Unsupported protocol version");

        if (offset + apReqLen > data.Length)
            return BuildErrorResponse(KRB5_KPASSWD_MALFORMED, "Malformed request: AP-REQ extends beyond message");

        _logger.LogDebug(
            "Kpasswd request received (version={Version}, msgLen={MsgLen}, apReqLen={ApReqLen})",
            version, msgLen, apReqLen);

        var apReqBytes = data[offset..(offset + apReqLen)];
        var krbPrivBytes = data[(offset + apReqLen)..];

        if (krbPrivBytes.Length == 0)
            return BuildErrorResponse(KRB5_KPASSWD_MALFORMED, "Missing KRB-PRIV message");

        try
        {
            // Step 1: Look up the kpasswd service principal to get the service key
            var tenantId = "default";
            var domainDn = RealmToDomainDn(_options.DefaultRealm);

            // The kpasswd service uses the kadmin/changepw principal or krbtgt
            var servicePrincipal = await _store.GetBySamAccountNameAsync(tenantId, domainDn, "krbtgt", ct);
            if (servicePrincipal == null)
            {
                _logger.LogError("krbtgt service account not found for realm {Realm}", _options.DefaultRealm);
                return BuildErrorResponse(KRB5_KPASSWD_HARDERROR, "Service configuration error");
            }

            // Retrieve the service key from the krbtgt principal
            var serviceKey = GetKerberosKeyFromPrincipal(servicePrincipal);

            // Step 2: Validate the AP-REQ using Kerberos.NET
            var validator = new KerberosValidator(serviceKey)
            {
                ValidateAfterDecrypt = ValidationActions.All & ~ValidationActions.Replay,
            };

            var decryptedApReq = await validator.Validate(apReqBytes);

            // Extract principal name from the ticket's CName field
            var principalName = decryptedApReq.Ticket.CName.FullyQualifiedName;

            _logger.LogInformation("Kpasswd: authenticated principal {Principal}", principalName);

            // Step 3: Decrypt the KRB-PRIV to extract the new password.
            // The KRB-PRIV is encrypted with the session key from the AP-REQ exchange.
            // For RFC 3244 version 1, the user-data inside KRB-PRIV is the new password in UTF-8.
            string newPassword;
            try
            {
                var krbPriv = KrbPriv.DecodeApplication(krbPrivBytes);
                var sessionKey = decryptedApReq.SessionKey;

                var decryptedPart = krbPriv.EncPart.Decrypt(
                    sessionKey,
                    KeyUsage.EncKrbPrivPart,
                    d => KrbEncKrbPrivPart.DecodeApplication(d));

                newPassword = Encoding.UTF8.GetString(decryptedPart.UserData.Span);

                // Strip any null terminators
                newPassword = newPassword.TrimEnd('\0');
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt KRB-PRIV for password change");
                return BuildErrorResponse(KRB5_KPASSWD_AUTHERROR, "Failed to decrypt password data");
            }

            if (string.IsNullOrEmpty(newPassword))
                return BuildErrorResponse(KRB5_KPASSWD_SOFTERROR, "Empty password not allowed");

            // Step 4: Look up the target principal for the password change
            // For version 1 (change own password), the target is the authenticated principal
            var samAccountName = principalName.Split('@')[0];
            var targetUser = await _store.GetBySamAccountNameAsync(tenantId, domainDn, samAccountName, ct);

            if (targetUser == null)
            {
                // Try by UPN
                targetUser = await _store.GetByUpnAsync(tenantId, principalName, ct);
            }

            if (targetUser == null)
            {
                _logger.LogWarning("Kpasswd: target user not found for principal {Principal}", principalName);
                return BuildErrorResponse(KRB5_KPASSWD_HARDERROR, "User not found");
            }

            // Step 5: Validate password complexity
            if (!_passwordPolicy.MeetsComplexityRequirements(newPassword, targetUser.SAMAccountName))
            {
                _logger.LogInformation("Kpasswd: password does not meet complexity requirements for {Principal}", principalName);
                return BuildErrorResponse(KRB5_KPASSWD_SOFTERROR, "Password does not meet complexity requirements");
            }

            // Step 6: Set the new password
            await _passwordPolicy.SetPasswordAsync(tenantId, targetUser.DistinguishedName, newPassword, ct);

            _logger.LogInformation("Kpasswd: password changed successfully for {Principal}", principalName);
            return BuildErrorResponse(KRB5_KPASSWD_SUCCESS, "Password changed");
        }
        catch (KerberosValidationException ex)
        {
            _logger.LogWarning(ex, "Kpasswd: Kerberos authentication failed");
            return BuildErrorResponse(KRB5_KPASSWD_AUTHERROR, "Authentication failed: " + ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kpasswd: unexpected error during password change");
            return BuildErrorResponse(KRB5_KPASSWD_HARDERROR, "Internal error");
        }
    }

    private static byte[] BuildErrorResponse(int resultCode, string resultString)
    {
        // Error response format (when AP-REP length is 0):
        //   message length (2 bytes)
        //   protocol version (2 bytes) — always 1
        //   AP-REP length (2 bytes) — 0 for error responses
        //   result data: result code (2 bytes) + result string (UTF-8)
        var errorBytes = System.Text.Encoding.UTF8.GetBytes(resultString);
        var resultData = new byte[2 + errorBytes.Length];
        resultData[0] = (byte)(resultCode >> 8);
        resultData[1] = (byte)resultCode;
        Array.Copy(errorBytes, 0, resultData, 2, errorBytes.Length);

        var totalLen = 6 + resultData.Length;
        var response = new byte[totalLen];
        response[0] = (byte)(totalLen >> 8);
        response[1] = (byte)totalLen;
        response[2] = 0; response[3] = 1; // protocol version = 1
        response[4] = 0; response[5] = 0; // AP-REP length = 0 (error case)
        Array.Copy(resultData, 0, response, 6, resultData.Length);
        return response;
    }

    private KerberosKey GetKerberosKeyFromPrincipal(DirectoryObject principal)
    {
        if (principal.KerberosKeys.Count > 0)
        {
            foreach (var keyData in principal.KerberosKeys)
            {
                var parts = keyData.Split(':');
                if (parts.Length != 2) continue;
                if (!int.TryParse(parts[0], out var etype)) continue;
                var keyBytes = Convert.FromBase64String(parts[1]);

                return new KerberosKey(
                    keyBytes,
                    principal: new PrincipalName(
                        PrincipalNameType.NT_SRV_INST,
                        _options.DefaultRealm,
                        new[] { "krbtgt", _options.DefaultRealm }),
                    etype: (EncryptionType)etype);
            }
        }

        if (!string.IsNullOrEmpty(principal.NTHash))
        {
            var ntHash = Convert.FromHexString(principal.NTHash);
            return new KerberosKey(
                ntHash,
                principal: new PrincipalName(
                    PrincipalNameType.NT_SRV_INST,
                    _options.DefaultRealm,
                    new[] { "krbtgt", _options.DefaultRealm }),
                etype: EncryptionType.RC4_HMAC_NT);
        }

        throw new InvalidOperationException("No credentials available for krbtgt");
    }

    private static string RealmToDomainDn(string realm)
    {
        var parts = realm.ToLowerInvariant().Split('.');
        return string.Join(",", parts.Select(p => $"DC={p}"));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _udpListener?.Close();
        _tcpListener?.Stop();
        _logger.LogInformation("Kpasswd service stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _udpListener?.Dispose();
        _tcpListener?.Stop();
        _cts?.Dispose();
    }
}
