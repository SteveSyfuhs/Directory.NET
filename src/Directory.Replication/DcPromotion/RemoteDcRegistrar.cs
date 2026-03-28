using Directory.Ldap.Client;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.DcPromotion;

/// <summary>
/// Registers a new DC in the remote Active Directory during domain join.
/// Creates machine account, server object, nTDSDSA object, and connection objects
/// on the source DC via LDAP.
/// </summary>
public class RemoteDcRegistrar
{
    private readonly ILogger _logger;

    public RemoteDcRegistrar(ILogger<RemoteDcRegistrar> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create or update the computer account for this DC in the domain.
    /// Sets userAccountControl = SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION (0x2020)
    /// </summary>
    public async Task<string> EnsureMachineAccountAsync(
        LdapClient ldap,
        string domainDn,
        string hostname,
        string dnsHostname,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Ensuring machine account for {Hostname} in {DomainDn}", hostname, domainDn);

        // Search for existing computer account
        var filter = $"(&(objectClass=computer)(sAMAccountName={hostname}$))";
        var searchResult = await ldap.SearchAsync(
            domainDn,
            Directory.Core.Models.SearchScope.WholeSubtree,
            filter,
            ["distinguishedName", "userAccountControl"],
            sizeLimit: 1,
            ct: ct);

        if (searchResult.Entries.Count > 0)
        {
            // Found existing account — update userAccountControl to include SERVER_TRUST_ACCOUNT
            var existingDn = searchResult.Entries[0].DistinguishedName;
            _logger.LogInformation("Found existing computer account {Dn}, updating userAccountControl", existingDn);

            var currentUac = 0;
            var uacStr = searchResult.Entries[0].GetFirstValue("userAccountControl");
            if (uacStr is not null)
                int.TryParse(uacStr, out currentUac);

            // SERVER_TRUST_ACCOUNT (0x2000) | TRUSTED_FOR_DELEGATION (0x20)
            const int serverTrustFlags = 0x2020;
            if ((currentUac & serverTrustFlags) != serverTrustFlags)
            {
                var newUac = (currentUac & ~0x0002) | serverTrustFlags; // Clear ACCOUNTDISABLE, set trust flags
                var modResult = await ldap.ModifyAsync(existingDn,
                [
                    new LdapModification
                    {
                        Operation = LdapModOperation.Replace,
                        AttributeName = "userAccountControl",
                        Values = [newUac.ToString()]
                    }
                ], ct);

                if (!modResult.Success)
                {
                    _logger.LogWarning("Failed to update userAccountControl on {Dn}: {Code} {Msg}",
                        existingDn, modResult.ResultCode, modResult.DiagnosticMessage);
                }
            }

            return existingDn;
        }

        // Create new computer account under CN=Computers,{domainDn}
        var computerDn = $"CN={hostname},CN=Computers,{domainDn}";
        _logger.LogInformation("Creating new computer account {Dn}", computerDn);

        var attributes = new Dictionary<string, List<string>>
        {
            ["objectClass"] = ["top", "person", "organizationalPerson", "user", "computer"],
            ["cn"] = [hostname],
            ["sAMAccountName"] = [$"{hostname}$"],
            ["userAccountControl"] = ["8224"], // 0x2020 = SERVER_TRUST_ACCOUNT | TRUSTED_FOR_DELEGATION
            ["dNSHostName"] = [dnsHostname],
            ["servicePrincipalName"] =
            [
                $"HOST/{hostname}",
                $"HOST/{dnsHostname}",
                $"ldap/{hostname}",
                $"ldap/{dnsHostname}"
            ]
        };

        var result = await ldap.AddAsync(computerDn, attributes, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create computer account {computerDn}: result={result.ResultCode}, {result.DiagnosticMessage}");
        }

        _logger.LogInformation("Computer account created: {Dn}", computerDn);
        return computerDn;
    }

    /// <summary>
    /// Create the server object under Sites configuration.
    /// CN={hostname},CN=Servers,CN={siteName},CN=Sites,CN=Configuration,{forestDn}
    /// </summary>
    public async Task<string> EnsureServerObjectAsync(
        LdapClient ldap,
        string configurationDn,
        string siteName,
        string hostname,
        string dnsHostname,
        string computerDn,
        CancellationToken ct = default)
    {
        var serverDn = $"CN={hostname},CN=Servers,CN={siteName},CN=Sites,{configurationDn}";
        _logger.LogInformation("Ensuring server object {Dn}", serverDn);

        // Check if it already exists
        var searchResult = await ldap.SearchAsync(
            serverDn,
            Directory.Core.Models.SearchScope.BaseObject,
            "(objectClass=server)",
            ["cn"],
            sizeLimit: 1,
            ct: ct);

        if (searchResult.Entries.Count > 0)
        {
            _logger.LogInformation("Server object {Dn} already exists", serverDn);
            return serverDn;
        }

        var attributes = new Dictionary<string, List<string>>
        {
            ["objectClass"] = ["top", "server"],
            ["cn"] = [hostname],
            ["dNSHostName"] = [dnsHostname],
            ["serverReference"] = [computerDn]
        };

        var result = await ldap.AddAsync(serverDn, attributes, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create server object {serverDn}: result={result.ResultCode}, {result.DiagnosticMessage}");
        }

        _logger.LogInformation("Server object created: {Dn}", serverDn);
        return serverDn;
    }

    /// <summary>
    /// Create the NTDS Settings object (nTDSDSA) under the server object.
    /// This is what makes this server a Domain Controller.
    /// </summary>
    public async Task<string> CreateNtdsSettingsAsync(
        LdapClient ldap,
        string serverDn,
        Guid invocationId,
        string domainDn,
        string configurationDn,
        string schemaDn,
        int behaviorVersion = 7,
        CancellationToken ct = default)
    {
        var ntdsDn = $"CN=NTDS Settings,{serverDn}";
        _logger.LogInformation("Creating NTDS Settings object {Dn}", ntdsDn);

        // Check if it already exists
        var searchResult = await ldap.SearchAsync(
            ntdsDn,
            Directory.Core.Models.SearchScope.BaseObject,
            "(objectClass=nTDSDSA)",
            ["cn"],
            sizeLimit: 1,
            ct: ct);

        if (searchResult.Entries.Count > 0)
        {
            _logger.LogInformation("NTDS Settings object {Dn} already exists", ntdsDn);
            return ntdsDn;
        }

        // invocationId is stored as an octet string (GUID bytes)
        var invocationIdHex = Convert.ToHexString(invocationId.ToByteArray());

        var attributes = new Dictionary<string, List<string>>
        {
            ["objectClass"] = ["top", "nTDSDSA"],
            ["cn"] = ["NTDS Settings"],
            ["invocationId"] = [invocationIdHex],
            ["msDS-HasMasterNCs"] = [schemaDn, configurationDn, domainDn],
            ["hasMasterNCs"] = [schemaDn, configurationDn, domainDn],
            ["msDS-Behavior-Version"] = [behaviorVersion.ToString()],
            ["options"] = ["1"],              // IS_GC
            ["systemFlags"] = ["33554432"]    // FLAG_DISALLOW_MOVE_ON_DELETE
        };

        var result = await ldap.AddAsync(ntdsDn, attributes, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create NTDS Settings {ntdsDn}: result={result.ResultCode}, {result.DiagnosticMessage}");
        }

        _logger.LogInformation("NTDS Settings object created: {Dn}", ntdsDn);
        return ntdsDn;
    }

    /// <summary>
    /// Create an inbound replication connection object from the source DC.
    /// </summary>
    public async Task<string> CreateConnectionObjectAsync(
        LdapClient ldap,
        string localNtdsSettingsDn,
        string sourceNtdsSettingsDn,
        string connectionName,
        CancellationToken ct = default)
    {
        var connectionDn = $"CN={connectionName},{localNtdsSettingsDn}";
        _logger.LogInformation("Creating connection object {Dn} from {Source}", connectionDn, sourceNtdsSettingsDn);

        // 168-byte schedule: all 0xFF means always replicate (7 days * 24 hours = 168 bytes)
        var schedule = new byte[188]; // 20-byte header + 168-byte data
        // Header: size (4 bytes LE) + bandwidth (4 bytes) + numberOfSchedules (4 bytes) + schedule offset etc.
        BitConverter.GetBytes(188).CopyTo(schedule, 0); // total size
        schedule[8] = 1; // numberOfSchedules
        schedule[12] = 20; // offset to first schedule header
        schedule[16] = 1; // type = SCHEDULE_INTERVAL
        schedule[18] = 20; // offset of data from the schedule header start
        // Fill the 168 data bytes with 0xFF
        for (var i = 20; i < 188; i++)
            schedule[i] = 0xFF;
        var scheduleHex = Convert.ToHexString(schedule);

        var attributes = new Dictionary<string, List<string>>
        {
            ["objectClass"] = ["top", "nTDSConnection"],
            ["cn"] = [connectionName],
            ["fromServer"] = [sourceNtdsSettingsDn],
            ["enabledConnection"] = ["TRUE"],
            ["options"] = ["0"],
            ["schedule"] = [scheduleHex]
        };

        var result = await ldap.AddAsync(connectionDn, attributes, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create connection object {connectionDn}: result={result.ResultCode}, {result.DiagnosticMessage}");
        }

        _logger.LogInformation("Connection object created: {Dn}", connectionDn);
        return connectionDn;
    }

    /// <summary>
    /// Register SPNs for all DC services on the computer account.
    /// </summary>
    public async Task RegisterServicePrincipalNamesAsync(
        LdapClient ldap,
        string computerDn,
        string hostname,
        string dnsHostname,
        string domainDnsName,
        Guid ntdsaGuid,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Registering SPNs on {Dn}", computerDn);

        var spns = new List<string>
        {
            $"ldap/{hostname}",
            $"ldap/{dnsHostname}",
            $"ldap/{dnsHostname}/{domainDnsName}",
            $"HOST/{hostname}",
            $"HOST/{dnsHostname}",
            $"GC/{dnsHostname}/{domainDnsName}",
            $"E3514235-4B06-11D1-AB04-00C04FC2DCD2/{ntdsaGuid}/{domainDnsName}",
            $"DNS/{dnsHostname}"
        };

        var modResult = await ldap.ModifyAsync(computerDn,
        [
            new LdapModification
            {
                Operation = LdapModOperation.Replace,
                AttributeName = "servicePrincipalName",
                Values = spns
            }
        ], ct);

        if (!modResult.Success)
        {
            _logger.LogWarning("Failed to update SPNs on {Dn}: {Code} {Msg}",
                computerDn, modResult.ResultCode, modResult.DiagnosticMessage);
        }
        else
        {
            _logger.LogInformation("SPNs registered successfully on {Dn}: {Count} SPNs", computerDn, spns.Count);
        }
    }
}
