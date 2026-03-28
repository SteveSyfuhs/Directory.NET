using System.Text.Json;
using System.Text.Json.Serialization;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Microsoft.Extensions.Logging;

namespace Directory.Security.CertificateAuthority;

/// <summary>
/// Persists CA state and issued certificates as directory objects in Cosmos DB.
/// CA root certificate: CN=CA,CN=Certification Authorities,CN=Public Key Services,CN=Services,CN=Configuration,...
/// Issued certs: CN={serial},CN=Enrolled Certificates,CN=Public Key Services,CN=System,{domainDn}
/// </summary>
public class CertificateStore
{
    private readonly IDirectoryStore _store;
    private readonly INamingContextService _ncService;
    private readonly ILogger<CertificateStore> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public CertificateStore(
        IDirectoryStore store,
        INamingContextService ncService,
        ILogger<CertificateStore> logger)
    {
        _store = store;
        _ncService = ncService;
        _logger = logger;
    }

    private string GetCaDn()
    {
        var configDn = _ncService.GetConfigurationDn();
        return $"CN=CA,CN=Certification Authorities,CN=Public Key Services,CN=Services,{configDn}";
    }

    private string GetEnrolledDn()
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        return $"CN=Enrolled Certificates,CN=Public Key Services,CN=System,{domainDn}";
    }

    // ── CA State ──────────────────────────────────────────────

    public async Task SaveCaStateAsync(CaPersistedState state, CancellationToken ct = default)
    {
        var dn = GetCaDn();
        var domainDn = _ncService.GetDomainNc().Dn;

        var existing = await _store.GetByDnAsync("default", dn, ct);

        if (existing != null)
        {
            var json = JsonSerializer.Serialize(state, JsonOpts);
            existing.SetAttribute("caStateData", new DirectoryAttribute("caStateData", json));
            existing.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existing, ct);
        }
        else
        {
            var parentDn = dn[("CN=CA,".Length)..];
            var usn = await _store.GetNextUsnAsync("default", domainDn, ct);
            var now = DateTimeOffset.UtcNow;

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = "default",
                DistinguishedName = dn,
                DomainDn = domainDn,
                ObjectCategory = "certificationAuthority",
                ObjectClass = ["top", "certificationAuthority"],
                Cn = "CA",
                DisplayName = state.CommonName,
                ParentDn = parentDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
            };

            var json = JsonSerializer.Serialize(state, JsonOpts);
            obj.SetAttribute("caStateData", new DirectoryAttribute("caStateData", json));

            await _store.CreateAsync(obj, ct);
        }

        _logger.LogInformation("CA state saved to directory");
    }

    public async Task<CaPersistedState> LoadCaStateAsync(CancellationToken ct = default)
    {
        var dn = GetCaDn();

        try
        {
            var obj = await _store.GetByDnAsync("default", dn, ct);
            if (obj == null || obj.IsDeleted) return null;

            var json = obj.GetAttribute("caStateData")?.GetFirstString();
            if (string.IsNullOrEmpty(json)) return null;

            return JsonSerializer.Deserialize<CaPersistedState>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load CA state (may not exist yet)");
            return null;
        }
    }

    // ── Issued Certificates ──────────────────────────────────

    public async Task SaveIssuedCertificateAsync(IssuedCertificateRecord record, CancellationToken ct = default)
    {
        var enrolledDn = GetEnrolledDn();
        var dn = $"CN={record.SerialNumber},{enrolledDn}";
        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);
        var now = DateTimeOffset.UtcNow;

        var existing = await _store.GetByDnAsync("default", dn, ct);
        if (existing != null)
        {
            var json = JsonSerializer.Serialize(record, JsonOpts);
            existing.SetAttribute("enrolledCertData", new DirectoryAttribute("enrolledCertData", json));
            existing.WhenChanged = now;
            await _store.UpdateAsync(existing, ct);
            return;
        }

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = "enrolledCertificate",
            ObjectClass = ["top", "enrolledCertificate"],
            Cn = record.SerialNumber,
            DisplayName = record.Subject,
            ParentDn = enrolledDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        var certJson = JsonSerializer.Serialize(record, JsonOpts);
        obj.SetAttribute("enrolledCertData", new DirectoryAttribute("enrolledCertData", certJson));

        await _store.CreateAsync(obj, ct);
    }

    public async Task<IssuedCertificateRecord> GetIssuedCertificateAsync(string serialNumber, CancellationToken ct = default)
    {
        var enrolledDn = GetEnrolledDn();
        var dn = $"CN={serialNumber},{enrolledDn}";

        var obj = await _store.GetByDnAsync("default", dn, ct);
        if (obj == null || obj.IsDeleted) return null;

        var json = obj.GetAttribute("enrolledCertData")?.GetFirstString();
        if (string.IsNullOrEmpty(json)) return null;

        return JsonSerializer.Deserialize<IssuedCertificateRecord>(json, JsonOpts);
    }

    public async Task<List<IssuedCertificateRecord>> ListIssuedCertificatesAsync(CancellationToken ct = default)
    {
        var enrolledDn = GetEnrolledDn();
        var results = new List<IssuedCertificateRecord>();

        try
        {
            var filter = new EqualityFilterNode("objectClass", "enrolledCertificate");
            var searchResult = await _store.SearchAsync("default", enrolledDn, SearchScope.SingleLevel, filter,
                null, pageSize: 1000, ct: ct);

            foreach (var entry in searchResult.Entries.Where(e => !e.IsDeleted))
            {
                var json = entry.GetAttribute("enrolledCertData")?.GetFirstString();
                if (string.IsNullOrEmpty(json)) continue;

                try
                {
                    var record = JsonSerializer.Deserialize<IssuedCertificateRecord>(json, JsonOpts);
                    if (record != null) results.Add(record);
                }
                catch { /* skip malformed records */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list issued certificates");
        }

        return results;
    }

    // ── Pending Certificate Requests ──────────────────────────────────

    private string GetPendingRequestsDn()
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        return $"CN=Pending Requests,CN=Public Key Services,CN=System,{domainDn}";
    }

    public async Task SavePendingRequestAsync(PendingCertificateRequest request, CancellationToken ct = default)
    {
        var parentDn = GetPendingRequestsDn();
        var dn = $"CN={request.RequestId},{parentDn}";
        var domainDn = _ncService.GetDomainNc().Dn;
        var usn = await _store.GetNextUsnAsync("default", domainDn, ct);
        var now = DateTimeOffset.UtcNow;

        var existing = await _store.GetByDnAsync("default", dn, ct);
        if (existing != null)
        {
            var json = JsonSerializer.Serialize(request, JsonOpts);
            existing.SetAttribute("pendingRequestData", new DirectoryAttribute("pendingRequestData", json));
            existing.WhenChanged = now;
            await _store.UpdateAsync(existing, ct);
            return;
        }

        var obj = new DirectoryObject
        {
            Id = dn.ToLowerInvariant(),
            TenantId = "default",
            DistinguishedName = dn,
            DomainDn = domainDn,
            ObjectCategory = "pendingCertRequest",
            ObjectClass = ["top", "pendingCertRequest"],
            Cn = request.RequestId,
            DisplayName = $"Pending: {request.SubjectDn}",
            ParentDn = parentDn,
            ObjectGuid = Guid.NewGuid().ToString(),
            WhenCreated = now,
            WhenChanged = now,
            USNCreated = usn,
            USNChanged = usn,
        };

        var requestJson = JsonSerializer.Serialize(request, JsonOpts);
        obj.SetAttribute("pendingRequestData", new DirectoryAttribute("pendingRequestData", requestJson));

        await _store.CreateAsync(obj, ct);
    }

    public async Task<PendingCertificateRequest> GetPendingRequestAsync(string requestId, CancellationToken ct = default)
    {
        var parentDn = GetPendingRequestsDn();
        var dn = $"CN={requestId},{parentDn}";

        var obj = await _store.GetByDnAsync("default", dn, ct);
        if (obj == null || obj.IsDeleted) return null;

        var json = obj.GetAttribute("pendingRequestData")?.GetFirstString();
        if (string.IsNullOrEmpty(json)) return null;

        return JsonSerializer.Deserialize<PendingCertificateRequest>(json, JsonOpts);
    }

    public async Task<List<PendingCertificateRequest>> ListPendingRequestsAsync(CancellationToken ct = default)
    {
        var parentDn = GetPendingRequestsDn();
        var results = new List<PendingCertificateRequest>();

        try
        {
            var filter = new EqualityFilterNode("objectClass", "pendingCertRequest");
            var searchResult = await _store.SearchAsync("default", parentDn, SearchScope.SingleLevel, filter,
                null, pageSize: 1000, ct: ct);

            foreach (var entry in searchResult.Entries.Where(e => !e.IsDeleted))
            {
                var json = entry.GetAttribute("pendingRequestData")?.GetFirstString();
                if (string.IsNullOrEmpty(json)) continue;

                try
                {
                    var record = JsonSerializer.Deserialize<PendingCertificateRequest>(json, JsonOpts);
                    if (record != null) results.Add(record);
                }
                catch { /* skip malformed records */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list pending certificate requests");
        }

        return results;
    }
}

// ── Persistence Models ──────────────────────────────────

/// <summary>
/// Persisted CA state including the root certificate and encrypted private key.
/// </summary>
public class CaPersistedState
{
    public string CommonName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public int KeySize { get; set; }
    public string HashAlgorithm { get; set; } = "SHA256";
    public string CertificatePem { get; set; } = "";

    /// <summary>
    /// PFX (PKCS#12) bytes stored as Base64. Contains both the CA certificate and private key.
    /// Protected with a machine-level password derived from the domain SID.
    /// </summary>
    public string PfxBase64 { get; set; } = "";

    public long NextSerialNumber { get; set; } = 1;
}

/// <summary>
/// Full record for an issued certificate stored in the directory.
/// </summary>
public class IssuedCertificateRecord
{
    public string SerialNumber { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public string Thumbprint { get; set; } = "";
    public string Status { get; set; } = "Active";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RevocationReason? RevocationReason { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public string CertificatePem { get; set; } = "";
    public List<string> SubjectAlternativeNames { get; set; } = [];
    public string KeyUsage { get; set; } = "";
    public List<string> EnhancedKeyUsage { get; set; } = [];
}
