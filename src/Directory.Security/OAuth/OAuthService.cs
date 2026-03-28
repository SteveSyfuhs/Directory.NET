using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.OAuth;

// ────────────────────────────────────────────────────────────
//  Models
// ────────────────────────────────────────────────────────────

public class OAuthClient
{
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
    public string ClientName { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty; // Hashed (SHA-256 hex)
    public List<string> RedirectUris { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new() { "openid", "profile", "email" };
    public List<string> AllowedGrantTypes { get; set; } = new() { "authorization_code", "client_credentials", "refresh_token" };
    public string LogoUri { get; set; }
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
    public bool RequirePkce { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class OAuthAuthorizationCode
{
    public string Code { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string SubjectDn { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; }
    public string CodeChallengeMethod { get; set; }
    public string Nonce { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class RefreshTokenEntry
{
    public string Token { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string SubjectDn { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public DateTimeOffset ExpiresAt { get; set; }
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; }
    public string IdToken { get; set; }
    public string Scope { get; set; }
}

public class UserInfoResponse
{
    public string Sub { get; set; } = string.Empty;
    public string Name { get; set; }
    public string Email { get; set; }
    public List<string> Groups { get; set; }
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string PreferredUsername { get; set; }
}

// ────────────────────────────────────────────────────────────
//  Service
// ────────────────────────────────────────────────────────────

public class OAuthService
{
    private readonly ConcurrentDictionary<string, OAuthClient> _clients = new();
    private readonly ConcurrentDictionary<string, OAuthAuthorizationCode> _authCodes = new();
    private readonly ConcurrentDictionary<string, RefreshTokenEntry> _refreshTokens = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    private RSA _signingKey;
    private string _keyId;
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _namingContextService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILogger<OAuthService> _logger;

    private const string OAuthConfigAttribute = "oauthSigningKey";
    private const string OAuthKeyIdAttribute = "oauthSigningKeyId";

    public OAuthService(IDirectoryStore store, INamingContextService namingContextService, IPasswordPolicy passwordPolicy, ILogger<OAuthService> logger)
    {
        _store = store;
        _namingContextService = namingContextService;
        _passwordPolicy = passwordPolicy;
        _logger = logger;

        // Generate ephemeral key initially; LoadOrCreateSigningKeyAsync replaces it with persisted key
        _signingKey = RSA.Create(2048);
        _keyId = Guid.NewGuid().ToString("N")[..16];
    }

    // ── Key persistence ───────────────────────────────────────

    /// <summary>
    /// Loads the RSA signing key from the directory store, or generates and persists a new one.
    /// Call this during hosted service initialization after the directory store is available.
    /// </summary>
    public async Task LoadOrCreateSigningKeyAsync(CancellationToken ct = default)
    {
        try
        {
            var domainDn = _namingContextService.GetDomainNc().Dn;
            var configDn = $"CN=OAuthConfig,CN=System,{domainDn}";

            var configObj = await _store.GetByDnAsync("default", configDn, ct);
            if (configObj is not null)
            {
                var pemAttr = configObj.GetAttribute(OAuthConfigAttribute)?.GetFirstString();
                var kidAttr = configObj.GetAttribute(OAuthKeyIdAttribute)?.GetFirstString();

                if (!string.IsNullOrEmpty(pemAttr) && !string.IsNullOrEmpty(kidAttr))
                {
                    var rsa = ImportRsaFromPem(pemAttr);
                    if (rsa is not null)
                    {
                        _signingKey.Dispose();
                        _signingKey = rsa;
                        _keyId = kidAttr;
                        _logger.LogInformation("OAuth signing key loaded from directory store (kid={KeyId})", _keyId);
                        return;
                    }
                }
            }

            // Key not found or invalid — generate a new one and persist it
            var newKey = RSA.Create(2048);
            var newKid = Guid.NewGuid().ToString("N")[..16];
            var pem = ExportRsaToPem(newKey);

            await PersistSigningKeyAsync(configDn, domainDn, pem, newKid, configObj, ct);

            _signingKey.Dispose();
            _signingKey = newKey;
            _keyId = newKid;
            _logger.LogInformation("OAuth signing key generated and persisted to directory store (kid={KeyId})", _keyId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load/persist OAuth signing key from directory store — using ephemeral key");
        }
    }

    private async Task PersistSigningKeyAsync(string configDn, string domainDn, string pem, string kid,
        DirectoryObject existing, CancellationToken ct)
    {
        if (existing is not null)
        {
            existing.SetAttribute(OAuthConfigAttribute, new Core.Models.DirectoryAttribute(OAuthConfigAttribute, pem));
            existing.SetAttribute(OAuthKeyIdAttribute, new Core.Models.DirectoryAttribute(OAuthKeyIdAttribute, kid));
            existing.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existing, ct);
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            var obj = new DirectoryObject
            {
                Id = configDn.ToLowerInvariant(),
                TenantId = "default",
                DomainDn = domainDn,
                DistinguishedName = configDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectClass = ["top", "container"],
                ObjectCategory = "container",
                Cn = "OAuthConfig",
                ParentDn = $"CN=System,{domainDn}",
                WhenCreated = now,
                WhenChanged = now,
            };
            obj.SetAttribute(OAuthConfigAttribute, new Core.Models.DirectoryAttribute(OAuthConfigAttribute, pem));
            obj.SetAttribute(OAuthKeyIdAttribute, new Core.Models.DirectoryAttribute(OAuthKeyIdAttribute, kid));
            await _store.CreateAsync(obj, ct);
        }
    }

    private static string ExportRsaToPem(RSA rsa)
    {
        var privateKeyBytes = rsa.ExportPkcs8PrivateKey();
        return "-----BEGIN PRIVATE KEY-----\n"
            + Convert.ToBase64String(privateKeyBytes, Base64FormattingOptions.InsertLineBreaks)
            + "\n-----END PRIVATE KEY-----";
    }

    private static RSA ImportRsaFromPem(string pem)
    {
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem.AsSpan());
            return rsa;
        }
        catch
        {
            return null;
        }
    }

    // ── Key material ─────────────────────────────────────────

    public RSA SigningKey => _signingKey;
    public string KeyId => _keyId;

    public object GetJwks()
    {
        var parameters = _signingKey.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = _keyId,
                    alg = "RS256",
                    n = Base64UrlEncode(parameters.Modulus),
                    e = Base64UrlEncode(parameters.Exponent),
                }
            }
        };
    }

    // ── Client management ────────────────────────────────────

    public IReadOnlyList<OAuthClient> GetAllClients()
        => _clients.Values.OrderBy(c => c.ClientName).ToList();

    public OAuthClient GetClient(string clientId)
        => _clients.TryGetValue(clientId, out var c) ? c : null;

    public (OAuthClient client, string plainSecret) CreateClient(OAuthClient template)
    {
        var client = new OAuthClient
        {
            ClientId = Guid.NewGuid().ToString("N"),
            ClientName = template.ClientName,
            RedirectUris = template.RedirectUris ?? new(),
            AllowedScopes = template.AllowedScopes ?? new() { "openid", "profile", "email" },
            AllowedGrantTypes = template.AllowedGrantTypes ?? new() { "authorization_code", "client_credentials", "refresh_token" },
            LogoUri = template.LogoUri,
            AccessTokenLifetimeMinutes = template.AccessTokenLifetimeMinutes > 0 ? template.AccessTokenLifetimeMinutes : 60,
            RefreshTokenLifetimeDays = template.RefreshTokenLifetimeDays > 0 ? template.RefreshTokenLifetimeDays : 30,
            RequirePkce = template.RequirePkce,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var plainSecret = GenerateRandomString(48);
        client.ClientSecret = HashSecret(plainSecret);

        _clients[client.ClientId] = client;
        _logger.LogInformation("OAuth client created: {ClientId} ({ClientName})", client.ClientId, client.ClientName);
        return (client, plainSecret);
    }

    public OAuthClient UpdateClient(string clientId, OAuthClient updates)
    {
        if (!_clients.TryGetValue(clientId, out var existing))
            return null;

        existing.ClientName = updates.ClientName ?? existing.ClientName;
        existing.RedirectUris = updates.RedirectUris ?? existing.RedirectUris;
        existing.AllowedScopes = updates.AllowedScopes ?? existing.AllowedScopes;
        existing.AllowedGrantTypes = updates.AllowedGrantTypes ?? existing.AllowedGrantTypes;
        existing.LogoUri = updates.LogoUri;
        existing.AccessTokenLifetimeMinutes = updates.AccessTokenLifetimeMinutes > 0 ? updates.AccessTokenLifetimeMinutes : existing.AccessTokenLifetimeMinutes;
        existing.RefreshTokenLifetimeDays = updates.RefreshTokenLifetimeDays > 0 ? updates.RefreshTokenLifetimeDays : existing.RefreshTokenLifetimeDays;
        existing.RequirePkce = updates.RequirePkce;
        existing.IsEnabled = updates.IsEnabled;

        return existing;
    }

    public bool DeleteClient(string clientId)
        => _clients.TryRemove(clientId, out _);

    public string RegenerateClientSecret(string clientId)
    {
        if (!_clients.TryGetValue(clientId, out var client))
            throw new InvalidOperationException("Client not found");

        var plainSecret = GenerateRandomString(48);
        client.ClientSecret = HashSecret(plainSecret);
        return plainSecret;
    }

    // ── Authorization Code flow ──────────────────────────────

    public string CreateAuthorizationCode(string clientId, string subjectDn, string redirectUri,
        List<string> scopes, string codeChallenge, string codeChallengeMethod, string nonce)
    {
        var code = GenerateRandomString(64);
        _authCodes[code] = new OAuthAuthorizationCode
        {
            Code = code,
            ClientId = clientId,
            SubjectDn = subjectDn,
            RedirectUri = redirectUri,
            Scopes = scopes,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            Nonce = nonce,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        return code;
    }

    public OAuthAuthorizationCode ConsumeAuthorizationCode(string code)
    {
        if (!_authCodes.TryRemove(code, out var authCode))
            return null;

        if (authCode.ExpiresAt < DateTimeOffset.UtcNow)
            return null;

        return authCode;
    }

    // ── Token generation ─────────────────────────────────────

    public async Task<TokenResponse> ExchangeCodeForTokens(string code, string clientId,
        string redirectUri, string codeVerifier, string issuer)
    {
        var authCode = ConsumeAuthorizationCode(code);
        if (authCode is null || authCode.ClientId != clientId || authCode.RedirectUri != redirectUri)
            return null;

        // Verify PKCE
        if (!string.IsNullOrEmpty(authCode.CodeChallenge))
        {
            if (string.IsNullOrEmpty(codeVerifier))
                return null;

            var expectedChallenge = authCode.CodeChallengeMethod == "S256"
                ? Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
                : codeVerifier;

            if (expectedChallenge != authCode.CodeChallenge)
                return null;
        }

        var client = GetClient(clientId);
        if (client is null || !client.IsEnabled)
            return null;

        var user = await _store.GetByDnAsync("default", authCode.SubjectDn);
        if (user is null)
            return null;

        var accessToken = GenerateAccessToken(user, client, authCode.Scopes, issuer);
        var idToken = authCode.Scopes.Contains("openid")
            ? GenerateIdToken(user, client, authCode.Nonce, issuer)
            : null;

        string refreshToken = null;
        if (authCode.Scopes.Contains("offline_access"))
        {
            refreshToken = GenerateRandomString(64);
            _refreshTokens[refreshToken] = new RefreshTokenEntry
            {
                Token = refreshToken,
                ClientId = clientId,
                SubjectDn = authCode.SubjectDn,
                Scopes = authCode.Scopes,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(client.RefreshTokenLifetimeDays),
            };
        }

        return new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = client.AccessTokenLifetimeMinutes * 60,
            RefreshToken = refreshToken,
            IdToken = idToken,
            Scope = string.Join(" ", authCode.Scopes),
        };
    }

    public async Task<TokenResponse> HandleClientCredentials(string clientId, string clientSecret,
        string scope, string issuer)
    {
        var client = GetClient(clientId);
        if (client is null || !client.IsEnabled)
            return null;

        if (!client.AllowedGrantTypes.Contains("client_credentials"))
            return null;

        if (HashSecret(clientSecret) != client.ClientSecret)
            return null;

        var scopes = (scope?.Split(' ') ?? Array.Empty<string>())
            .Where(s => client.AllowedScopes.Contains(s))
            .ToList();

        var accessToken = GenerateClientCredentialsToken(client, scopes, issuer);

        return new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = client.AccessTokenLifetimeMinutes * 60,
            Scope = string.Join(" ", scopes),
        };
    }

    public async Task<TokenResponse> HandleRefreshToken(string refreshToken, string clientId, string issuer)
    {
        if (!_refreshTokens.TryGetValue(refreshToken, out var entry))
            return null;

        if (entry.ClientId != clientId || entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _refreshTokens.TryRemove(refreshToken, out _);
            return null;
        }

        var client = GetClient(clientId);
        if (client is null || !client.IsEnabled)
            return null;

        var user = await _store.GetByDnAsync("default", entry.SubjectDn);
        if (user is null)
            return null;

        // Rotate refresh token
        _refreshTokens.TryRemove(refreshToken, out _);
        var newRefreshToken = GenerateRandomString(64);
        _refreshTokens[newRefreshToken] = new RefreshTokenEntry
        {
            Token = newRefreshToken,
            ClientId = clientId,
            SubjectDn = entry.SubjectDn,
            Scopes = entry.Scopes,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(client.RefreshTokenLifetimeDays),
        };

        var accessToken = GenerateAccessToken(user, client, entry.Scopes, issuer);
        var idToken = entry.Scopes.Contains("openid")
            ? GenerateIdToken(user, client, null, issuer)
            : null;

        return new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = client.AccessTokenLifetimeMinutes * 60,
            RefreshToken = newRefreshToken,
            IdToken = idToken,
            Scope = string.Join(" ", entry.Scopes),
        };
    }

    public bool RevokeToken(string token)
    {
        // Try removing as refresh token
        if (_refreshTokens.TryRemove(token, out _))
            return true;

        // Mark as revoked (for access tokens – stored until expiry window passes)
        _revokedTokens[token] = DateTimeOffset.UtcNow.AddHours(2);
        return true;
    }

    // ── UserInfo ─────────────────────────────────────────────

    public async Task<UserInfoResponse> GetUserInfo(string accessToken)
    {
        var claims = ValidateAccessToken(accessToken);
        if (claims is null)
            return null;

        var sub = claims.GetValueOrDefault("sub");
        if (sub is null)
            return null;

        var user = await _store.GetByGuidAsync("default", sub);
        if (user is null)
            return null;

        return new UserInfoResponse
        {
            Sub = user.ObjectGuid,
            Name = user.DisplayName ?? user.Cn,
            Email = user.UserPrincipalName ?? user.Mail,
            GivenName = user.GivenName,
            FamilyName = user.Sn,
            PreferredUsername = user.UserPrincipalName ?? user.SAMAccountName,
            Groups = user.MemberOf,
        };
    }

    // ── JWT generation (manual, no external NuGet) ───────────

    private string GenerateAccessToken(Core.Models.DirectoryObject user, OAuthClient client,
        List<string> scopes, string issuer)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["sub"] = user.ObjectGuid,
            ["aud"] = client.ClientId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(client.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["scope"] = string.Join(" ", scopes),
        };

        if (scopes.Contains("profile"))
        {
            if (user.DisplayName is not null) payload["name"] = user.DisplayName;
            if (user.GivenName is not null) payload["given_name"] = user.GivenName;
            if (user.Sn is not null) payload["family_name"] = user.Sn;
            if (user.UserPrincipalName is not null) payload["preferred_username"] = user.UserPrincipalName;
        }

        if (scopes.Contains("email"))
        {
            var email = user.UserPrincipalName ?? user.Mail;
            if (email is not null) payload["email"] = email;
        }

        if (scopes.Contains("groups") && user.MemberOf.Count > 0)
        {
            payload["groups"] = user.MemberOf;
        }

        return SignJwt(payload);
    }

    private string GenerateIdToken(Core.Models.DirectoryObject user, OAuthClient client,
        string nonce, string issuer)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["sub"] = user.ObjectGuid,
            ["aud"] = client.ClientId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(client.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
            ["auth_time"] = now.ToUnixTimeSeconds(),
        };

        if (nonce is not null) payload["nonce"] = nonce;
        if (user.DisplayName is not null) payload["name"] = user.DisplayName;
        if (user.UserPrincipalName is not null) payload["email"] = user.UserPrincipalName;
        if (user.UserPrincipalName is not null) payload["preferred_username"] = user.UserPrincipalName;

        return SignJwt(payload);
    }

    private string GenerateClientCredentialsToken(OAuthClient client, List<string> scopes, string issuer)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["sub"] = client.ClientId,
            ["aud"] = client.ClientId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(client.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["scope"] = string.Join(" ", scopes),
            ["client_id"] = client.ClientId,
        };

        return SignJwt(payload);
    }

    private string SignJwt(Dictionary<string, object> payload)
    {
        var header = new Dictionary<string, string>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = _keyId,
        };

        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var signingInput = $"{headerB64}.{payloadB64}";
        var signatureBytes = _signingKey.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signatureBytes)}";
    }

    public Dictionary<string, string> ValidateAccessToken(string token)
    {
        try
        {
            // Check if revoked
            if (_revokedTokens.ContainsKey(token))
                return null;

            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var signingInput = $"{parts[0]}.{parts[1]}";
            var signature = Base64UrlDecode(parts[2]);

            var valid = _signingKey.VerifyData(
                Encoding.UTF8.GetBytes(signingInput),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            if (!valid) return null;

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
            if (claims is null) return null;

            // Check expiration
            if (claims.TryGetValue("exp", out var expElement))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                if (exp < DateTimeOffset.UtcNow) return null;
            }

            return claims.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToString());
        }
        catch
        {
            return null;
        }
    }

    // ── Discovery ────────────────────────────────────────────

    public object GetDiscoveryDocument(string issuer)
    {
        return new
        {
            issuer,
            authorization_endpoint = $"{issuer}/api/v1/oauth/authorize",
            token_endpoint = $"{issuer}/api/v1/oauth/token",
            userinfo_endpoint = $"{issuer}/api/v1/oauth/userinfo",
            jwks_uri = $"{issuer}/.well-known/jwks.json",
            revocation_endpoint = $"{issuer}/api/v1/oauth/revoke",
            end_session_endpoint = $"{issuer}/api/v1/oauth/end-session",
            scopes_supported = new[] { "openid", "profile", "email", "groups", "offline_access" },
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "client_credentials", "refresh_token" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
            code_challenge_methods_supported = new[] { "S256", "plain" },
            claims_supported = new[] { "sub", "name", "email", "groups", "preferred_username", "given_name", "family_name" },
        };
    }

    // ── Helpers ───────────────────────────────────────────────

    public static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    private static string HashSecret(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash);
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Base64UrlEncode(bytes)[..length];
    }

    public bool ValidateClientCredentials(string clientId, string clientSecret)
    {
        var client = GetClient(clientId);
        if (client is null || !client.IsEnabled) return false;
        return client.ClientSecret == HashSecret(clientSecret);
    }

    /// <summary>
    /// Authenticate a user by UPN or DN + password. Returns the DirectoryObject on success.
    /// </summary>
    public async Task<Core.Models.DirectoryObject> AuthenticateUser(string username, string password)
    {
        // Try UPN first
        var user = await _store.GetByUpnAsync("default", username);
        user ??= await _store.GetByDnAsync("default", username);

        if (user is null) return null;

        var valid = await _passwordPolicy.ValidatePasswordAsync("default", user.DistinguishedName, password);
        return valid ? user : null;
    }
}
