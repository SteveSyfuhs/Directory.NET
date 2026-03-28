using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Fido2;

/// <summary>
/// Implements FIDO2/WebAuthn registration and authentication flows.
/// Stores credential data as a custom attribute (msDS-Fido2Registration) on user DirectoryObjects.
/// Implements WebAuthn verification from scratch: CBOR parsing, client data verification,
/// authenticator data parsing, and assertion signature verification (ES256/RS256).
/// </summary>
public class Fido2Service
{
    private const string Fido2AttributeName = "msDS-Fido2Registration";
    private const int ChallengeLength = 32;
    private const int ChallengeExpiryMinutes = 5;

    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<Fido2Service> _logger;
    private readonly string _rpId;
    private readonly string _rpName;
    private readonly string _origin;

    public Fido2Service(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<Fido2Service> logger,
        string rpId = "localhost",
        string rpName = "Directory.NET",
        string origin = "https://localhost")
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
        _rpId = rpId;
        _rpName = rpName;
        _origin = origin;
    }

    /// <summary>
    /// Begin FIDO2 registration — generate a challenge and PublicKeyCredentialCreationOptions
    /// for the browser to pass to navigator.credentials.create().
    /// </summary>
    public async Task<PublicKeyCredentialCreationOptions> BeginRegistration(string userDn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);
        var challenge = GenerateChallenge();

        // Store pending challenge
        data.PendingChallenges[challenge] = new Fido2Challenge
        {
            Challenge = challenge,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = "registration",
        };

        // Prune expired challenges
        PruneExpiredChallenges(data);
        await SaveRegistrationDataAsync(obj, data, ct);

        var userId = Base64UrlEncode(Encoding.UTF8.GetBytes(obj.DistinguishedName));
        var displayName = obj.DisplayName ?? obj.Cn ?? obj.DistinguishedName;
        var accountName = obj.UserPrincipalName ?? obj.SAMAccountName ?? obj.Cn ?? userDn;

        var options = new PublicKeyCredentialCreationOptions
        {
            Rp = new RpEntity { Id = _rpId, Name = _rpName },
            User = new UserEntity { Id = userId, Name = accountName, DisplayName = displayName },
            Challenge = challenge,
            PubKeyCredParams = new List<PubKeyCredParam>
            {
                new() { Type = "public-key", Alg = -7 },   // ES256
                new() { Type = "public-key", Alg = -257 },  // RS256
            },
            Timeout = 60000,
            Attestation = "none",
            ExcludeCredentials = data.Credentials
                .Where(c => c.IsEnabled)
                .Select(c => new PublicKeyCredentialDescriptor
                {
                    Type = "public-key",
                    Id = c.CredentialId,
                    Transports = c.Transports.Count > 0 ? c.Transports : null,
                })
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                AuthenticatorAttachment = "cross-platform",
                RequireResidentKey = false,
                ResidentKey = "discouraged",
                UserVerification = "preferred",
            },
        };

        _logger.LogInformation("FIDO2 registration started for {DN}", obj.DistinguishedName);
        return options;
    }

    /// <summary>
    /// Complete FIDO2 registration — verify the attestation response from the browser
    /// and store the new credential.
    /// </summary>
    public async Task<Fido2RegistrationResult> CompleteRegistration(
        string userDn, AttestationResponse attestation, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);

        try
        {
            // 1. Decode and verify clientDataJSON
            var clientDataBytes = Base64UrlDecode(attestation.Response.ClientDataJSON);
            var clientData = JsonSerializer.Deserialize<JsonElement>(clientDataBytes);

            var type = clientData.GetProperty("type").GetString();
            if (type != "webauthn.create")
                return new Fido2RegistrationResult { Success = false, Error = "Invalid client data type" };

            var challenge = clientData.GetProperty("challenge").GetString();
            if (string.IsNullOrEmpty(challenge) || !data.PendingChallenges.ContainsKey(challenge))
                return new Fido2RegistrationResult { Success = false, Error = "Invalid or expired challenge" };

            var pendingChallenge = data.PendingChallenges[challenge];
            if (pendingChallenge.Type != "registration" ||
                DateTimeOffset.UtcNow - pendingChallenge.CreatedAt > TimeSpan.FromMinutes(ChallengeExpiryMinutes))
            {
                data.PendingChallenges.Remove(challenge);
                await SaveRegistrationDataAsync(obj, data, ct);
                return new Fido2RegistrationResult { Success = false, Error = "Challenge expired" };
            }

            var origin = clientData.GetProperty("origin").GetString();
            if (origin != _origin)
                return new Fido2RegistrationResult { Success = false, Error = $"Origin mismatch: expected {_origin}, got {origin}" };

            // 2. Decode attestation object (CBOR)
            var attestationObjectBytes = Base64UrlDecode(attestation.Response.AttestationObject);
            var cborReader = new CborReader(attestationObjectBytes);
            var attestationMap = cborReader.ReadMap();

            // 3. Parse authenticator data
            var authDataKey = attestationMap.Keys.FirstOrDefault(k => k.ToString() == "authData");
            if (authDataKey is null || attestationMap[authDataKey] is not byte[] authData)
                return new Fido2RegistrationResult { Success = false, Error = "Missing authenticator data" };

            // Verify RP ID hash (first 32 bytes)
            var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(_rpId));
            if (!authData[..32].SequenceEqual(rpIdHash))
                return new Fido2RegistrationResult { Success = false, Error = "RP ID hash mismatch" };

            // Parse flags
            var flags = authData[32];
            var userPresent = (flags & 0x01) != 0;
            var attestedCredDataPresent = (flags & 0x40) != 0;

            if (!userPresent)
                return new Fido2RegistrationResult { Success = false, Error = "User not present" };

            if (!attestedCredDataPresent)
                return new Fido2RegistrationResult { Success = false, Error = "No attested credential data" };

            // Sign count (4 bytes big-endian at offset 33)
            var signCount = (long)(
                (authData[33] << 24) | (authData[34] << 16) |
                (authData[35] << 8) | authData[36]);

            // 4. Parse attested credential data (starts at offset 37)
            // AAGUID (16 bytes)
            var offset = 37;
            // var aaguid = authData[offset..(offset + 16)];  // informational only
            offset += 16;

            // Credential ID length (2 bytes big-endian)
            var credIdLen = (authData[offset] << 8) | authData[offset + 1];
            offset += 2;

            // Credential ID
            var credentialIdBytes = authData[offset..(offset + credIdLen)];
            var credentialId = Base64UrlEncode(credentialIdBytes);
            offset += credIdLen;

            // Public key (COSE_Key encoded in CBOR, remainder of authData)
            var publicKeyBytes = authData[offset..];

            // 5. Determine attestation type
            var fmtKey = attestationMap.Keys.FirstOrDefault(k => k.ToString() == "fmt");
            var attestationType = fmtKey is not null ? attestationMap[fmtKey]?.ToString() ?? "none" : "none";

            // 6. Store credential
            data.PendingChallenges.Remove(challenge);

            var credential = new Fido2Credential
            {
                Id = Guid.NewGuid().ToString(),
                UserDn = obj.DistinguishedName,
                CredentialId = credentialId,
                PublicKey = publicKeyBytes,
                SignCount = signCount,
                DeviceName = attestation.DeviceName ?? $"Security Key {data.Credentials.Count + 1}",
                AttestationType = attestationType,
                Transports = new List<string>(),
                RegisteredAt = DateTimeOffset.UtcNow,
                IsEnabled = true,
            };

            data.Credentials.Add(credential);
            await SaveRegistrationDataAsync(obj, data, ct);

            _logger.LogInformation("FIDO2 credential registered for {DN}, credentialId={CredId}",
                obj.DistinguishedName, credentialId);

            return new Fido2RegistrationResult { Success = true, CredentialId = credentialId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIDO2 registration failed for {DN}", userDn);
            return new Fido2RegistrationResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Begin FIDO2 authentication — generate a challenge and PublicKeyCredentialRequestOptions
    /// for the browser to pass to navigator.credentials.get().
    /// </summary>
    public async Task<PublicKeyCredentialRequestOptions> BeginAuthentication(string userDn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);
        var enabledCreds = data.Credentials.Where(c => c.IsEnabled).ToList();
        if (enabledCreds.Count == 0)
            throw new InvalidOperationException("No FIDO2 credentials registered for this user.");

        var challenge = GenerateChallenge();

        data.PendingChallenges[challenge] = new Fido2Challenge
        {
            Challenge = challenge,
            CreatedAt = DateTimeOffset.UtcNow,
            Type = "authentication",
        };

        PruneExpiredChallenges(data);
        await SaveRegistrationDataAsync(obj, data, ct);

        var options = new PublicKeyCredentialRequestOptions
        {
            Challenge = challenge,
            Timeout = 60000,
            RpId = _rpId,
            AllowCredentials = enabledCreds.Select(c => new PublicKeyCredentialDescriptor
            {
                Type = "public-key",
                Id = c.CredentialId,
                Transports = c.Transports.Count > 0 ? c.Transports : null,
            }).ToList(),
            UserVerification = "preferred",
        };

        _logger.LogInformation("FIDO2 authentication started for {DN}", obj.DistinguishedName);
        return options;
    }

    /// <summary>
    /// Complete FIDO2 authentication — verify the assertion response from the browser,
    /// check the signature using the stored public key, and update the sign count.
    /// </summary>
    public async Task<Fido2AuthenticationResult> CompleteAuthentication(
        string userDn, AssertionResponse assertion, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);

        try
        {
            // 1. Decode and verify clientDataJSON
            var clientDataBytes = Base64UrlDecode(assertion.Response.ClientDataJSON);
            var clientData = JsonSerializer.Deserialize<JsonElement>(clientDataBytes);

            var type = clientData.GetProperty("type").GetString();
            if (type != "webauthn.get")
                return new Fido2AuthenticationResult { Success = false, Error = "Invalid client data type" };

            var challenge = clientData.GetProperty("challenge").GetString();
            if (string.IsNullOrEmpty(challenge) || !data.PendingChallenges.ContainsKey(challenge))
                return new Fido2AuthenticationResult { Success = false, Error = "Invalid or expired challenge" };

            var pendingChallenge = data.PendingChallenges[challenge];
            if (pendingChallenge.Type != "authentication" ||
                DateTimeOffset.UtcNow - pendingChallenge.CreatedAt > TimeSpan.FromMinutes(ChallengeExpiryMinutes))
            {
                data.PendingChallenges.Remove(challenge);
                await SaveRegistrationDataAsync(obj, data, ct);
                return new Fido2AuthenticationResult { Success = false, Error = "Challenge expired" };
            }

            var origin = clientData.GetProperty("origin").GetString();
            if (origin != _origin)
                return new Fido2AuthenticationResult { Success = false, Error = $"Origin mismatch" };

            // 2. Find the credential
            var credentialId = assertion.Id;
            var credential = data.Credentials.FirstOrDefault(c => c.CredentialId == credentialId && c.IsEnabled);
            if (credential is null)
                return new Fido2AuthenticationResult { Success = false, Error = "Credential not found" };

            // 3. Parse authenticator data
            var authDataBytes = Base64UrlDecode(assertion.Response.AuthenticatorData);

            // Verify RP ID hash
            var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(_rpId));
            if (!authDataBytes[..32].SequenceEqual(rpIdHash))
                return new Fido2AuthenticationResult { Success = false, Error = "RP ID hash mismatch" };

            // Check user present flag
            var flags = authDataBytes[32];
            if ((flags & 0x01) == 0)
                return new Fido2AuthenticationResult { Success = false, Error = "User not present" };

            // Parse sign count
            var newSignCount = (long)(
                (authDataBytes[33] << 24) | (authDataBytes[34] << 16) |
                (authDataBytes[35] << 8) | authDataBytes[36]);

            // 4. Verify signature
            var signatureBytes = Base64UrlDecode(assertion.Response.Signature);
            var clientDataHash = SHA256.HashData(clientDataBytes);

            // Signature is over authenticatorData || hash(clientDataJSON)
            var signedData = new byte[authDataBytes.Length + clientDataHash.Length];
            Buffer.BlockCopy(authDataBytes, 0, signedData, 0, authDataBytes.Length);
            Buffer.BlockCopy(clientDataHash, 0, signedData, authDataBytes.Length, clientDataHash.Length);

            if (!VerifySignature(credential.PublicKey, signedData, signatureBytes))
                return new Fido2AuthenticationResult { Success = false, Error = "Signature verification failed" };

            // 5. Verify sign count (protection against cloned authenticators)
            if (newSignCount > 0 && credential.SignCount > 0 && newSignCount <= credential.SignCount)
            {
                _logger.LogWarning("FIDO2 sign count regression for {DN}, credential {CredId}. " +
                    "Stored={Stored}, Received={Received}. Possible cloned authenticator.",
                    userDn, credentialId, credential.SignCount, newSignCount);
                // We log the warning but still allow authentication
            }

            // 6. Update credential state
            credential.SignCount = newSignCount;
            credential.LastUsedAt = DateTimeOffset.UtcNow;
            data.PendingChallenges.Remove(challenge);
            await SaveRegistrationDataAsync(obj, data, ct);

            _logger.LogInformation("FIDO2 authentication succeeded for {DN}", obj.DistinguishedName);
            return new Fido2AuthenticationResult { Success = true, UserDn = obj.DistinguishedName };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIDO2 authentication failed for {DN}", userDn);
            return new Fido2AuthenticationResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// List all FIDO2 credentials registered for a user.
    /// </summary>
    public async Task<List<Fido2CredentialSummary>> ListCredentials(string userDn, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);
        return data.Credentials.Select(c => new Fido2CredentialSummary
        {
            Id = c.Id,
            CredentialId = c.CredentialId,
            DeviceName = c.DeviceName,
            AttestationType = c.AttestationType,
            Transports = c.Transports,
            RegisteredAt = c.RegisteredAt,
            LastUsedAt = c.LastUsedAt,
            SignCount = c.SignCount,
            IsEnabled = c.IsEnabled,
        }).ToList();
    }

    /// <summary>
    /// Delete a FIDO2 credential by its internal ID.
    /// </summary>
    public async Task DeleteCredential(string userDn, string credentialId, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);
        var removed = data.Credentials.RemoveAll(c => c.Id == credentialId);
        if (removed == 0)
            throw new InvalidOperationException($"Credential not found: {credentialId}");

        await SaveRegistrationDataAsync(obj, data, ct);
        _logger.LogInformation("FIDO2 credential {CredId} deleted for {DN}", credentialId, userDn);
    }

    /// <summary>
    /// Rename a FIDO2 credential.
    /// </summary>
    public async Task RenameCredential(string userDn, string credentialId, string name, CancellationToken ct = default)
    {
        var obj = await ResolveUserAsync(userDn, ct)
            ?? throw new InvalidOperationException($"User not found: {userDn}");

        var data = GetRegistrationData(obj);
        var credential = data.Credentials.FirstOrDefault(c => c.Id == credentialId)
            ?? throw new InvalidOperationException($"Credential not found: {credentialId}");

        credential.DeviceName = name;
        await SaveRegistrationDataAsync(obj, data, ct);
        _logger.LogInformation("FIDO2 credential {CredId} renamed to '{Name}' for {DN}", credentialId, name, userDn);
    }

    // --- Signature verification ---

    /// <summary>
    /// Verifies an assertion signature using the stored COSE public key.
    /// Supports ES256 (ECDSA P-256 with SHA-256) and RS256 (RSASSA-PKCS1-v1_5 with SHA-256).
    /// </summary>
    private static bool VerifySignature(byte[] cosePublicKey, byte[] signedData, byte[] signature)
    {
        try
        {
            var cbor = new CborReader(cosePublicKey);
            var keyMap = cbor.ReadMap();

            // COSE key type (kty): 1=OKP, 2=EC2, 3=RSA
            var ktyKey = keyMap.Keys.FirstOrDefault(k => IsIntKey(k, 1));
            var kty = ktyKey is not null ? Convert.ToInt64(keyMap[ktyKey]) : 0;

            // Algorithm (alg): -7=ES256, -257=RS256
            var algKey = keyMap.Keys.FirstOrDefault(k => IsIntKey(k, 3));
            var alg = algKey is not null ? Convert.ToInt64(keyMap[algKey]) : 0;

            if (kty == 2 && alg == -7) // EC2 + ES256
                return VerifyES256(keyMap, signedData, signature);

            if (kty == 3 && alg == -257) // RSA + RS256
                return VerifyRS256(keyMap, signedData, signature);

            // Fall back: try ES256 if kty/alg not explicitly set
            if (kty == 2)
                return VerifyES256(keyMap, signedData, signature);

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyES256(Dictionary<object, object> keyMap, byte[] data, byte[] signature)
    {
        // EC2 key: -1 (crv)=1 (P-256), -2 (x), -3 (y)
        var xKey = keyMap.Keys.FirstOrDefault(k => IsIntKey(k, -2));
        var yKey = keyMap.Keys.FirstOrDefault(k => IsIntKey(k, -3));

        if (xKey is null || yKey is null) return false;

        var x = keyMap[xKey] as byte[];
        var y = keyMap[yKey] as byte[];
        if (x is null || y is null) return false;

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y },
        });

        // WebAuthn uses ASN.1 DER-encoded signatures; try DER first, then raw IEEE P1363
        if (ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            return true;

        // Try IEEE P1363 format (r || s, each 32 bytes)
        if (signature.Length == 64)
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return false;
    }

    private static bool VerifyRS256(Dictionary<object, object> keyMap, byte[] data, byte[] signature)
    {
        // RSA key: -1 (n), -2 (e)
        var nKey = keyMap.Keys.FirstOrDefault(k => IsIntKey(k, -1));
        var eKey = keyMap.Keys.FirstOrDefault(k => IsIntKey(k, -2));

        if (nKey is null || eKey is null) return false;

        var n = keyMap[nKey] as byte[];
        var e = keyMap[eKey] as byte[];
        if (n is null || e is null) return false;

        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = n,
            Exponent = e,
        });

        return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static bool IsIntKey(object key, long expected)
    {
        if (key is long l) return l == expected;
        if (key is int i) return i == expected;
        return false;
    }

    // --- Helper methods ---

    private static string GenerateChallenge()
    {
        var bytes = RandomNumberGenerator.GetBytes(ChallengeLength);
        return Base64UrlEncode(bytes);
    }

    private static void PruneExpiredChallenges(Fido2RegistrationData data)
    {
        var expired = data.PendingChallenges
            .Where(kv => DateTimeOffset.UtcNow - kv.Value.CreatedAt > TimeSpan.FromMinutes(ChallengeExpiryMinutes * 2))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in expired)
            data.PendingChallenges.Remove(key);
    }

    internal static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    internal static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private async Task<DirectoryObject> ResolveUserAsync(string dn, CancellationToken ct)
    {
        var obj = await _store.GetByDnAsync("default", dn, ct);
        if (obj is null)
            obj = await _store.GetByUpnAsync("default", dn, ct);
        if (obj is null)
        {
            var domainDn = _ncService.GetDomainNc().Dn;
            obj = await _store.GetBySamAccountNameAsync("default", domainDn, dn, ct);
        }
        return obj;
    }

    private static Fido2RegistrationData GetRegistrationData(DirectoryObject obj)
    {
        if (!obj.Attributes.TryGetValue(Fido2AttributeName, out var attr))
            return new Fido2RegistrationData();

        var json = attr.Values.FirstOrDefault()?.ToString();
        if (string.IsNullOrEmpty(json))
            return new Fido2RegistrationData();

        try
        {
            return JsonSerializer.Deserialize<Fido2RegistrationData>(json) ?? new Fido2RegistrationData();
        }
        catch
        {
            return new Fido2RegistrationData();
        }
    }

    private async Task SaveRegistrationDataAsync(DirectoryObject obj, Fido2RegistrationData data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        obj.Attributes[Fido2AttributeName] = new DirectoryAttribute
        {
            Name = Fido2AttributeName,
            Values = [json],
        };
        obj.WhenChanged = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(obj, ct);
    }
}
