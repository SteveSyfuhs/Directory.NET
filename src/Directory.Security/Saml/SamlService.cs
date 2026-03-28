using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.Saml;

// ────────────────────────────────────────────────────────────
//  Models
// ────────────────────────────────────────────────────────────

public class SamlServiceProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;
    public string SingleLogoutServiceUrl { get; set; }
    public string Certificate { get; set; } // SP's public cert (Base64 DER) for encryption
    public string NameIdFormat { get; set; } = "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress";
    public List<SamlAttributeMapping> AttributeMappings { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SamlAttributeMapping
{
    public string SamlAttributeName { get; set; } = string.Empty;
    public string DirectoryAttribute { get; set; } = string.Empty;
}

// ────────────────────────────────────────────────────────────
//  Service
// ────────────────────────────────────────────────────────────

public class SamlService
{
    private readonly ConcurrentDictionary<string, SamlServiceProvider> _serviceProviders = new();
    private RSA _signingKey;
    private X509Certificate2 _signingCertificate;
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _namingContextService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILogger<SamlService> _logger;

    private const string SamlCertAttribute = "samlSigningCertPfx";

    public SamlService(IDirectoryStore store, INamingContextService namingContextService, IPasswordPolicy passwordPolicy, ILogger<SamlService> logger)
    {
        _store = store;
        _namingContextService = namingContextService;
        _passwordPolicy = passwordPolicy;
        _logger = logger;

        // Generate an ephemeral certificate initially; LoadOrCreateSigningCertificateAsync replaces it
        (_signingKey, _signingCertificate) = CreateSelfSignedCertificate();
    }

    public X509Certificate2 SigningCertificate => _signingCertificate;

    // ── Certificate persistence ───────────────────────────────

    /// <summary>
    /// Loads the SAML signing certificate from the directory store, or generates and persists a new one.
    /// Call this during hosted service initialization after the directory store is available.
    /// </summary>
    public async Task LoadOrCreateSigningCertificateAsync(CancellationToken ct = default)
    {
        try
        {
            var domainDn = _namingContextService.GetDomainNc().Dn;
            var configDn = $"CN=SamlConfig,CN=System,{domainDn}";

            var configObj = await _store.GetByDnAsync("default", configDn, ct);
            if (configObj is not null)
            {
                var pfxAttr = configObj.GetAttribute(SamlCertAttribute)?.GetFirstString();
                if (!string.IsNullOrEmpty(pfxAttr))
                {
                    var pfxBytes = Convert.FromBase64String(pfxAttr);
                    var cert = LoadCertificateFromPfx(pfxBytes);
                    if (cert is not null)
                    {
                        var rsa = cert.GetRSAPrivateKey();
                        if (rsa is not null)
                        {
                            _signingCertificate.Dispose();
                            _signingKey.Dispose();
                            _signingCertificate = cert;
                            _signingKey = rsa;
                            _logger.LogInformation("SAML signing certificate loaded from directory store");
                            return;
                        }
                    }
                }
            }

            // Certificate not found or invalid — generate a new one and persist it
            var (newKey, newCert) = CreateSelfSignedCertificate();
            var pfxBytesNew = ExportCertificateToPfx(newCert);
            var pfxBase64 = Convert.ToBase64String(pfxBytesNew);

            await PersistSigningCertificateAsync(configDn, domainDn, pfxBase64, configObj, ct);

            _signingCertificate.Dispose();
            _signingKey.Dispose();
            _signingCertificate = newCert;
            _signingKey = newKey;
            _logger.LogInformation("SAML signing certificate generated and persisted to directory store");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load/persist SAML signing certificate from directory store — using ephemeral certificate");
        }
    }

    private async Task PersistSigningCertificateAsync(string configDn, string domainDn, string pfxBase64,
        DirectoryObject existing, CancellationToken ct)
    {
        if (existing is not null)
        {
            existing.SetAttribute(SamlCertAttribute, new DirectoryAttribute(SamlCertAttribute, pfxBase64));
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
                Cn = "SamlConfig",
                ParentDn = $"CN=System,{domainDn}",
                WhenCreated = now,
                WhenChanged = now,
            };
            obj.SetAttribute(SamlCertAttribute, new DirectoryAttribute(SamlCertAttribute, pfxBase64));
            await _store.CreateAsync(obj, ct);
        }
    }

    private static (RSA key, X509Certificate2 cert) CreateSelfSignedCertificate()
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Directory.NET SAML IdP",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(10));
        return (key, cert);
    }

    private static byte[] ExportCertificateToPfx(X509Certificate2 cert)
    {
        return cert.Export(X509ContentType.Pfx);
    }

    private static X509Certificate2 LoadCertificateFromPfx(byte[] pfxBytes)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch
        {
            return null;
        }
    }

    // ── SP management ────────────────────────────────────────

    public IReadOnlyList<SamlServiceProvider> GetAllServiceProviders()
        => _serviceProviders.Values.OrderBy(sp => sp.Name).ToList();

    public SamlServiceProvider GetServiceProvider(string id)
        => _serviceProviders.TryGetValue(id, out var sp) ? sp : null;

    public SamlServiceProvider GetServiceProviderByEntityId(string entityId)
        => _serviceProviders.Values.FirstOrDefault(sp => sp.EntityId == entityId);

    public SamlServiceProvider CreateServiceProvider(SamlServiceProvider template)
    {
        var sp = new SamlServiceProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            EntityId = template.EntityId,
            Name = template.Name,
            AssertionConsumerServiceUrl = template.AssertionConsumerServiceUrl,
            SingleLogoutServiceUrl = template.SingleLogoutServiceUrl,
            Certificate = template.Certificate,
            NameIdFormat = template.NameIdFormat ?? "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress",
            AttributeMappings = template.AttributeMappings ?? new(),
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _serviceProviders[sp.Id] = sp;
        _logger.LogInformation("SAML SP created: {Id} ({Name})", sp.Id, sp.Name);
        return sp;
    }

    public SamlServiceProvider UpdateServiceProvider(string id, SamlServiceProvider updates)
    {
        if (!_serviceProviders.TryGetValue(id, out var existing))
            return null;

        existing.EntityId = updates.EntityId ?? existing.EntityId;
        existing.Name = updates.Name ?? existing.Name;
        existing.AssertionConsumerServiceUrl = updates.AssertionConsumerServiceUrl ?? existing.AssertionConsumerServiceUrl;
        existing.SingleLogoutServiceUrl = updates.SingleLogoutServiceUrl;
        existing.Certificate = updates.Certificate ?? existing.Certificate;
        existing.NameIdFormat = updates.NameIdFormat ?? existing.NameIdFormat;
        existing.AttributeMappings = updates.AttributeMappings ?? existing.AttributeMappings;
        existing.IsEnabled = updates.IsEnabled;

        return existing;
    }

    public bool DeleteServiceProvider(string id)
        => _serviceProviders.TryRemove(id, out _);

    // ── Authentication ───────────────────────────────────────

    public async Task<Core.Models.DirectoryObject> AuthenticateUser(string username, string password)
    {
        var user = await _store.GetByUpnAsync("default", username);
        user ??= await _store.GetByDnAsync("default", username);
        if (user is null) return null;

        var valid = await _passwordPolicy.ValidatePasswordAsync("default", user.DistinguishedName, password);
        return valid ? user : null;
    }

    // ── IdP Metadata ─────────────────────────────────────────

    public string GenerateIdPMetadata(string entityId, string ssoUrl, string sloUrl)
    {
        var certBase64 = Convert.ToBase64String(_signingCertificate.RawData);

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<EntityDescriptor xmlns=""urn:oasis:names:tc:SAML:2.0:metadata""
                  entityID=""{EscapeXml(entityId)}"">
  <IDPSSODescriptor WantAuthnRequestsSigned=""false""
                    protocolSupportEnumeration=""urn:oasis:names:tc:SAML:2.0:protocol"">
    <KeyDescriptor use=""signing"">
      <ds:KeyInfo xmlns:ds=""http://www.w3.org/2000/09/xmldsig#"">
        <ds:X509Data>
          <ds:X509Certificate>{certBase64}</ds:X509Certificate>
        </ds:X509Data>
      </ds:KeyInfo>
    </KeyDescriptor>
    <NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</NameIDFormat>
    <NameIDFormat>urn:oasis:names:tc:SAML:2.0:nameid-format:persistent</NameIDFormat>
    <NameIDFormat>urn:oasis:names:tc:SAML:2.0:nameid-format:unspecified</NameIDFormat>
    <SingleSignOnService Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST""
                         Location=""{EscapeXml(ssoUrl)}"" />
    <SingleSignOnService Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect""
                         Location=""{EscapeXml(ssoUrl)}"" />
    <SingleLogoutService Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST""
                         Location=""{EscapeXml(sloUrl)}"" />
  </IDPSSODescriptor>
</EntityDescriptor>";
    }

    // ── SAML Response / Assertion ────────────────────────────

    public string GenerateSamlResponse(
        Core.Models.DirectoryObject user,
        SamlServiceProvider sp,
        string idpEntityId,
        string inResponseTo = null)
    {
        var responseId = "_" + Guid.NewGuid().ToString("N");
        var assertionId = "_" + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var nowStr = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notBefore = now.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var notOnOrAfter = now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ");

        var nameId = ResolveNameId(user, sp.NameIdFormat);
        var attributes = ResolveAttributes(user, sp.AttributeMappings);

        var attributeStatements = new StringBuilder();
        if (attributes.Count > 0)
        {
            attributeStatements.AppendLine(@"      <AttributeStatement>");
            foreach (var (name, value) in attributes)
            {
                attributeStatements.AppendLine(
                    $@"        <Attribute Name=""{EscapeXml(name)}"" NameFormat=""urn:oasis:names:tc:SAML:2.0:attrname-format:uri"">
          <AttributeValue>{EscapeXml(value)}</AttributeValue>
        </Attribute>");
            }
            attributeStatements.AppendLine(@"      </AttributeStatement>");
        }

        var inResponseToAttr = inResponseTo is not null
            ? $@" InResponseTo=""{EscapeXml(inResponseTo)}"""
            : "";

        // Build the assertion XML (unsigned first, then we sign it)
        var assertionXml = $@"<saml:Assertion xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion""
                    ID=""{assertionId}"" Version=""2.0"" IssueInstant=""{nowStr}"">
      <saml:Issuer>{EscapeXml(idpEntityId)}</saml:Issuer>
      <saml:Subject>
        <saml:NameID Format=""{EscapeXml(sp.NameIdFormat)}"">{EscapeXml(nameId)}</saml:NameID>
        <saml:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"">
          <saml:SubjectConfirmationData{inResponseToAttr}
            Recipient=""{EscapeXml(sp.AssertionConsumerServiceUrl)}""
            NotOnOrAfter=""{notOnOrAfter}"" />
        </saml:SubjectConfirmation>
      </saml:Subject>
      <saml:Conditions NotBefore=""{notBefore}"" NotOnOrAfter=""{notOnOrAfter}"">
        <saml:AudienceRestriction>
          <saml:Audience>{EscapeXml(sp.EntityId)}</saml:Audience>
        </saml:AudienceRestriction>
      </saml:Conditions>
      <saml:AuthnStatement AuthnInstant=""{nowStr}"" SessionIndex=""{assertionId}"">
        <saml:AuthnContext>
          <saml:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml:AuthnContextClassRef>
        </saml:AuthnContext>
      </saml:AuthnStatement>
{attributeStatements}    </saml:Assertion>";

        // Sign the assertion
        var signedAssertion = SignXmlAssertion(assertionXml, assertionId);

        // Wrap in SAML Response
        var responseXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol""
                ID=""{responseId}""
                Version=""2.0""
                IssueInstant=""{nowStr}""
                Destination=""{EscapeXml(sp.AssertionConsumerServiceUrl)}""{inResponseToAttr}>
  <saml:Issuer xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">{EscapeXml(idpEntityId)}</saml:Issuer>
  <samlp:Status>
    <samlp:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
  </samlp:Status>
  {signedAssertion}
</samlp:Response>";

        return responseXml;
    }

    public string GenerateLogoutResponse(string idpEntityId, string destination, string inResponseTo)
    {
        var responseId = "_" + Guid.NewGuid().ToString("N");
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var inResponseToAttr = inResponseTo is not null
            ? $@" InResponseTo=""{EscapeXml(inResponseTo)}"""
            : "";

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<samlp:LogoutResponse xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol""
                      xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion""
                      ID=""{responseId}""
                      Version=""2.0""
                      IssueInstant=""{nowStr}""
                      Destination=""{EscapeXml(destination)}""{inResponseToAttr}>
  <saml:Issuer>{EscapeXml(idpEntityId)}</saml:Issuer>
  <samlp:Status>
    <samlp:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
  </samlp:Status>
</samlp:LogoutResponse>";
    }

    // ── AuthnRequest parsing (from SP) ───────────────────────

    public (string issuer, string id, string acsUrl) ParseAuthnRequest(string samlRequest)
    {
        try
        {
            byte[] decoded;
            try
            {
                // First try plain base64
                decoded = Convert.FromBase64String(samlRequest);
            }
            catch
            {
                // Try URL-decoded then base64
                decoded = Convert.FromBase64String(Uri.UnescapeDataString(samlRequest));
            }

            // Try decompressing (HTTP-Redirect uses DEFLATE)
            string xml;
            try
            {
                using var ms = new MemoryStream(decoded);
                using var deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(deflate);
                xml = reader.ReadToEnd();
            }
            catch
            {
                // If not compressed, use directly
                xml = Encoding.UTF8.GetString(decoded);
            }

            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
            nsManager.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");

            var authnRequest = doc.SelectSingleNode("//samlp:AuthnRequest", nsManager);
            var issuer = doc.SelectSingleNode("//saml:Issuer", nsManager)?.InnerText;
            var id = authnRequest?.Attributes?["ID"]?.Value;
            var acsUrl = authnRequest?.Attributes?["AssertionConsumerServiceURL"]?.Value;

            return (issuer, id, acsUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SAML AuthnRequest");
            return (null, null, null);
        }
    }

    // ── XML Digital Signature ────────────────────────────────

    private string SignXmlAssertion(string assertionXml, string assertionId)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(assertionXml);

        var signedXml = new SignedXml(doc)
        {
            SigningKey = _signingKey,
        };

        var reference = new Reference { Uri = "#" + assertionId };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(_signingCertificate));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var signatureElement = signedXml.GetXml();

        // Insert signature after Issuer element
        var issuerNode = doc.GetElementsByTagName("Issuer", "urn:oasis:names:tc:SAML:2.0:assertion")[0];
        if (issuerNode?.ParentNode is not null)
        {
            doc.DocumentElement.InsertAfter(doc.ImportNode(signatureElement, true), issuerNode);
        }

        return doc.OuterXml;
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string ResolveNameId(Core.Models.DirectoryObject user, string nameIdFormat)
    {
        return nameIdFormat switch
        {
            "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"
                => user.UserPrincipalName ?? user.Mail ?? user.SAMAccountName ?? user.ObjectGuid,
            "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent"
                => user.ObjectGuid,
            _ => user.UserPrincipalName ?? user.ObjectGuid,
        };
    }

    private static List<(string name, string value)> ResolveAttributes(
        Core.Models.DirectoryObject user, List<SamlAttributeMapping> mappings)
    {
        var result = new List<(string, string)>();

        // Default mappings if none specified
        if (mappings.Count == 0)
        {
            mappings = new List<SamlAttributeMapping>
            {
                new() { SamlAttributeName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress", DirectoryAttribute = "userPrincipalName" },
                new() { SamlAttributeName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", DirectoryAttribute = "displayName" },
                new() { SamlAttributeName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname", DirectoryAttribute = "givenName" },
                new() { SamlAttributeName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname", DirectoryAttribute = "sn" },
                new() { SamlAttributeName = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn", DirectoryAttribute = "userPrincipalName" },
            };
        }

        foreach (var mapping in mappings)
        {
            var value = ResolveDirectoryAttribute(user, mapping.DirectoryAttribute);
            if (value is not null)
            {
                result.Add((mapping.SamlAttributeName, value));
            }
        }

        return result;
    }

    private static string ResolveDirectoryAttribute(Core.Models.DirectoryObject user, string attrName)
    {
        return attrName.ToLowerInvariant() switch
        {
            "userprincipalname" => user.UserPrincipalName,
            "mail" => user.Mail,
            "displayname" => user.DisplayName,
            "cn" => user.Cn,
            "givenname" => user.GivenName,
            "sn" => user.Sn,
            "samaccountname" => user.SAMAccountName,
            "objectguid" => user.ObjectGuid,
            "objectsid" => user.ObjectSid,
            "title" => user.Title,
            "department" => user.Department,
            "company" => user.Company,
            _ => user.GetAttribute(attrName)?.GetFirstString(),
        };
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
