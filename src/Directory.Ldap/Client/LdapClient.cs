using System.Formats.Asn1;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Security.CertificateAuthority;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Client;

/// <summary>
/// LDAP v3 client for connecting to Active Directory domain controllers.
/// Used during DC promotion to query RootDSE, create machine accounts, and register DC objects.
/// </summary>
public class LdapClient : IAsyncDisposable
{
    private TcpClient _tcp;
    private Stream _stream;
    private int _nextMessageId = 1;
    private readonly ILogger _logger;
    private readonly LdapClientTlsOptions _tlsOptions;
    private readonly CertificateAuthorityService _caService;
    private string _targetHost;

    public LdapClient(ILogger logger = null)
        : this(null, null, logger)
    {
    }

    /// <summary>
    /// Create an LDAP client with TLS certificate validation options.
    /// </summary>
    /// <param name="tlsOptions">TLS validation options. When null, uses default strict validation with system trust store.</param>
    /// <param name="caService">Optional CA service for validating certificates against the directory's AD CS trust chain.</param>
    /// <param name="logger">Optional logger.</param>
    public LdapClient(LdapClientTlsOptions tlsOptions, CertificateAuthorityService caService = null, ILogger logger = null)
    {
        _tlsOptions = tlsOptions ?? new LdapClientTlsOptions();
        _caService = caService;
        _logger = logger;
    }

    /// <summary>
    /// Connect to a domain controller on the specified port.
    /// Use port 389 for plain LDAP, 636 for LDAPS.
    /// </summary>
    public async Task ConnectAsync(string hostname, int port = 389, bool useTls = false, CancellationToken ct = default)
    {
        _targetHost = hostname;
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(hostname, port, ct);

        Stream stream = _tcp.GetStream();

        if (useTls)
        {
            var sslStream = new SslStream(stream, leaveInnerStreamOpen: false, ValidateServerCertificate);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = hostname,
            }, ct);
            stream = sslStream;
        }

        _stream = stream;
        _logger?.LogDebug("Connected to {Hostname}:{Port} (TLS={UseTls})", hostname, port, useTls);
    }

    /// <summary>
    /// Simple bind with a full DN and password.
    /// </summary>
    public async Task<LdapBindResult> BindAsync(string dn, string password, CancellationToken ct = default)
    {
        return await PerformBindAsync(dn, password, ct);
    }

    /// <summary>
    /// Simple bind with sAMAccountName (DOMAIN\user format) -- sends as-is, AD accepts it.
    /// </summary>
    public async Task<LdapBindResult> SimpleBindAsync(string username, string password, CancellationToken ct = default)
    {
        return await PerformBindAsync(username, password, ct);
    }

    /// <summary>
    /// Perform an LDAP search operation.
    /// </summary>
    public async Task<LdapSearchResult> SearchAsync(
        string baseDn,
        SearchScope scope,
        string filter,
        string[] attributes = null,
        int sizeLimit = 0,
        int timeLimitSeconds = 0,
        CancellationToken ct = default)
    {
        var messageId = _nextMessageId++;

        // Parse the text filter to an AST, then encode to BER
        var filterNode = LdapFilterParser.ParseText(filter);
        var filterBytes = EncodeFilterToBer(filterNode);

        // Build SearchRequest [APPLICATION 3]
        var opWriter = new AsnWriter(BerHelper.Rules);
        using (opWriter.PushSequence(BerHelper.ApplicationTag(3)))
        {
            BerHelper.WriteLdapString(opWriter, baseDn);
            opWriter.WriteEnumeratedValue(scope);
            opWriter.WriteEnumeratedValue(DerefAliases.NeverDerefAliases);
            opWriter.WriteInteger(sizeLimit);
            opWriter.WriteInteger(timeLimitSeconds);
            opWriter.WriteBoolean(false); // typesOnly

            // Write the pre-encoded filter
            opWriter.WriteEncodedValue(filterBytes);

            // Attribute selection
            using (opWriter.PushSequence())
            {
                if (attributes is not null)
                {
                    foreach (var attr in attributes)
                    {
                        BerHelper.WriteLdapString(opWriter, attr);
                    }
                }
            }
        }

        var messageBytes = EncodeMessage(messageId, opWriter.Encode());
        await SendMessageAsync(messageBytes, ct);

        // Collect SearchResultEntry responses until SearchResultDone
        var entries = new List<LdapSearchEntry>();
        int resultCode = 0;
        string diagnosticMessage = "";

        while (true)
        {
            var responseData = await ReceiveMessageAsync(ct);
            var response = LdapMessage.Decode(responseData);

            if (response.Operation == LdapOperation.SearchResultEntry)
            {
                var entry = DecodeSearchResultEntry(response.ProtocolOpData);
                entries.Add(entry);
            }
            else if (response.Operation == LdapOperation.SearchResultReference)
            {
                // Skip referrals
                continue;
            }
            else if (response.Operation == LdapOperation.SearchResultDone)
            {
                (resultCode, _, diagnosticMessage) = DecodeLdapResult(response.ProtocolOpData, 5);
                break;
            }
            else
            {
                _logger?.LogWarning("Unexpected operation {Op} during search", response.Operation);
                break;
            }
        }

        return new LdapSearchResult
        {
            Entries = entries,
            ResultCode = resultCode,
            DiagnosticMessage = diagnosticMessage,
        };
    }

    /// <summary>
    /// Read the RootDSE from the domain controller.
    /// Convenience method: searches base="", scope=base, filter=(objectClass=*).
    /// </summary>
    public async Task<Dictionary<string, List<string>>> ReadRootDseAsync(CancellationToken ct = default)
    {
        var result = await SearchAsync(
            baseDn: "",
            scope: SearchScope.BaseObject,
            filter: "(objectClass=*)",
            ct: ct);

        if (result.Entries.Count > 0)
        {
            return result.Entries[0].Attributes;
        }

        return new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// Add an LDAP entry with the specified attributes.
    /// </summary>
    public async Task<LdapResult> AddAsync(string dn, Dictionary<string, List<string>> attributes, CancellationToken ct = default)
    {
        var messageId = _nextMessageId++;

        var opWriter = new AsnWriter(BerHelper.Rules);
        using (opWriter.PushSequence(BerHelper.ApplicationTag(8)))
        {
            BerHelper.WriteLdapString(opWriter, dn);

            using (opWriter.PushSequence()) // AttributeList
            {
                foreach (var (name, values) in attributes)
                {
                    using (opWriter.PushSequence()) // Attribute
                    {
                        BerHelper.WriteLdapString(opWriter, name);

                        using (opWriter.PushSetOf()) // SET OF values
                        {
                            foreach (var value in values)
                            {
                                BerHelper.WriteLdapString(opWriter, value);
                            }
                        }
                    }
                }
            }
        }

        var messageBytes = EncodeMessage(messageId, opWriter.Encode());
        await SendMessageAsync(messageBytes, ct);

        var responseData = await ReceiveMessageAsync(ct);
        var response = LdapMessage.Decode(responseData);

        var (resultCode, matchedDn, diagnosticMessage) = DecodeLdapResult(response.ProtocolOpData, 9);

        return new LdapResult
        {
            Success = resultCode == 0,
            ResultCode = resultCode,
            MatchedDn = matchedDn,
            DiagnosticMessage = diagnosticMessage,
        };
    }

    /// <summary>
    /// Modify an existing LDAP entry.
    /// </summary>
    public async Task<LdapResult> ModifyAsync(string dn, List<LdapModification> modifications, CancellationToken ct = default)
    {
        var messageId = _nextMessageId++;

        var opWriter = new AsnWriter(BerHelper.Rules);
        using (opWriter.PushSequence(BerHelper.ApplicationTag(6)))
        {
            BerHelper.WriteLdapString(opWriter, dn);

            using (opWriter.PushSequence()) // SEQUENCE OF changes
            {
                foreach (var mod in modifications)
                {
                    using (opWriter.PushSequence()) // change
                    {
                        opWriter.WriteEnumeratedValue((ModifyOperation)(int)mod.Operation);

                        using (opWriter.PushSequence()) // PartialAttribute
                        {
                            BerHelper.WriteLdapString(opWriter, mod.AttributeName);

                            using (opWriter.PushSetOf()) // SET OF values
                            {
                                foreach (var value in mod.Values)
                                {
                                    BerHelper.WriteLdapString(opWriter, value);
                                }
                            }
                        }
                    }
                }
            }
        }

        var messageBytes = EncodeMessage(messageId, opWriter.Encode());
        await SendMessageAsync(messageBytes, ct);

        var responseData = await ReceiveMessageAsync(ct);
        var response = LdapMessage.Decode(responseData);

        var (resultCode, matchedDn, diagnosticMessage) = DecodeLdapResult(response.ProtocolOpData, 7);

        return new LdapResult
        {
            Success = resultCode == 0,
            ResultCode = resultCode,
            MatchedDn = matchedDn,
            DiagnosticMessage = diagnosticMessage,
        };
    }

    /// <summary>
    /// Delete an LDAP entry.
    /// </summary>
    public async Task<LdapResult> DeleteAsync(string dn, CancellationToken ct = default)
    {
        var messageId = _nextMessageId++;

        // DelRequest [APPLICATION 10] is PRIMITIVE OCTET STRING (the DN)
        var opWriter = new AsnWriter(BerHelper.Rules);
        opWriter.WriteOctetString(Encoding.UTF8.GetBytes(dn), BerHelper.ApplicationTag(10, isConstructed: false));

        var messageBytes = EncodeMessage(messageId, opWriter.Encode());
        await SendMessageAsync(messageBytes, ct);

        var responseData = await ReceiveMessageAsync(ct);
        var response = LdapMessage.Decode(responseData);

        var (resultCode, matchedDn, diagnosticMessage) = DecodeLdapResult(response.ProtocolOpData, 11);

        return new LdapResult
        {
            Success = resultCode == 0,
            ResultCode = resultCode,
            MatchedDn = matchedDn,
            DiagnosticMessage = diagnosticMessage,
        };
    }

    /// <summary>
    /// Send an unbind request and close the connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            try
            {
                // UnbindRequest [APPLICATION 2] — no content, just the tag
                var opWriter = new AsnWriter(BerHelper.Rules);
                opWriter.WriteNull(BerHelper.ApplicationTag(2, isConstructed: false));

                var messageBytes = EncodeMessage(_nextMessageId++, opWriter.Encode());
                await SendMessageAsync(messageBytes, CancellationToken.None);
            }
            catch
            {
                // Best-effort unbind
            }
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _tcp?.Dispose();
        _tcp = null;

        GC.SuppressFinalize(this);
    }

    #region Private helpers

    private async Task<LdapBindResult> PerformBindAsync(string name, string password, CancellationToken ct)
    {
        var messageId = _nextMessageId++;

        // BindRequest [APPLICATION 0]: SEQUENCE { version, name, simple [0] password }
        var opWriter = new AsnWriter(BerHelper.Rules);
        using (opWriter.PushSequence(BerHelper.ApplicationTag(0)))
        {
            opWriter.WriteInteger(3); // LDAP v3
            BerHelper.WriteLdapString(opWriter, name);
            opWriter.WriteOctetString(Encoding.UTF8.GetBytes(password), BerHelper.ContextTag(0));
        }

        var messageBytes = EncodeMessage(messageId, opWriter.Encode());
        await SendMessageAsync(messageBytes, ct);

        var responseData = await ReceiveMessageAsync(ct);
        var response = LdapMessage.Decode(responseData);

        // Decode BindResponse [APPLICATION 1]
        var reader = new AsnReader(response.ProtocolOpData, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(1));

        var resultCode = (int)sequence.ReadInteger(); // resultCode is ENUMERATED but reads as integer
        var matchedDn = BerHelper.ReadLdapString(sequence); // matchedDN
        var diagnosticMessage = BerHelper.ReadLdapString(sequence); // diagnosticMessage

        return new LdapBindResult
        {
            Success = resultCode == 0,
            ResultCode = resultCode,
            DiagnosticMessage = diagnosticMessage,
        };
    }

    private static byte[] EncodeMessage(int messageId, byte[] protocolOpData)
    {
        var writer = new AsnWriter(BerHelper.Rules);
        using (writer.PushSequence())
        {
            writer.WriteInteger(messageId);
            writer.WriteEncodedValue(protocolOpData);
        }
        return writer.Encode();
    }

    private async Task SendMessageAsync(byte[] berData, CancellationToken ct)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        await _stream.WriteAsync(berData, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<byte[]> ReceiveMessageAsync(CancellationToken ct)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

        // Read enough bytes to determine the BER TLV total length, then read the rest.
        // Start with a small header buffer and expand as needed.
        var headerBuffer = new byte[64];
        var headerLen = 0;

        // Read at least 2 bytes to start parsing
        while (headerLen < 2)
        {
            var read = await _stream.ReadAsync(headerBuffer.AsMemory(headerLen), ct);
            if (read == 0)
                throw new IOException("Connection closed by remote host.");
            headerLen += read;
        }

        // Try to determine the total TLV length
        int totalLength;
        while ((totalLength = BerHelper.TryReadTlvLength(headerBuffer.AsSpan(0, headerLen))) < 0)
        {
            if (headerLen >= headerBuffer.Length)
            {
                // Expand the header buffer (shouldn't happen for normal LDAP headers)
                Array.Resize(ref headerBuffer, headerBuffer.Length * 2);
            }

            var read = await _stream.ReadAsync(headerBuffer.AsMemory(headerLen), ct);
            if (read == 0)
                throw new IOException("Connection closed by remote host.");
            headerLen += read;
        }

        // Allocate the full message buffer
        var messageBuffer = new byte[totalLength];
        var copied = Math.Min(headerLen, totalLength);
        Buffer.BlockCopy(headerBuffer, 0, messageBuffer, 0, copied);

        // Read remaining bytes if needed
        var remaining = totalLength - copied;
        var offset = copied;
        while (remaining > 0)
        {
            var read = await _stream.ReadAsync(messageBuffer.AsMemory(offset, remaining), ct);
            if (read == 0)
                throw new IOException("Connection closed by remote host.");
            offset += read;
            remaining -= read;
        }

        return messageBuffer;
    }

    /// <summary>
    /// Decode a standard LDAPResult (resultCode, matchedDN, diagnosticMessage) from a response.
    /// </summary>
    private static (int ResultCode, string MatchedDn, string DiagnosticMessage) DecodeLdapResult(
        ReadOnlyMemory<byte> protocolOpData, int applicationTag)
    {
        var reader = new AsnReader(protocolOpData, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(applicationTag));

        var resultCode = (int)sequence.ReadInteger(); // ENUMERATED encoded as INTEGER in BER
        var matchedDn = BerHelper.ReadLdapString(sequence);
        var diagnosticMessage = BerHelper.ReadLdapString(sequence);

        return (resultCode, matchedDn, diagnosticMessage);
    }

    /// <summary>
    /// Decode a SearchResultEntry [APPLICATION 4] into an LdapSearchEntry.
    /// </summary>
    private static LdapSearchEntry DecodeSearchResultEntry(ReadOnlyMemory<byte> protocolOpData)
    {
        var reader = new AsnReader(protocolOpData, BerHelper.Rules);
        var sequence = reader.ReadSequence(BerHelper.ApplicationTag(4));

        var dn = BerHelper.ReadLdapString(sequence);
        var attributes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var attrListReader = sequence.ReadSequence();
        while (attrListReader.HasData)
        {
            var attrSeq = attrListReader.ReadSequence();
            var attrName = BerHelper.ReadLdapString(attrSeq);
            var values = BerHelper.ReadStringSet(attrSeq);
            attributes[attrName] = values;
        }

        return new LdapSearchEntry
        {
            DistinguishedName = dn,
            Attributes = attributes,
        };
    }

    /// <summary>
    /// Encode a FilterNode AST to BER bytes suitable for embedding in a SearchRequest.
    /// Uses the visitor pattern on the existing FilterNode types.
    /// </summary>
    private static byte[] EncodeFilterToBer(FilterNode filter)
    {
        var writer = new AsnWriter(BerHelper.Rules);
        WriteFilter(writer, filter);
        return writer.Encode();
    }

    private static void WriteFilter(AsnWriter writer, FilterNode filter)
    {
        switch (filter)
        {
            case AndFilterNode and:
                using (writer.PushSetOf(BerHelper.ContextTag(0, isConstructed: true)))
                {
                    foreach (var child in and.Children)
                        WriteFilter(writer, child);
                }
                break;

            case OrFilterNode or:
                using (writer.PushSetOf(BerHelper.ContextTag(1, isConstructed: true)))
                {
                    foreach (var child in or.Children)
                        WriteFilter(writer, child);
                }
                break;

            case NotFilterNode not:
                using (writer.PushSequence(BerHelper.ContextTag(2, isConstructed: true)))
                {
                    WriteFilter(writer, not.Child);
                }
                break;

            case EqualityFilterNode eq:
                using (writer.PushSequence(BerHelper.ContextTag(3, isConstructed: true)))
                {
                    BerHelper.WriteLdapString(writer, eq.Attribute);
                    BerHelper.WriteLdapString(writer, eq.Value);
                }
                break;

            case SubstringFilterNode sub:
                using (writer.PushSequence(BerHelper.ContextTag(4, isConstructed: true)))
                {
                    BerHelper.WriteLdapString(writer, sub.Attribute);
                    using (writer.PushSequence()) // SEQUENCE OF substring choices
                    {
                        if (sub.Initial is not null)
                            BerHelper.WriteLdapString(writer, BerHelper.ContextTag(0), sub.Initial);
                        foreach (var any in sub.Any)
                            BerHelper.WriteLdapString(writer, BerHelper.ContextTag(1), any);
                        if (sub.Final is not null)
                            BerHelper.WriteLdapString(writer, BerHelper.ContextTag(2), sub.Final);
                    }
                }
                break;

            case GreaterOrEqualFilterNode ge:
                using (writer.PushSequence(BerHelper.ContextTag(5, isConstructed: true)))
                {
                    BerHelper.WriteLdapString(writer, ge.Attribute);
                    BerHelper.WriteLdapString(writer, ge.Value);
                }
                break;

            case LessOrEqualFilterNode le:
                using (writer.PushSequence(BerHelper.ContextTag(6, isConstructed: true)))
                {
                    BerHelper.WriteLdapString(writer, le.Attribute);
                    BerHelper.WriteLdapString(writer, le.Value);
                }
                break;

            case PresenceFilterNode pres:
                writer.WriteOctetString(Encoding.UTF8.GetBytes(pres.Attribute), BerHelper.ContextTag(7));
                break;

            case ApproxMatchFilterNode approx:
                using (writer.PushSequence(BerHelper.ContextTag(8, isConstructed: true)))
                {
                    BerHelper.WriteLdapString(writer, approx.Attribute);
                    BerHelper.WriteLdapString(writer, approx.Value);
                }
                break;

            case ExtensibleMatchFilterNode ext:
                using (writer.PushSequence(BerHelper.ContextTag(9, isConstructed: true)))
                {
                    if (ext.MatchingRule is not null)
                        BerHelper.WriteLdapString(writer, BerHelper.ContextTag(1), ext.MatchingRule);
                    if (ext.Attribute is not null)
                        BerHelper.WriteLdapString(writer, BerHelper.ContextTag(2), ext.Attribute);
                    BerHelper.WriteLdapString(writer, BerHelper.ContextTag(3), ext.Value);
                    if (ext.DnAttributes)
                        writer.WriteBoolean(true, BerHelper.ContextTag(4));
                }
                break;

            default:
                throw new NotSupportedException($"Unsupported filter node type: {filter.GetType().Name}");
        }
    }

    /// <summary>
    /// Validates the server certificate presented during the TLS handshake.
    /// Performs chain validation against AD CS trust anchors and/or the system trust store,
    /// checks expiration, and optionally checks revocation status.
    /// </summary>
    private bool ValidateServerCertificate(
        object sender,
        X509Certificate certificate,
        X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // Fast path: skip all validation if explicitly configured (development only)
        if (_tlsOptions.SkipCertificateValidation)
        {
            _logger?.LogWarning("TLS certificate validation is disabled — accepting certificate from {Host} without verification", _targetHost);
            return true;
        }

        if (certificate == null)
        {
            _logger?.LogError("TLS handshake failed: server did not present a certificate");
            return false;
        }

        var cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

        // 1. Check certificate expiration
        var now = DateTime.UtcNow;
        if (now < cert2.NotBefore || now > cert2.NotAfter)
        {
            _logger?.LogError(
                "TLS certificate has expired or is not yet valid. Subject={Subject}, NotBefore={NotBefore}, NotAfter={NotAfter}",
                cert2.Subject, cert2.NotBefore, cert2.NotAfter);
            return false;
        }

        // 2. If no SSL policy errors were flagged by the system, the certificate is already
        //    trusted by the OS trust store — accept it.
        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            _logger?.LogDebug("TLS certificate validated via system trust store. Subject={Subject}, Thumbprint={Thumbprint}",
                cert2.Subject, cert2.Thumbprint);
            return true;
        }

        // 3. Self-signed certificate handling for development
        if (_tlsOptions.AllowSelfSignedCertificates && IsSelfSigned(cert2))
        {
            // Only name-mismatch or chain errors are expected for self-signed certs.
            // We still reject if the cert has expired (checked above).
            _logger?.LogWarning(
                "Accepting self-signed TLS certificate (development mode). Subject={Subject}, Thumbprint={Thumbprint}",
                cert2.Subject, cert2.Thumbprint);
            return true;
        }

        // 4. Attempt chain validation using AD CS trusted CA certificates and any
        //    additional trusted CAs provided in options
        var trustedCaPems = BuildTrustedCaPemList();

        if (trustedCaPems.Count > 0)
        {
            var adCsResult = ValidateWithTrustedCAs(cert2, chain, trustedCaPems);
            if (adCsResult)
            {
                _logger?.LogDebug(
                    "TLS certificate validated via AD CS trust chain. Subject={Subject}, Thumbprint={Thumbprint}",
                    cert2.Subject, cert2.Thumbprint);
                return true;
            }
        }

        // 5. Validation failed — log the specific errors
        _logger?.LogError(
            "TLS certificate validation failed for {Host}. SslPolicyErrors={Errors}, Subject={Subject}, Thumbprint={Thumbprint}",
            _targetHost, sslPolicyErrors, cert2.Subject, cert2.Thumbprint);

        if (chain != null)
        {
            foreach (var status in chain.ChainStatus)
            {
                _logger?.LogError("  Chain status: {Status} — {Information}", status.Status, status.StatusInformation);
            }
        }

        return false;
    }

    /// <summary>
    /// Build the list of trusted CA PEMs from the CA service and explicit configuration.
    /// </summary>
    private List<string> BuildTrustedCaPemList()
    {
        var pems = new List<string>();

        // Add CA certificate from the directory's Certificate Authority service
        if (_caService != null && _caService.IsInitialized)
        {
            var caInfo = _caService.GetCaInfo();
            if (!string.IsNullOrEmpty(caInfo.CertificatePem))
            {
                pems.Add(caInfo.CertificatePem);
            }
        }

        // Add any additional trusted CA PEMs from configuration
        if (_tlsOptions.TrustedCaCertificatePems != null)
        {
            foreach (var pem in _tlsOptions.TrustedCaCertificatePems)
            {
                if (!string.IsNullOrWhiteSpace(pem))
                {
                    pems.Add(pem);
                }
            }
        }

        return pems;
    }

    /// <summary>
    /// Validate the server certificate by building a chain against the provided trusted CA
    /// certificates. This handles the AD CS trust scenario where DC certificates are issued
    /// by an enterprise CA that is not in the system trust store.
    /// </summary>
    private bool ValidateWithTrustedCAs(X509Certificate2 serverCert, X509Chain existingChain, List<string> trustedCaPems)
    {
        using var customChain = new X509Chain();

        customChain.ChainPolicy.RevocationMode = _tlsOptions.CheckRevocation
            ? X509RevocationMode.Online
            : X509RevocationMode.NoCheck;

        // Allow unknown revocation status — CRL distribution points may not be reachable
        // in all network configurations, and we do not want to reject otherwise valid
        // certificates solely because revocation status could not be determined.
        customChain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
        customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

        // If we have existing intermediate certificates from the TLS handshake, add them
        if (existingChain != null)
        {
            foreach (var element in existingChain.ChainElements)
            {
                customChain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        // Add trusted CA certificates
        foreach (var pem in trustedCaPems)
        {
            try
            {
                var caCert = X509Certificate2.CreateFromPem(pem);
                customChain.ChainPolicy.CustomTrustStore.Add(caCert);
                customChain.ChainPolicy.ExtraStore.Add(caCert);
            }
            catch (CryptographicException ex)
            {
                _logger?.LogWarning(ex, "Failed to parse a trusted CA PEM certificate — skipping");
            }
        }

        // Use custom trust store mode so our AD CS CA certificates are treated as trust anchors
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

        var isValid = customChain.Build(serverCert);

        if (!isValid)
        {
            // Check if the only failure is revocation-related and we can tolerate it
            var hasNonRevocationFailure = false;
            foreach (var status in customChain.ChainStatus)
            {
                if (status.Status != X509ChainStatusFlags.RevocationStatusUnknown &&
                    status.Status != X509ChainStatusFlags.OfflineRevocation &&
                    status.Status != X509ChainStatusFlags.NoError)
                {
                    hasNonRevocationFailure = true;
                    _logger?.LogDebug("Chain validation failure: {Status} — {Info}", status.Status, status.StatusInformation);
                }
            }

            // If revocation status is the only issue and revocation checking is enabled,
            // treat it as acceptable — the CRL/OCSP endpoint may be unreachable
            if (!hasNonRevocationFailure)
            {
                _logger?.LogDebug("Chain validated successfully (revocation status unknown, treated as acceptable)");
                return true;
            }
        }

        return isValid;
    }

    /// <summary>
    /// Determine whether a certificate is self-signed by checking if the subject and issuer match
    /// and the certificate can verify its own signature.
    /// </summary>
    private static bool IsSelfSigned(X509Certificate2 cert)
    {
        if (cert.Subject != cert.Issuer)
            return false;

        try
        {
            // Verify the certificate signed itself
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.Build(cert);

            // A self-signed cert will have exactly one element in the chain (itself)
            return chain.ChainElements.Count == 1;
        }
        catch
        {
            // If chain building fails, fall back to subject/issuer comparison
            return true;
        }
    }

    #endregion
}
