using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Lsa;

/// <summary>
/// Extended MS-LSAD / MS-LSAT operations providing additional LSA functionality
/// beyond the base set needed for domain join. Includes SID-to-name translation,
/// name-to-SID resolution, policy queries, trusted domain enumeration, and secret management.
///
/// These operations complement the base <see cref="LsaOperations"/> class
/// and follow the same NDR marshalling patterns.
///
/// Reference: [MS-LSAD] https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-lsad
/// Reference: [MS-LSAT] https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-lsat
/// </summary>
public class LsaFullOperations
{
    private readonly IDirectoryStore _store;
    private readonly IRidAllocator _ridAllocator;
    private readonly INamingContextService _ncService;
    private readonly ILogger<LsaFullOperations> _logger;

    // Well-known SID mappings
    private static readonly Dictionary<string, (string Name, ushort Type, string Domain)> WellKnownSids =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-0-0"] = ("Nobody", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-1-0"] = ("Everyone", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-2-0"] = ("LOCAL", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-3-0"] = ("CREATOR OWNER", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-3-1"] = ("CREATOR GROUP", LsaConstants.SidTypeWellKnownGroup, ""),
            ["S-1-5-1"] = ("DIALUP", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-2"] = ("NETWORK", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-3"] = ("BATCH", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-4"] = ("INTERACTIVE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-6"] = ("SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-7"] = ("ANONYMOUS LOGON", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-9"] = ("ENTERPRISE DOMAIN CONTROLLERS", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-10"] = ("SELF", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-11"] = ("Authenticated Users", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-13"] = ("TERMINAL SERVER USER", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-14"] = ("REMOTE INTERACTIVE LOGON", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-18"] = ("SYSTEM", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-19"] = ("LOCAL SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-20"] = ("NETWORK SERVICE", LsaConstants.SidTypeWellKnownGroup, "NT AUTHORITY"),
            ["S-1-5-32-544"] = ("Administrators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-545"] = ("Users", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-546"] = ("Guests", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-547"] = ("Power Users", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-548"] = ("Account Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-549"] = ("Server Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-550"] = ("Print Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-551"] = ("Backup Operators", LsaConstants.SidTypeAlias, "BUILTIN"),
            ["S-1-5-32-552"] = ("Replicators", LsaConstants.SidTypeAlias, "BUILTIN"),
        };

    // Well-known domain RIDs
    private static readonly Dictionary<uint, (string Name, ushort Type)> WellKnownDomainRids = new()
    {
        [500] = ("Administrator", LsaConstants.SidTypeUser),
        [501] = ("Guest", LsaConstants.SidTypeUser),
        [502] = ("krbtgt", LsaConstants.SidTypeUser),
        [512] = ("Domain Admins", LsaConstants.SidTypeGroup),
        [513] = ("Domain Users", LsaConstants.SidTypeGroup),
        [514] = ("Domain Guests", LsaConstants.SidTypeGroup),
        [515] = ("Domain Computers", LsaConstants.SidTypeGroup),
        [516] = ("Domain Controllers", LsaConstants.SidTypeGroup),
        [517] = ("Cert Publishers", LsaConstants.SidTypeAlias),
        [518] = ("Schema Admins", LsaConstants.SidTypeGroup),
        [519] = ("Enterprise Admins", LsaConstants.SidTypeGroup),
        [520] = ("Group Policy Creator Owners", LsaConstants.SidTypeGroup),
        [553] = ("RAS and IAS Servers", LsaConstants.SidTypeAlias),
    };

    public LsaFullOperations(
        IDirectoryStore store,
        IRidAllocator ridAllocator,
        INamingContextService ncService,
        ILogger<LsaFullOperations> logger)
    {
        _store = store;
        _ridAllocator = ridAllocator;
        _ncService = ncService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 15 — LsarLookupSids
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarLookupSids (opnum 15): Translates an array of SIDs to account names and domains.
    /// Wire in:  [in] policy handle, [in] PLSAPR_SID_ENUM_BUFFER SidEnumBuffer,
    ///           [out] PLSAPR_REFERENCED_DOMAIN_LIST ReferencedDomains,
    ///           [in,out] PLSAPR_TRANSLATED_NAMES TranslatedNames, [in] LookupLevel,
    ///           [in,out] MappedCount
    /// Wire out: [out] ReferencedDomains, [out] TranslatedNames, [out] MappedCount, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> LsarLookupSidsAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] LSAPR_HANDLE PolicyHandle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var policyHandle = GetPolicyHandle(context, attr, uuid);

        // [in] PLSAPR_SID_ENUM_BUFFER: Entries (uint32), SidInfo pointer
        var sidCount = reader.ReadUInt32();
        var sidInfoPtr = reader.ReadUInt32();

        var sids = new List<string>();
        if (sidInfoPtr != 0 && sidCount > 0)
        {
            // Conformant array of PLSAPR_SID_INFORMATION
            var maxCount = reader.ReadUInt32();

            // Read SID pointers
            var sidPtrs = new uint[sidCount];
            for (int i = 0; i < (int)sidCount; i++)
            {
                sidPtrs[i] = reader.ReadUInt32();
            }

            // Read deferred SID data
            for (int i = 0; i < (int)sidCount; i++)
            {
                if (sidPtrs[i] != 0)
                {
                    sids.Add(ReadSid(reader));
                }
                else
                {
                    sids.Add("");
                }
            }
        }

        // [in,out] PLSAPR_TRANSLATED_NAMES: Entries, Names pointer (initially empty)
        var translatedEntries = reader.ReadUInt32();
        var translatedPtr = reader.ReadUInt32();

        // [in] LSAP_LOOKUP_LEVEL
        var lookupLevel = reader.ReadUInt16();

        // [in,out] MappedCount
        var mappedCount = reader.ReadUInt32();

        _logger.LogDebug("LsarLookupSids: {Count} SIDs, LookupLevel={Level}", sidCount, lookupLevel);

        // Resolve SIDs
        var domainNc = _ncService.GetDomainNc();
        var domains = new List<(string Name, string Sid)>();
        var results = new List<(string Name, ushort Type, int DomainIndex)>();

        for (int i = 0; i < sids.Count; i++)
        {
            var sid = sids[i];
            var resolved = await ResolveSidAsync(policyHandle.TenantId, sid, domainNc, ct);
            results.Add(resolved);

            // Add domain to referenced domains if not already present
            if (!string.IsNullOrEmpty(resolved.Name))
            {
                string domainSid = GetDomainSidFromSid(sid);
                string domainName = GetDomainNameForSid(sid, domainNc);

                if (!string.IsNullOrEmpty(domainSid))
                {
                    int domIdx = domains.FindIndex(d =>
                        string.Equals(d.Sid, domainSid, StringComparison.OrdinalIgnoreCase));
                    if (domIdx < 0)
                    {
                        domIdx = domains.Count;
                        domains.Add((domainName, domainSid));
                    }
                    results[i] = (resolved.Name, resolved.Type, domIdx);
                }
            }
        }

        int mapped = results.Count(r => r.Type != LsaConstants.SidTypeUnknown);

        return WriteLookupSidsResponse(domains, results, (uint)mapped);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 14 — LsarLookupNames
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarLookupNames (opnum 14): Translates account names to SIDs.
    /// Wire in:  [in] policy handle, [in] Count, [in] PRPC_UNICODE_STRING Names[],
    ///           [out] ReferencedDomains, [in,out] TranslatedSids, [in] LookupLevel, [in,out] MappedCount
    /// Wire out: [out] ReferencedDomains, [out] TranslatedSids, [out] MappedCount, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> LsarLookupNamesAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] LSAPR_HANDLE PolicyHandle
        var (attr, uuid) = reader.ReadContextHandle();
        var policyHandle = GetPolicyHandle(context, attr, uuid);

        // [in] ULONG Count
        var count = reader.ReadUInt32();

        // [in] PRPC_UNICODE_STRING Names[Count] — conformant array
        var maxCount = reader.ReadUInt32();

        var nameHeaders = new (ushort Length, ushort MaxLength, uint Ptr)[count];
        for (int i = 0; i < (int)count; i++)
        {
            nameHeaders[i].Length = reader.ReadUInt16();
            nameHeaders[i].MaxLength = reader.ReadUInt16();
            reader.Align(4);
            nameHeaders[i].Ptr = reader.ReadUInt32();
        }

        var names = new string[count];
        for (int i = 0; i < (int)count; i++)
        {
            if (nameHeaders[i].Ptr != 0)
            {
                names[i] = reader.ReadConformantVaryingString();
            }
            else
            {
                names[i] = "";
            }
        }

        // [in,out] PLSAPR_TRANSLATED_SIDS (Entries, Sids pointer)
        var translatedEntries = reader.ReadUInt32();
        var translatedPtr = reader.ReadUInt32();

        // [in] LSAP_LOOKUP_LEVEL
        var lookupLevel = reader.ReadUInt16();

        // [in,out] MappedCount
        var mappedCount = reader.ReadUInt32();

        _logger.LogDebug("LsarLookupNames: {Count} names, LookupLevel={Level}", count, lookupLevel);

        var domainNc = _ncService.GetDomainNc();
        var domains = new List<(string Name, string Sid)>();
        var results = new List<(string Sid, ushort Type, int DomainIndex, uint Rid)>();
        int mapped = 0;

        for (int i = 0; i < (int)count; i++)
        {
            var name = names[i];
            if (string.IsNullOrEmpty(name))
            {
                results.Add(("", LsaConstants.SidTypeUnknown, -1, 0));
                continue;
            }

            // Parse DOMAIN\Name or Name@Domain format
            string domainPart = null;
            string namePart = name;

            if (name.Contains('\\'))
            {
                var parts = name.Split('\\', 2);
                domainPart = parts[0];
                namePart = parts[1];
            }
            else if (name.Contains('@'))
            {
                var parts = name.Split('@', 2);
                namePart = parts[0];
                domainPart = parts[1];
            }

            // Check well-known names
            var wellKnown = WellKnownSids.FirstOrDefault(kvp =>
                string.Equals(kvp.Value.Name, namePart, StringComparison.OrdinalIgnoreCase));

            if (wellKnown.Key is not null)
            {
                string wkDomainName = wellKnown.Value.Domain;
                string wkSid = wellKnown.Key;
                int domIdx = EnsureDomain(domains, wkDomainName, GetDomainSidFromSid(wkSid));
                uint rid = ExtractRid(wkSid);
                results.Add((wkSid, wellKnown.Value.Type, domIdx, rid));
                mapped++;
                continue;
            }

            // Search in the directory
            var obj = await _store.GetBySamAccountNameAsync(
                policyHandle.TenantId, domainNc.Dn, namePart, ct);

            if (obj?.ObjectSid is not null)
            {
                string objDomainSid = GetDomainSidFromSid(obj.ObjectSid);
                string objDomainName = ExtractNetBiosName(domainNc.Dn);
                int domIdx = EnsureDomain(domains, objDomainName, objDomainSid);
                uint rid = ExtractRid(obj.ObjectSid);
                ushort sidType = ClassifyObjectSidType(obj);
                results.Add((obj.ObjectSid, sidType, domIdx, rid));
                mapped++;
            }
            else
            {
                results.Add(("", LsaConstants.SidTypeUnknown, -1, 0));
            }
        }

        return WriteLookupNamesResponse(domains, results, (uint)mapped, (uint)count);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 7 / 46 — LsarQueryInformationPolicy
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarQueryInformationPolicy (opnum 7/46): Returns policy information.
    /// Supports PolicyPrimaryDomainInformation (3), PolicyAccountDomainInformation (5),
    /// PolicyLsaServerRoleInformation (6), and PolicyDnsDomainInformation (12).
    /// </summary>
    public Task<byte[]> LsarQueryInformationPolicyAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (attr, uuid) = reader.ReadContextHandle();
        var policyHandle = GetPolicyHandle(context, attr, uuid);

        var infoClass = reader.ReadUInt16();

        _logger.LogDebug("LsarQueryInformationPolicy: InfoClass={InfoClass}", infoClass);

        var domainNc = _ncService.GetDomainNc();
        var writer = new NdrWriter();

        switch (infoClass)
        {
            case LsaConstants.PolicyPrimaryDomainInformation:
                WritePrimaryDomainInfo(writer, domainNc);
                break;

            case LsaConstants.PolicyAccountDomainInformation:
                WriteAccountDomainInfo(writer, domainNc);
                break;

            case LsaConstants.PolicyLsaServerRoleInformation:
                WriteLsaServerRoleInfo(writer);
                break;

            case LsaConstants.PolicyDnsDomainInformation:
            case LsaConstants.PolicyDnsDomainInformationInt:
                WriteDnsDomainInfo(writer, domainNc);
                break;

            default:
                _logger.LogWarning("Unsupported policy info class: {InfoClass}", infoClass);
                writer.WritePointer(true); // null info pointer
                writer.WriteUInt32(LsaConstants.StatusInvalidParameter);
                return Task.FromResult(writer.ToArray());
        }

        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 50 — LsarEnumerateTrustedDomains
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarEnumerateTrustedDomainsEx (opnum 50): Enumerates trusted domain objects.
    /// In this single-domain implementation, returns an empty list.
    /// Wire in:  [in] policy handle, [in,out] EnumerationContext, [in] PrefMaxLen
    /// Wire out: [out] PLSAPR_TRUSTED_ENUM_BUFFER_EX, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> LsarEnumerateTrustedDomainsAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (attr, uuid) = reader.ReadContextHandle();
        var policyHandle = GetPolicyHandle(context, attr, uuid);

        var enumContext = reader.ReadUInt32();
        var prefMaxLen = reader.ReadUInt32();

        _logger.LogDebug("LsarEnumerateTrustedDomains: Context={Context}", enumContext);

        var writer = new NdrWriter();

        // [out] EnumerationContext
        writer.WriteUInt32(0);

        // LSAPR_TRUSTED_ENUM_BUFFER_EX: EntriesRead (uint32), Information pointer
        writer.WriteUInt32(0); // EntriesRead = 0
        writer.WritePointer(true); // null array pointer

        // STATUS_NO_MORE_ENTRIES when enumeration is complete
        writer.WriteUInt32(LsaConstants.StatusNoMoreEntries);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 16 — LsarCreateSecret
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarCreateSecret (opnum 16): Creates a new secret object in the LSA database.
    /// Secrets are used to store trust passwords, service account credentials, etc.
    /// Wire in:  [in] policy handle, [in] PRPC_UNICODE_STRING SecretName, [in] DesiredAccess
    /// Wire out: [out] secret handle, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> LsarCreateSecretAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        var (attr, uuid) = reader.ReadContextHandle();
        var policyHandle = GetPolicyHandle(context, attr, uuid);

        // SecretName: RPC_UNICODE_STRING
        var nameLen = reader.ReadUInt16();
        var nameMaxLen = reader.ReadUInt16();
        reader.Align(4);
        var namePtr = reader.ReadUInt32();
        string secretName = "";
        if (namePtr != 0)
        {
            secretName = reader.ReadConformantVaryingString();
        }

        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("LsarCreateSecret: Name={Name}", secretName);

        if (string.IsNullOrWhiteSpace(secretName))
        {
            var failWriter = new NdrWriter();
            failWriter.WriteContextHandle(0, Guid.Empty);
            failWriter.WriteUInt32(LsaConstants.StatusInvalidParameter);
            return failWriter.ToArray();
        }

        // Store the secret as a directory object in the System container
        var domainNc = _ncService.GetDomainNc();
        string secretDn = $"CN={secretName},CN=System,{domainNc.Dn}";

        // Check if secret already exists
        var existing = await _store.GetByDnAsync(policyHandle.TenantId, secretDn, ct);
        if (existing is not null)
        {
            var collisionWriter = new NdrWriter();
            collisionWriter.WriteContextHandle(0, Guid.Empty);
            collisionWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return collisionWriter.ToArray();
        }

        var secretObj = new DirectoryObject
        {
            Id = secretDn.ToLowerInvariant(),
            TenantId = policyHandle.TenantId,
            DomainDn = domainNc.Dn,
            DistinguishedName = secretDn,
            ParentDn = $"CN=System,{domainNc.Dn}",
            Cn = secretName,
            ObjectClass = ["top", "secret"],
            ObjectCategory = "secret",
        };

        await _store.CreateAsync(secretObj, ct);

        // Create a secret handle
        var secretHandle = new LsaSecretHandle
        {
            GrantedAccess = desiredAccess,
            SecretDn = secretDn,
            TenantId = policyHandle.TenantId,
        };

        var handleBytes = context.ContextHandles.CreateHandle(secretHandle);
        var handleGuid = new Guid(handleBytes.AsSpan().Slice(4, 16));

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, handleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 30 — LsarQuerySecret
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarQuerySecret (opnum 30): Retrieves the current and/or old value of a secret.
    /// Wire in:  [in] secret handle, [in,out,unique] CurrentValue, [in,out,unique] CurrentValueSetTime,
    ///           [in,out,unique] OldValue, [in,out,unique] OldValueSetTime
    /// Wire out: [out] CurrentValue, [out] CurrentValueSetTime, [out] OldValue, [out] OldValueSetTime, [out] NTSTATUS
    /// </summary>
    public async Task<byte[]> LsarQuerySecretAsync(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // [in] LSAPR_HANDLE SecretHandle (20 bytes)
        var (handleAttr, handleUuid) = reader.ReadContextHandle();

        var handleBytes = new byte[20];
        BitConverter.GetBytes(handleAttr).CopyTo(handleBytes, 0);
        handleUuid.TryWriteBytes(handleBytes.AsSpan(4));

        var secretHandle = context.ContextHandles.GetHandle<LsaSecretHandle>(handleBytes);
        if (secretHandle is null)
        {
            var failWriter = new NdrWriter();
            failWriter.WritePointer(true); // null current value
            failWriter.WritePointer(true); // null current time
            failWriter.WritePointer(true); // null old value
            failWriter.WritePointer(true); // null old time
            failWriter.WriteUInt32(LsaConstants.StatusInvalidHandle);
            return failWriter.ToArray();
        }

        // Read request parameters (pointers indicating which values to return)
        var wantCurrentValue = reader.ReadUInt32() != 0;
        var wantCurrentTime = reader.ReadUInt32() != 0;
        var wantOldValue = reader.ReadUInt32() != 0;
        var wantOldTime = reader.ReadUInt32() != 0;

        _logger.LogDebug("LsarQuerySecret: Secret={Secret}", secretHandle.SecretDn);

        var secretObj = await _store.GetByDnAsync(secretHandle.TenantId, secretHandle.SecretDn, ct);

        var writer = new NdrWriter();

        // Current value
        if (wantCurrentValue && secretObj is not null)
        {
            var currentValueAttr = secretObj.GetAttribute("currentValue");
            if (currentValueAttr?.GetFirstBytes() is { } currentBytes)
            {
                writer.WritePointer(false); // non-null
                // LSAPR_CR_CIPHER_VALUE: Length(uint32), MaxLength(uint32), Buffer pointer
                writer.WriteUInt32((uint)currentBytes.Length);
                writer.WriteUInt32((uint)currentBytes.Length);
                writer.WritePointer(false);
                writer.WriteUInt32((uint)currentBytes.Length); // conformant max
                writer.WriteUInt32(0);                          // offset
                writer.WriteUInt32((uint)currentBytes.Length); // actual
                writer.WriteBytes(currentBytes);
            }
            else
            {
                writer.WritePointer(true); // null
            }
        }
        else
        {
            writer.WritePointer(true); // null
        }

        // Current value set time
        if (wantCurrentTime && secretObj is not null)
        {
            writer.WritePointer(false);
            writer.WriteInt64(secretObj.WhenChanged.ToFileTime());
        }
        else
        {
            writer.WritePointer(true);
        }

        // Old value
        if (wantOldValue && secretObj is not null)
        {
            var oldValueAttr = secretObj.GetAttribute("priorValue");
            if (oldValueAttr?.GetFirstBytes() is { } oldBytes)
            {
                writer.WritePointer(false);
                writer.WriteUInt32((uint)oldBytes.Length);
                writer.WriteUInt32((uint)oldBytes.Length);
                writer.WritePointer(false);
                writer.WriteUInt32((uint)oldBytes.Length);
                writer.WriteUInt32(0);
                writer.WriteUInt32((uint)oldBytes.Length);
                writer.WriteBytes(oldBytes);
            }
            else
            {
                writer.WritePointer(true);
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        // Old value set time
        if (wantOldTime && secretObj is not null)
        {
            writer.WritePointer(false);
            writer.WriteInt64(secretObj.WhenCreated.ToFileTime());
        }
        else
        {
            writer.WritePointer(true);
        }

        writer.WriteUInt32(LsaConstants.StatusSuccess);
        return writer.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Opnum 44 — LsarOpenPolicy2
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// LsarOpenPolicy2 (opnum 44): Opens a handle to the LSA policy object.
    /// Wire in:  [in, unique] SystemName, [in] ObjectAttributes, [in] DesiredAccess
    /// Wire out: [out] PolicyHandle, [out] NTSTATUS
    /// </summary>
    public Task<byte[]> LsarOpenPolicy2Async(
        ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // SystemName: [in, unique, string] wchar_t*
        var systemNamePtr = reader.ReadPointer();
        string systemName = "";
        if (systemNamePtr != 0)
        {
            systemName = reader.ReadConformantVaryingString();
        }

        // LSAPR_OBJECT_ATTRIBUTES (24 bytes)
        reader.ReadUInt32(); // Length
        var rootDirPtr = reader.ReadUInt32();
        var objectNamePtr = reader.ReadUInt32();
        reader.ReadUInt32(); // Attributes
        var secDescPtr = reader.ReadUInt32();
        var secQosPtr = reader.ReadUInt32();

        if (objectNamePtr != 0)
            reader.ReadConformantVaryingString();
        if (secQosPtr != 0)
        {
            reader.ReadUInt32();
            reader.ReadUInt16();
            reader.ReadByte();
            reader.ReadByte();
        }

        var desiredAccess = reader.ReadUInt32();

        var handle = new LsaPolicyHandle
        {
            GrantedAccess = desiredAccess,
            SystemName = systemName,
            TenantId = context.TenantId,
            DomainDn = context.DomainDn,
        };

        var handleBytes = context.ContextHandles.CreateHandle(handle);
        var handleGuid = new Guid(handleBytes.AsSpan().Slice(4, 16));

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, handleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helper methods
    // ─────────────────────────────────────────────────────────────────────

    private LsaPolicyHandle GetPolicyHandle(RpcCallContext context, uint attr, Guid uuid)
    {
        var handleBytes = new byte[20];
        BitConverter.GetBytes(attr).CopyTo(handleBytes, 0);
        uuid.TryWriteBytes(handleBytes.AsSpan(4));

        var handle = context.ContextHandles.GetHandle<LsaPolicyHandle>(handleBytes);
        if (handle is null)
        {
            throw new RpcFaultException(LsaConstants.StatusInvalidHandle, "Invalid LSA policy handle");
        }
        return handle;
    }

    private async Task<(string Name, ushort Type, int DomainIndex)> ResolveSidAsync(
        string tenantId, string sid, NamingContext domainNc, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sid))
            return ("", LsaConstants.SidTypeUnknown, -1);

        // Check well-known SIDs
        if (WellKnownSids.TryGetValue(sid, out var wellKnown))
        {
            return (wellKnown.Name, wellKnown.Type, -1);
        }

        // Check well-known domain RIDs
        uint rid = ExtractRid(sid);
        if (WellKnownDomainRids.TryGetValue(rid, out var domainRid))
        {
            return (domainRid.Name, domainRid.Type, -1);
        }

        // Search the directory
        var filter = new EqualityFilterNode("objectSid", sid);
        var result = await _store.SearchAsync(
            tenantId, domainNc.Dn, SearchScope.WholeSubtree, filter,
            attributes: ["sAMAccountName", "objectClass"],
            sizeLimit: 1, ct: ct);

        if (result.Entries.Count > 0)
        {
            var obj = result.Entries[0];
            return (obj.SAMAccountName ?? obj.Cn ?? "", ClassifyObjectSidType(obj), -1);
        }

        return ("", LsaConstants.SidTypeUnknown, -1);
    }

    private static string GetDomainSidFromSid(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return "";

        // For well-known SIDs without a domain (S-1-5-xx), return the authority
        var parts = sid.Split('-');
        if (parts.Length <= 4) return ""; // No domain component

        // Domain SID is everything except the last sub-authority
        int lastDash = sid.LastIndexOf('-');
        return lastDash > 0 ? sid[..lastDash] : "";
    }

    private static string GetDomainNameForSid(string sid, NamingContext domainNc)
    {
        if (sid.StartsWith("S-1-5-32"))
            return "BUILTIN";
        if (sid.StartsWith("S-1-5-") && sid.Split('-').Length <= 4)
            return "NT AUTHORITY";
        if (sid.StartsWith("S-1-1-") || sid.StartsWith("S-1-2-") || sid.StartsWith("S-1-3-"))
            return "";

        return ExtractNetBiosName(domainNc.Dn);
    }

    private static uint ExtractRid(string sid)
    {
        if (string.IsNullOrEmpty(sid)) return 0;
        int lastDash = sid.LastIndexOf('-');
        if (lastDash < 0) return 0;
        return uint.TryParse(sid.AsSpan(lastDash + 1), out uint rid) ? rid : 0;
    }

    private static int EnsureDomain(List<(string Name, string Sid)> domains, string name, string sid)
    {
        if (string.IsNullOrEmpty(sid)) return -1;

        int idx = domains.FindIndex(d =>
            string.Equals(d.Sid, sid, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            idx = domains.Count;
            domains.Add((name, sid));
        }
        return idx;
    }

    private static ushort ClassifyObjectSidType(DirectoryObject obj)
    {
        var classes = obj.ObjectClass;
        if (classes.Contains("computer", StringComparer.OrdinalIgnoreCase))
            return LsaConstants.SidTypeComputer;
        if (classes.Contains("user", StringComparer.OrdinalIgnoreCase))
            return LsaConstants.SidTypeUser;
        if (classes.Contains("group", StringComparer.OrdinalIgnoreCase))
            return LsaConstants.SidTypeGroup;
        return LsaConstants.SidTypeUnknown;
    }

    private static string ExtractNetBiosName(string domainDn)
    {
        if (string.IsNullOrEmpty(domainDn)) return "DOMAIN";
        foreach (var part in domainDn.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..].ToUpperInvariant();
        }
        return "DOMAIN";
    }

    private static string ReadSid(NdrReader reader)
    {
        var maxSubAuthCount = reader.ReadUInt32();
        var revision = reader.ReadByte();
        var subAuthCount = reader.ReadByte();
        var authBytes = reader.ReadBytes(6).ToArray();

        long authority = 0;
        for (int i = 0; i < 6; i++)
        {
            authority = (authority << 8) | authBytes[i];
        }

        reader.Align(4);

        var sb = new StringBuilder();
        sb.Append($"S-{revision}-{authority}");
        for (int i = 0; i < subAuthCount; i++)
        {
            sb.Append($"-{reader.ReadUInt32()}");
        }

        return sb.ToString();
    }

    private byte[] WriteLookupSidsResponse(
        List<(string Name, string Sid)> domains,
        List<(string Name, ushort Type, int DomainIndex)> results,
        uint mappedCount)
    {
        var writer = new NdrWriter();

        // [out] PLSAPR_REFERENCED_DOMAIN_LIST (pointer)
        writer.WritePointer(false);

        // LSAPR_REFERENCED_DOMAIN_LIST: Entries, MaxEntries, Domains pointer
        writer.WriteUInt32((uint)domains.Count);
        writer.WriteUInt32((uint)domains.Count); // MaxEntries

        if (domains.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)domains.Count); // conformant array max count

            foreach (var (name, sid) in domains)
            {
                writer.WriteRpcUnicodeString(name);
                // SID pointer
                writer.WritePointer(false);
            }

            writer.FlushDeferred();

            // Write deferred SID data
            foreach (var (_, sid) in domains)
            {
                writer.WriteRpcSid(sid);
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        // [out] LSAPR_TRANSLATED_NAMES: Entries, Names pointer
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)results.Count); // conformant max count

            foreach (var (name, type, domIdx) in results)
            {
                writer.WriteUInt16(type); // Use (SID_NAME_USE)
                writer.Align(4);
                writer.WriteRpcUnicodeString(name); // Name
                writer.WriteInt32(domIdx); // DomainIndex
            }

            writer.FlushDeferred();
        }
        else
        {
            writer.WritePointer(true);
        }

        // [out] MappedCount
        writer.WriteUInt32(mappedCount);

        // NTSTATUS
        uint count = (uint)results.Count;
        uint status = mappedCount == count
            ? LsaConstants.StatusSuccess
            : mappedCount > 0
                ? LsaConstants.StatusSomeMapped
                : LsaConstants.StatusNoneMapped;

        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    private byte[] WriteLookupNamesResponse(
        List<(string Name, string Sid)> domains,
        List<(string Sid, ushort Type, int DomainIndex, uint Rid)> results,
        uint mappedCount,
        uint totalCount)
    {
        var writer = new NdrWriter();

        // [out] PLSAPR_REFERENCED_DOMAIN_LIST (pointer)
        writer.WritePointer(false);

        // LSAPR_REFERENCED_DOMAIN_LIST
        writer.WriteUInt32((uint)domains.Count);
        writer.WriteUInt32((uint)domains.Count);

        if (domains.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)domains.Count);

            foreach (var (name, sid) in domains)
            {
                writer.WriteRpcUnicodeString(name);
                writer.WritePointer(false); // SID pointer
            }

            writer.FlushDeferred();

            foreach (var (_, sid) in domains)
            {
                writer.WriteRpcSid(sid);
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        // [out] LSAPR_TRANSLATED_SIDS: Entries, Sids pointer
        writer.WriteUInt32((uint)results.Count);

        if (results.Count > 0)
        {
            writer.WritePointer(false);
            writer.WriteUInt32((uint)results.Count);

            foreach (var (_, type, domIdx, rid) in results)
            {
                writer.WriteUInt16(type); // Use
                writer.Align(4);
                writer.WriteUInt32(rid); // RelativeId
                writer.WriteInt32(domIdx); // DomainIndex
            }
        }
        else
        {
            writer.WritePointer(true);
        }

        // MappedCount
        writer.WriteUInt32(mappedCount);

        // NTSTATUS
        uint status = mappedCount == totalCount
            ? LsaConstants.StatusSuccess
            : mappedCount > 0
                ? LsaConstants.StatusSomeMapped
                : LsaConstants.StatusNoneMapped;

        writer.WriteUInt32(status);
        return writer.ToArray();
    }

    private static void WritePrimaryDomainInfo(NdrWriter writer, NamingContext domainNc)
    {
        writer.WritePointer(false); // info pointer

        // Switch on info class
        writer.WriteUInt16(LsaConstants.PolicyPrimaryDomainInformation);
        writer.Align(4);

        // LSAPR_POLICY_PRIMARY_DOM_INFO: Name (RPC_UNICODE_STRING), Sid (PRPC_SID pointer)
        string domainName = ExtractNetBiosName(domainNc.Dn);
        writer.WriteRpcUnicodeString(domainName);
        writer.WritePointer(false); // SID pointer

        writer.FlushDeferred();

        // Write SID
        if (!string.IsNullOrEmpty(domainNc.DomainSid))
        {
            writer.WriteRpcSid(domainNc.DomainSid);
        }
        else
        {
            writer.WriteRpcSid("S-1-5-21-0-0-0");
        }
    }

    private static void WriteAccountDomainInfo(NdrWriter writer, NamingContext domainNc)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(LsaConstants.PolicyAccountDomainInformation);
        writer.Align(4);

        string domainName = ExtractNetBiosName(domainNc.Dn);
        writer.WriteRpcUnicodeString(domainName);
        writer.WritePointer(false);

        writer.FlushDeferred();

        if (!string.IsNullOrEmpty(domainNc.DomainSid))
        {
            writer.WriteRpcSid(domainNc.DomainSid);
        }
        else
        {
            writer.WriteRpcSid("S-1-5-21-0-0-0");
        }
    }

    private static void WriteLsaServerRoleInfo(NdrWriter writer)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(LsaConstants.PolicyLsaServerRoleInformation);
        writer.Align(4);

        writer.WriteUInt16(LsaConstants.PolicyServerRolePrimary);
    }

    private static void WriteDnsDomainInfo(NdrWriter writer, NamingContext domainNc)
    {
        writer.WritePointer(false);
        writer.WriteUInt16(LsaConstants.PolicyDnsDomainInformation);
        writer.Align(4);

        string netbiosName = ExtractNetBiosName(domainNc.Dn);
        string dnsName = domainNc.DnsName;

        // LSAPR_POLICY_DNS_DOMAIN_INFO:
        //   Name, DnsDomainName, DnsForestName, DomainGuid, Sid
        writer.WriteRpcUnicodeString(netbiosName);       // Name
        writer.WriteRpcUnicodeString(dnsName);            // DnsDomainName
        writer.WriteRpcUnicodeString(dnsName);            // DnsForestName

        // DomainGuid (16 bytes)
        writer.WriteBytes(Guid.NewGuid().ToByteArray());

        // Sid pointer
        writer.WritePointer(false);

        writer.FlushDeferred();

        if (!string.IsNullOrEmpty(domainNc.DomainSid))
        {
            writer.WriteRpcSid(domainNc.DomainSid);
        }
        else
        {
            writer.WriteRpcSid("S-1-5-21-0-0-0");
        }
    }
}

// LsaSecretHandle is defined in LsaContextHandles.cs
