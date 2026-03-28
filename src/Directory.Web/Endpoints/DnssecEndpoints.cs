using Directory.Dns.Dnssec;

namespace Directory.Web.Endpoints;

public static class DnssecEndpoints
{
    public static RouteGroupBuilder MapDnssecEndpoints(this RouteGroupBuilder group)
    {
        // ── Zone DNSSEC Settings ───────────────────────────────────────────

        group.MapGet("/{zone}", (string zone, DnssecService svc) =>
        {
            var settings = svc.GetZoneSettings(zone);
            return Results.Ok(new DnssecSettingsDto
            {
                ZoneName = settings.ZoneName,
                DnssecEnabled = settings.DnssecEnabled,
                SignatureValidityDays = settings.SignatureValidityDays,
                KeyRolloverIntervalDays = settings.KeyRolloverIntervalDays,
                LastSignedAt = settings.LastSignedAt?.ToString("o"),
            });
        })
        .WithName("GetDnssecSettings")
        .WithTags("DNSSEC");

        group.MapPut("/{zone}", (string zone, UpdateDnssecSettingsRequest request, DnssecService svc) =>
        {
            var settings = svc.UpdateZoneSettings(zone, request.DnssecEnabled,
                request.SignatureValidityDays, request.KeyRolloverIntervalDays);
            return Results.Ok(new DnssecSettingsDto
            {
                ZoneName = settings.ZoneName,
                DnssecEnabled = settings.DnssecEnabled,
                SignatureValidityDays = settings.SignatureValidityDays,
                KeyRolloverIntervalDays = settings.KeyRolloverIntervalDays,
                LastSignedAt = settings.LastSignedAt?.ToString("o"),
            });
        })
        .WithName("UpdateDnssecSettings")
        .WithTags("DNSSEC");

        // ── Zone Signing ───────────────────────────────────────────────────

        group.MapPost("/{zone}/sign", async (string zone, DnssecService svc, CancellationToken ct) =>
        {
            try
            {
                var signedCount = await svc.SignZoneAsync(zone, ct);
                return Results.Ok(new
                {
                    Zone = zone,
                    SignedRRsets = signedCount,
                    SignedAt = DateTimeOffset.UtcNow.ToString("o"),
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("SignDnssecZone")
        .WithTags("DNSSEC");

        // ── Key Management ─────────────────────────────────────────────────

        group.MapGet("/{zone}/keys", (string zone, DnssecService svc) =>
        {
            var keys = svc.GetKeyPairs(zone);
            return Results.Ok(keys.Select(k => new DnssecKeyDto
            {
                Id = k.Id,
                KeyType = k.KeyType.ToString(),
                Algorithm = k.Algorithm,
                AlgorithmName = k.Algorithm == 13 ? "ECDSAP256SHA256" : k.Algorithm == 8 ? "RSASHA256" : $"Algorithm {k.Algorithm}",
                KeyTag = k.KeyTag,
                CreatedAt = k.CreatedAt.ToString("o"),
                ExpiresAt = k.ExpiresAt?.ToString("o"),
                IsActive = k.IsActive,
                PublicKeyBase64 = Convert.ToBase64String(k.PublicKey),
            }));
        })
        .WithName("ListDnssecKeys")
        .WithTags("DNSSEC");

        group.MapPost("/{zone}/keys", (string zone, GenerateKeyRequest request, DnssecService svc) =>
        {
            if (!Enum.TryParse<DnssecKeyType>(request.KeyType, true, out var keyType))
                return Results.Problem(statusCode: 400, detail: $"Invalid key type: {request.KeyType}. Use KSK or ZSK.");

            var algorithm = request.Algorithm ?? 13;
            if (algorithm != 13 && algorithm != 8)
                return Results.Problem(statusCode: 400, detail: "Algorithm must be 13 (ECDSAP256SHA256) or 8 (RSASHA256).");

            try
            {
                var key = svc.GenerateKeyPair(zone, keyType, algorithm);
                return Results.Created($"/api/v1/dns/dnssec/{zone}/keys/{key.Id}", new DnssecKeyDto
                {
                    Id = key.Id,
                    KeyType = key.KeyType.ToString(),
                    Algorithm = key.Algorithm,
                    AlgorithmName = key.Algorithm == 13 ? "ECDSAP256SHA256" : "RSASHA256",
                    KeyTag = key.KeyTag,
                    CreatedAt = key.CreatedAt.ToString("o"),
                    ExpiresAt = key.ExpiresAt?.ToString("o"),
                    IsActive = key.IsActive,
                    PublicKeyBase64 = Convert.ToBase64String(key.PublicKey),
                });
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: 400, detail: ex.Message);
            }
        })
        .WithName("GenerateDnssecKey")
        .WithTags("DNSSEC");

        group.MapDelete("/{zone}/keys/{id}", (string zone, string id, DnssecService svc) =>
        {
            var deleted = svc.DeleteKeyPair(zone, id);
            return deleted ? Results.NoContent() : Results.Problem(statusCode: 404, detail: $"Key '{id}' not found in zone '{zone}'.");
        })
        .WithName("DeleteDnssecKey")
        .WithTags("DNSSEC");

        // ── DS Record ──────────────────────────────────────────────────────

        group.MapGet("/{zone}/ds", (string zone, DnssecService svc) =>
        {
            var ds = svc.GenerateDsRecord(zone);
            if (ds == null)
                return Results.Problem(statusCode: 404, detail: $"No active KSK found for zone '{zone}'. Generate a KSK first.");

            return Results.Ok(new DnssecDsDto
            {
                ZoneName = ds.ZoneName,
                KeyTag = ds.KeyTag,
                Algorithm = ds.Algorithm,
                DigestType = ds.DigestType,
                Digest = ds.Digest,
                DsRecord = ds.ToString(),
            });
        })
        .WithName("GetDnssecDsRecord")
        .WithTags("DNSSEC");

        return group;
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────

public class DnssecSettingsDto
{
    public string ZoneName { get; set; } = "";
    public bool DnssecEnabled { get; set; }
    public int SignatureValidityDays { get; set; }
    public int KeyRolloverIntervalDays { get; set; }
    public string LastSignedAt { get; set; }
}

public class DnssecKeyDto
{
    public string Id { get; set; } = "";
    public string KeyType { get; set; } = "";
    public int Algorithm { get; set; }
    public string AlgorithmName { get; set; } = "";
    public int KeyTag { get; set; }
    public string CreatedAt { get; set; } = "";
    public string ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string PublicKeyBase64 { get; set; } = "";
}

public class DnssecDsDto
{
    public string ZoneName { get; set; } = "";
    public int KeyTag { get; set; }
    public int Algorithm { get; set; }
    public int DigestType { get; set; }
    public string Digest { get; set; } = "";
    public string DsRecord { get; set; } = "";
}

public record UpdateDnssecSettingsRequest(
    bool DnssecEnabled,
    int? SignatureValidityDays = null,
    int? KeyRolloverIntervalDays = null
);

public record GenerateKeyRequest(
    string KeyType,
    int? Algorithm = 13
);
