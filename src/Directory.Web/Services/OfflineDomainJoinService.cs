using System.Text.Json;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Web.Models;
using Microsoft.Extensions.Options;

namespace Directory.Web.Services;

public class DjoinProvisionRequest
{
    public string ComputerName { get; set; } = "";
    public string OrganizationalUnit { get; set; }
    public string MachinePasswordOverride { get; set; } // Optional; auto-generated if null
    public bool ReuseExistingAccount { get; set; }       // Allow re-provisioning existing account
}

public class DjoinProvisionResult
{
    public bool Success { get; set; }
    public string ComputerDn { get; set; } = "";
    public string ComputerSid { get; set; } = "";
    public string DjoinBlob { get; set; } = "";  // Base64-encoded provisioning blob
    public string ErrorMessage { get; set; }
}

/// <summary>
/// The djoin blob contains everything the client needs to join the domain offline.
/// </summary>
public class DjoinBlobContent
{
    public string Version { get; set; } = "1.0";
    public string DomainDnsName { get; set; } = "";
    public string DomainNetBiosName { get; set; } = "";
    public string DomainSid { get; set; } = "";
    public string DomainDn { get; set; } = "";
    public string ForestDnsName { get; set; } = "";
    public string DcName { get; set; } = "";
    public string DcAddress { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string ComputerDn { get; set; } = "";
    public string ComputerSid { get; set; } = "";
    public string MachinePassword { get; set; } = "";
    public List<string> ServicePrincipalNames { get; set; } = new();
    public string SiteName { get; set; } = "";
    public DateTimeOffset ProvisionedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } // Blob validity (default 30 days)
}

public class DjoinValidationResult
{
    public bool Valid { get; set; }
    public string ComputerName { get; set; }
    public string ComputerDn { get; set; }
    public string ComputerSid { get; set; }
    public string DomainDnsName { get; set; }
    public DateTimeOffset? ProvisionedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool? AccountExists { get; set; }
    public string ErrorMessage { get; set; }
}

public class OfflineDomainJoinService
{
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _ncService;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly DomainConfiguration _domainConfig;
    private readonly DcNodeOptions _dcNodeOptions;
    private readonly ILogger<OfflineDomainJoinService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public OfflineDomainJoinService(
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        INamingContextService ncService,
        IPasswordPolicy passwordPolicy,
        DomainConfiguration domainConfig,
        IOptions<DcNodeOptions> dcNodeOptions,
        ILogger<OfflineDomainJoinService> logger)
    {
        _store = store;
        _ridAllocator = ridAllocator;
        _ncService = ncService;
        _passwordPolicy = passwordPolicy;
        _domainConfig = domainConfig;
        _dcNodeOptions = dcNodeOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Provision an offline domain join: creates a computer account (or reuses an existing one)
    /// and generates a base64-encoded blob containing all information a machine needs to join offline.
    /// </summary>
    public async Task<DjoinProvisionResult> ProvisionOfflineJoinAsync(DjoinProvisionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ComputerName))
            return new DjoinProvisionResult { Success = false, ErrorMessage = "Computer name is required" };

        var domainDn = _ncService.GetDomainNc().Dn;
        var computerName = request.ComputerName.TrimEnd('$');
        var samAccountName = computerName + "$";

        var containerDn = !string.IsNullOrWhiteSpace(request.OrganizationalUnit)
            ? request.OrganizationalUnit
            : $"CN=Computers,{domainDn}";

        var dn = $"CN={computerName},{containerDn}";

        // Generate or use override password
        var machinePassword = !string.IsNullOrWhiteSpace(request.MachinePasswordOverride)
            ? request.MachinePasswordOverride
            : GenerateMachinePassword();

        string objectSid;

        // Check for existing account
        var existing = await _store.GetByDnAsync("default", dn, ct);
        if (existing != null && !existing.IsDeleted)
        {
            if (!request.ReuseExistingAccount)
                return new DjoinProvisionResult
                {
                    Success = false,
                    ErrorMessage = $"Computer account already exists at {dn}. Set ReuseExistingAccount to true to re-provision."
                };

            // Re-provision existing account: reset password and update timestamps
            objectSid = existing.ObjectSid ?? "";
            existing.WhenChanged = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(existing, ct);
            await _passwordPolicy.SetPasswordAsync("default", dn, machinePassword);
        }
        else
        {
            // Create new computer account
            objectSid = await _ridAllocator.GenerateObjectSidAsync("default", domainDn);
            var now = DateTimeOffset.UtcNow;
            var usn = await _store.GetNextUsnAsync("default", domainDn, ct);

            var obj = new DirectoryObject
            {
                Id = dn.ToLowerInvariant(),
                TenantId = "default",
                DistinguishedName = dn,
                DomainDn = domainDn,
                ObjectCategory = "computer",
                ObjectClass = ["top", "person", "organizationalPerson", "user", "computer"],
                Cn = computerName,
                SAMAccountName = samAccountName,
                DisplayName = computerName,
                ParentDn = containerDn,
                ObjectGuid = Guid.NewGuid().ToString(),
                ObjectSid = objectSid,
                // WORKSTATION_TRUST_ACCOUNT (0x1000) | ACCOUNTDISABLE (0x2)
                UserAccountControl = 0x1000 | 0x2,
                PrimaryGroupId = 515, // Domain Computers
                WhenCreated = now,
                WhenChanged = now,
                USNCreated = usn,
                USNChanged = usn,
                SAMAccountType = 0x30000001,
                ServicePrincipalName =
                [
                    $"HOST/{computerName}",
                    $"HOST/{computerName}.{_domainConfig.DomainDnsName}",
                    $"RestrictedKrbHost/{computerName}",
                    $"RestrictedKrbHost/{computerName}.{_domainConfig.DomainDnsName}",
                ],
            };

            // Mark as offline-join provisioned
            obj.SetAttribute("ms-DS-OfflineJoinProvisioned",
                new DirectoryAttribute("ms-DS-OfflineJoinProvisioned", "TRUE"));

            await _store.CreateAsync(obj, ct);
            await _passwordPolicy.SetPasswordAsync("default", dn, machinePassword);
        }

        // Build the djoin blob
        var blobContent = new DjoinBlobContent
        {
            Version = "1.0",
            DomainDnsName = _domainConfig.DomainDnsName,
            DomainNetBiosName = _domainConfig.NetBiosName,
            DomainSid = _domainConfig.DomainSid,
            DomainDn = domainDn,
            ForestDnsName = _domainConfig.ForestDnsName,
            DcName = _dcNodeOptions.Hostname,
            DcAddress = _dcNodeOptions.Hostname,
            ComputerName = computerName,
            ComputerDn = dn,
            ComputerSid = objectSid,
            MachinePassword = machinePassword,
            ServicePrincipalNames =
            [
                $"HOST/{computerName}",
                $"HOST/{computerName}.{_domainConfig.DomainDnsName}",
                $"RestrictedKrbHost/{computerName}",
                $"RestrictedKrbHost/{computerName}.{_domainConfig.DomainDnsName}",
            ],
            SiteName = _dcNodeOptions.SiteName,
            ProvisionedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };

        var json = JsonSerializer.Serialize(blobContent, JsonOptions);
        var djoinBlob = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        _logger.LogInformation("Provisioned offline domain join for {ComputerName} at {Dn}", computerName, dn);

        return new DjoinProvisionResult
        {
            Success = true,
            ComputerDn = dn,
            ComputerSid = objectSid,
            DjoinBlob = djoinBlob,
        };
    }

    /// <summary>
    /// Decode and validate a djoin blob: check the account exists, the blob is not expired.
    /// </summary>
    public async Task<DjoinValidationResult> ValidateBlobAsync(string blob, CancellationToken ct = default)
    {
        DjoinBlobContent content;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(blob));
            content = JsonSerializer.Deserialize<DjoinBlobContent>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            return new DjoinValidationResult
            {
                Valid = false,
                ErrorMessage = $"Failed to decode blob: {ex.Message}"
            };
        }

        if (content == null)
            return new DjoinValidationResult { Valid = false, ErrorMessage = "Blob decoded to null" };

        // Check expiration
        if (DateTimeOffset.UtcNow > content.ExpiresAt)
        {
            return new DjoinValidationResult
            {
                Valid = false,
                ComputerName = content.ComputerName,
                ComputerDn = content.ComputerDn,
                ComputerSid = content.ComputerSid,
                DomainDnsName = content.DomainDnsName,
                ProvisionedAt = content.ProvisionedAt,
                ExpiresAt = content.ExpiresAt,
                ErrorMessage = "Blob has expired",
            };
        }

        // Check if the computer account still exists
        var obj = await _store.GetByDnAsync("default", content.ComputerDn, ct);
        var accountExists = obj != null && !obj.IsDeleted;

        return new DjoinValidationResult
        {
            Valid = accountExists,
            ComputerName = content.ComputerName,
            ComputerDn = content.ComputerDn,
            ComputerSid = content.ComputerSid,
            DomainDnsName = content.DomainDnsName,
            ProvisionedAt = content.ProvisionedAt,
            ExpiresAt = content.ExpiresAt,
            AccountExists = accountExists,
            ErrorMessage = accountExists ? null : "Computer account no longer exists",
        };
    }

    /// <summary>
    /// Revoke an offline join provision: disable the pre-provisioned account and reset its password
    /// to invalidate the blob.
    /// </summary>
    public async Task<bool> RevokeBlobAsync(string computerName, CancellationToken ct = default)
    {
        var domainDn = _ncService.GetDomainNc().Dn;
        var name = computerName.TrimEnd('$');

        var filter = new EqualityFilterNode("sAMAccountName", name + "$");
        var result = await _store.SearchAsync("default", domainDn, SearchScope.WholeSubtree, filter,
            null, pageSize: 1, ct: ct);

        var obj = result.Entries.FirstOrDefault(e => !e.IsDeleted);
        if (obj == null)
            return false;

        // Ensure the account is disabled
        obj.UserAccountControl |= 0x2;
        obj.WhenChanged = DateTimeOffset.UtcNow;

        // Reset the machine password to invalidate the blob
        var newPassword = Guid.NewGuid().ToString("N") + "Xx9!";
        await _passwordPolicy.SetPasswordAsync("default", obj.DistinguishedName, newPassword);
        await _store.UpdateAsync(obj, ct);

        _logger.LogInformation("Revoked offline domain join for {ComputerName}", name);
        return true;
    }

    private static string GenerateMachinePassword()
    {
        // Generate a strong 120-character random machine password (similar to AD behavior)
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+[]{}|;:',.<>?/~`";
        var random = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[120];
        random.GetBytes(bytes);
        var password = new char[120];
        for (int i = 0; i < 120; i++)
            password[i] = chars[bytes[i] % chars.Length];
        return new string(password);
    }
}
