using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Core.Telemetry;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Directory.Ldap.Handlers;

/// <summary>
/// Handles LDAP Search operations including RootDSE, with full support for:
/// - Server-side sort (1.2.840.113556.1.4.473)
/// - DirSync incremental replication (1.2.840.113556.1.4.841)
/// - Extended DN format (1.2.840.113556.1.4.529)
/// - Range retrieval for multi-valued attributes (;range=N-M)
/// - ASQ — attribute scoped query (1.2.840.113556.1.4.1504)
/// - VLV — virtual list view (2.16.840.1.113730.3.4.9)
/// - Referral generation for cross-NC searches
/// - Constructed attribute computation
/// - ACL-based attribute filtering
/// - Global Catalog attribute filtering
/// </summary>
public class SearchHandler : ILdapOperationHandler
{
    private readonly IDirectoryStore _store;
    private readonly RootDseProvider _rootDse;
    private readonly LdapControlHandler _controlHandler;
    private readonly ISchemaService _schemaService;
    private readonly IConstructedAttributeService _constructedAttributes;
    private readonly IAccessControlService _accessControl;
    private readonly INamingContextService _namingContextService;
    private readonly IOptionsMonitor<LdapServerOptions> _optionsMonitor;
    private readonly ILogger<SearchHandler> _logger;

    /// <summary>
    /// Maximum number of values returned per attribute in a single response entry
    /// before range retrieval kicks in.
    /// </summary>
    private const int MaxValuesPerAttribute = 1500;

    public LdapOperation Operation => LdapOperation.SearchRequest;

    public SearchHandler(
        IDirectoryStore store,
        RootDseProvider rootDse,
        LdapControlHandler controlHandler,
        ISchemaService schemaService,
        IConstructedAttributeService constructedAttributes,
        IAccessControlService accessControl,
        INamingContextService namingContextService,
        IOptionsMonitor<LdapServerOptions> optionsMonitor,
        ILogger<SearchHandler> logger)
    {
        _store = store;
        _rootDse = rootDse;
        _controlHandler = controlHandler;
        _schemaService = schemaService;
        _constructedAttributes = constructedAttributes;
        _accessControl = accessControl;
        _namingContextService = namingContextService;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    private LdapServerOptions _options => _optionsMonitor.CurrentValue;

    public async Task HandleAsync(LdapMessage request, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var searchReq = SearchRequest.Decode(request.ProtocolOpData);

        _logger.LogDebug("Search request: base={Base}, scope={Scope}, filter present",
            searchReq.BaseObject, searchReq.Scope);

        DirectoryMetrics.SearchOperations.Add(1, new KeyValuePair<string, object>("scope", searchReq.Scope.ToString()));

        // RootDSE: base DN = "", scope = base
        if (string.IsNullOrEmpty(searchReq.BaseObject) && searchReq.Scope == SearchScope.BaseObject)
        {
            await HandleRootDseSearchAsync(request.MessageId, searchReq, writer, ct);
            return;
        }

        // Process request controls
        var controls = _controlHandler.ProcessRequestControls(request.Controls);

        // Reject unsupported critical controls
        if (controls.UnsupportedCritical != null)
        {
            var errDone = new SearchResultDone
            {
                ResultCode = LdapResultCode.UnavailableCriticalExtension,
                DiagnosticMessage = $"Critical control not supported: {controls.UnsupportedCritical}",
            };
            await writer.WriteMessageAsync(request.MessageId, errDone.Encode(), ct: ct);
            return;
        }

        // Set up time limit cancellation if the client requested one
        using var timeLimitCts = searchReq.TimeLimit > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(searchReq.TimeLimit))
            : null;
        using var linkedCts = timeLimitCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, timeLimitCts.Token)
            : null;
        var effectiveCt = linkedCts?.Token ?? ct;

        try
        {
            // Determine effective size limit
            var sizeLimit = searchReq.SizeLimit > 0
                ? Math.Min(searchReq.SizeLimit, _options.MaxPageSize)
                : _options.MaxPageSize;

            // Handle ASQ — attribute scoped query
            if (controls.AsqAttribute != null)
            {
                await HandleAsqSearchAsync(request.MessageId, searchReq, controls, state, writer, sizeLimit, effectiveCt);
                return;
            }

            // Handle DirSync — incremental replication
            if (controls.DirSync != null)
            {
                await HandleDirSyncSearchAsync(request.MessageId, searchReq, controls, state, writer, effectiveCt);
                return;
            }

            // Standard search with paging
            var pageSize = controls.PagedResults?.PageSize ?? sizeLimit;
            var continuationToken = controls.PagedResults?.Cookie;

            // Determine if we can use streaming (no sort, no VLV — those require all results in memory)
            bool canStream = controls.SortRequest == null
                && controls.VlvRequest == null
                && controls.PagedResults == null
                && _store is IStreamingDirectoryStore;

            SearchResult result = null;
            List<DirectoryObject> entries = null;
            int sortResultCode = 0;
            int vlvTargetPosition = 0, vlvContentCount = 0;
            bool adminLimitHit = false;
            bool sizeLimitHit = false;

            // Generate referrals for cross-NC search bases
            var referrals = GenerateReferrals(searchReq.BaseObject, searchReq.Scope, state);

            // Send referrals first
            bool timeLimitHit = false;
            foreach (var referral in referrals)
            {
                if (timeLimitCts != null && timeLimitCts.IsCancellationRequested)
                {
                    timeLimitHit = true;
                    break;
                }
                ct.ThrowIfCancellationRequested();
                await writer.WriteMessageAsync(request.MessageId, referral.Encode(), ct: ct);
            }

            int sentCount = 0;

            // Resolve caller info for visibility and attribute filtering
            var searchCallerSid = state.BoundSid ?? string.Empty;
            var searchCallerGroups = state.GroupSids ?? (IReadOnlySet<string>)new HashSet<string>();

            if (canStream && !timeLimitHit)
            {
                // Streaming path: send entries to the client as they arrive from the DB.
                // Memory usage is O(page_size) instead of O(total_results).
                var streamingStore = (IStreamingDirectoryStore)_store;
                var adminLimit = _options.MaxAdminSearchLimit;

                await foreach (var entry in streamingStore.SearchStreamAsync(
                    state.TenantId,
                    searchReq.BaseObject,
                    searchReq.Scope,
                    searchReq.Filter,
                    searchReq.Attributes.Count > 0 ? searchReq.Attributes.ToArray() : null,
                    sizeLimit,
                    searchReq.TimeLimit,
                    includeDeleted: controls.ShowDeleted,
                    ct: effectiveCt))
                {
                    if (timeLimitCts != null && timeLimitCts.IsCancellationRequested)
                    {
                        timeLimitHit = true;
                        break;
                    }
                    ct.ThrowIfCancellationRequested();

                    // Object visibility filtering (MS-ADTS 7.1.3.4)
                    if (!string.IsNullOrEmpty(searchCallerSid)
                        && !await IsObjectVisibleAsync(entry, searchCallerSid, searchCallerGroups, state, effectiveCt))
                        continue;

                    // Enforce admin limit
                    if (sentCount >= adminLimit)
                    {
                        adminLimitHit = true;
                        break;
                    }

                    // Enforce client-requested size limit
                    if (searchReq.SizeLimit > 0 && sentCount >= searchReq.SizeLimit)
                    {
                        sizeLimitHit = true;
                        break;
                    }

                    var resultEntry = await BuildSearchResultEntryAsync(
                        entry, searchReq.Attributes, searchReq.TypesOnly,
                        state, controls, ct);
                    await writer.WriteMessageAsync(request.MessageId, resultEntry.Encode(), ct: ct);
                    sentCount++;
                }
            }
            else if (!timeLimitHit)
            {
                // Non-streaming path: load all results for sort/VLV/paging
                result = await _store.SearchAsync(
                    state.TenantId,
                    searchReq.BaseObject,
                    searchReq.Scope,
                    searchReq.Filter,
                    searchReq.Attributes.Count > 0 ? searchReq.Attributes.ToArray() : null,
                    pageSize,
                    searchReq.TimeLimit,
                    continuationToken: continuationToken,
                    pageSize: pageSize,
                    includeDeleted: controls.ShowDeleted,
                    ct: effectiveCt);

                entries = result.Entries.ToList();

                // Server-side sort
                if (controls.SortRequest != null)
                {
                    entries = ApplyServerSort(entries, controls.SortRequest, out sortResultCode);
                }

                // VLV — apply after sort
                vlvContentCount = entries.Count;
                if (controls.VlvRequest != null)
                {
                    entries = ApplyVlv(entries, controls.VlvRequest, controls.SortRequest,
                        out vlvTargetPosition, out vlvContentCount);
                }

                // Enforce admin limit
                var adminLimit = _options.MaxAdminSearchLimit;
                if (controls.PagedResults == null && entries.Count > adminLimit)
                {
                    entries = entries.Take(adminLimit).ToList();
                    adminLimitHit = true;
                }

                // Enforce client-requested size limit
                if (searchReq.SizeLimit > 0 && entries.Count > searchReq.SizeLimit)
                {
                    entries = entries.Take(searchReq.SizeLimit).ToList();
                    sizeLimitHit = true;
                }

                // Send each entry as a SearchResultEntry (with visibility filtering)
                foreach (var entry in entries)
                {
                    if (timeLimitCts != null && timeLimitCts.IsCancellationRequested)
                    {
                        timeLimitHit = true;
                        break;
                    }
                    ct.ThrowIfCancellationRequested();

                    // Object visibility filtering (MS-ADTS 7.1.3.4)
                    if (!string.IsNullOrEmpty(searchCallerSid)
                        && !await IsObjectVisibleAsync(entry, searchCallerSid, searchCallerGroups, state, effectiveCt))
                        continue;

                    var resultEntry = await BuildSearchResultEntryAsync(
                        entry, searchReq.Attributes, searchReq.TypesOnly,
                        state, controls, ct);
                    await writer.WriteMessageAsync(request.MessageId, resultEntry.Encode(), ct: ct);
                    sentCount++;
                }
            }

            // Build response controls
            var responseControls = BuildResponseControls(controls,
                result ?? new SearchResult(),
                sortResultCode, vlvTargetPosition, vlvContentCount);

            // Determine the result code based on limit violations
            var resultCode = referrals.Count > 0 ? LdapResultCode.Referral : LdapResultCode.Success;

            if (timeLimitHit)
            {
                resultCode = LdapResultCode.TimeLimitExceeded;
            }
            else if (adminLimitHit)
            {
                resultCode = LdapResultCode.AdminLimitExceeded;
            }
            else if (sizeLimitHit)
            {
                resultCode = LdapResultCode.SizeLimitExceeded;
            }

            // Send SearchResultDone
            var done = new SearchResultDone
            {
                ResultCode = resultCode,
            };

            await writer.WriteMessageAsync(request.MessageId, done.Encode(), responseControls, ct);
        }
        catch (OperationCanceledException) when (timeLimitCts != null && timeLimitCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Time limit expired during store search — return what we have
            _logger.LogDebug("Search time limit exceeded for base={Base}", searchReq.BaseObject);
            var done = new SearchResultDone
            {
                ResultCode = LdapResultCode.TimeLimitExceeded,
                DiagnosticMessage = "Time limit exceeded",
            };
            await writer.WriteMessageAsync(request.MessageId, done.Encode(), ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search error for base={Base}", searchReq.BaseObject);

            var done = new SearchResultDone
            {
                ResultCode = LdapResultCode.OperationsError,
                DiagnosticMessage = "Internal error during search",
            };
            await writer.WriteMessageAsync(request.MessageId, done.Encode(), ct: ct);
        }
    }

    #region RootDSE

    private async Task HandleRootDseSearchAsync(int messageId, SearchRequest searchReq, ILdapResponseWriter writer, CancellationToken ct)
    {
        var rootDseEntry = _rootDse.GetRootDse(searchReq.Attributes);
        await writer.WriteMessageAsync(messageId, rootDseEntry.Encode(), ct: ct);

        var done = new SearchResultDone { ResultCode = LdapResultCode.Success };
        await writer.WriteMessageAsync(messageId, done.Encode(), ct: ct);
    }

    #endregion

    #region ASQ (Attribute Scoped Query)

    /// <summary>
    /// ASQ search: resolve the linked attribute on the base object, then search within those targets.
    /// Per MS-ADTS 3.1.1.3.4.1.12.
    /// </summary>
    private async Task HandleAsqSearchAsync(
        int messageId, SearchRequest searchReq, ControlProcessingResult controls,
        LdapConnectionState state, ILdapResponseWriter writer, int sizeLimit, CancellationToken ct)
    {
        // The base object must exist
        var baseObj = await _store.GetByDnAsync(state.TenantId, searchReq.BaseObject, ct);
        if (baseObj == null)
        {
            var done = new SearchResultDone
            {
                ResultCode = LdapResultCode.NoSuchObject,
                DiagnosticMessage = $"Base object not found: {searchReq.BaseObject}",
            };
            await writer.WriteMessageAsync(messageId, done.Encode(), ct: ct);
            return;
        }

        // Read the linked attribute values (DNs) from the base object
        var linkedAttr = baseObj.GetAttribute(controls.AsqAttribute);
        var linkedDns = linkedAttr?.GetStrings().ToList() ?? [];

        int count = 0;
        foreach (var targetDn in linkedDns)
        {
            if (count >= sizeLimit) break;
            ct.ThrowIfCancellationRequested();

            // Retrieve each linked object and apply the filter
            var targetResult = await _store.SearchAsync(
                state.TenantId,
                targetDn,
                SearchScope.BaseObject,
                searchReq.Filter,
                searchReq.Attributes.Count > 0 ? searchReq.Attributes.ToArray() : null,
                1, searchReq.TimeLimit,
                includeDeleted: controls.ShowDeleted,
                ct: ct);

            foreach (var entry in targetResult.Entries)
            {
                var resultEntry = await BuildSearchResultEntryAsync(
                    entry, searchReq.Attributes, searchReq.TypesOnly,
                    state, controls, ct);
                await writer.WriteMessageAsync(messageId, resultEntry.Encode(), ct: ct);
                count++;
            }
        }

        // ASQ response control: result code in the control value
        var asqResponseValue = LdapControlHandler.BuildSortResponse(0); // 0 = success
        var responseControls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.Asq, Value = asqResponseValue }
        };

        var asqDone = new SearchResultDone { ResultCode = LdapResultCode.Success };
        await writer.WriteMessageAsync(messageId, asqDone.Encode(), responseControls, ct);
    }

    #endregion

    #region DirSync (Incremental Replication)

    /// <summary>
    /// DirSync search: returns objects modified since the USN recorded in the cookie.
    /// Per MS-ADTS 3.1.1.3.4.1.3.
    /// </summary>
    private async Task HandleDirSyncSearchAsync(
        int messageId, SearchRequest searchReq, ControlProcessingResult controls,
        LdapConnectionState state, ILdapResponseWriter writer, CancellationToken ct)
    {
        var dirSync = controls.DirSync;

        // Decode USN from cookie (simple format: UTF-8 encoded long)
        long fromUsn = 0;
        if (dirSync.Cookie != null && dirSync.Cookie.Length > 0)
        {
            if (long.TryParse(Encoding.UTF8.GetString(dirSync.Cookie), out var parsedUsn))
                fromUsn = parsedUsn;
        }

        // Search for all objects, then filter by USN > fromUsn
        var result = await _store.SearchAsync(
            state.TenantId,
            searchReq.BaseObject,
            searchReq.Scope,
            searchReq.Filter,
            searchReq.Attributes.Count > 0 ? searchReq.Attributes.ToArray() : null,
            dirSync.MaxBytes > 0 ? Math.Min(dirSync.MaxBytes, _options.MaxPageSize) : _options.MaxPageSize,
            searchReq.TimeLimit,
            includeDeleted: true, // DirSync always includes deleted objects
            ct: ct);

        // Filter entries by USN
        var changedEntries = result.Entries
            .Where(e => e.USNChanged > fromUsn)
            .OrderBy(e => e.USNChanged)
            .ToList();

        long highestUsn = fromUsn;

        foreach (var entry in changedEntries)
        {
            ct.ThrowIfCancellationRequested();

            var resultEntry = await BuildSearchResultEntryAsync(
                entry, searchReq.Attributes, searchReq.TypesOnly,
                state, controls, ct);
            await writer.WriteMessageAsync(messageId, resultEntry.Encode(), ct: ct);

            if (entry.USNChanged > highestUsn)
                highestUsn = entry.USNChanged;
        }

        // Build DirSync response cookie with the highest USN
        var newCookie = Encoding.UTF8.GetBytes(highestUsn.ToString());
        var moreData = changedEntries.Count >= _options.MaxPageSize;
        var dirSyncResponseValue = LdapControlHandler.BuildDirSyncResponse(
            moreData ? 1 : 0, 0, newCookie);

        var responseControls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.DirSync, Value = dirSyncResponseValue }
        };

        var done = new SearchResultDone { ResultCode = LdapResultCode.Success };
        await writer.WriteMessageAsync(messageId, done.Encode(), responseControls, ct);
    }

    #endregion

    #region Server-Side Sort

    private static List<DirectoryObject> ApplyServerSort(
        List<DirectoryObject> entries, SortRequestControl sortRequest, out int resultCode)
    {
        resultCode = 0; // success

        if (sortRequest.SortKeys.Count == 0)
            return entries;

        try
        {
            IOrderedEnumerable<DirectoryObject> ordered = null;

            for (int i = 0; i < sortRequest.SortKeys.Count; i++)
            {
                var key = sortRequest.SortKeys[i];
                var attrName = key.AttributeName;
                var reverse = key.ReverseOrder;

                string GetSortValue(DirectoryObject obj)
                {
                    var attr = obj.GetAttribute(attrName);
                    return attr?.GetFirstString();
                }

                if (i == 0)
                {
                    ordered = reverse
                        ? entries.OrderByDescending(GetSortValue, StringComparer.OrdinalIgnoreCase)
                        : entries.OrderBy(GetSortValue, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    ordered = reverse
                        ? ordered.ThenByDescending(GetSortValue, StringComparer.OrdinalIgnoreCase)
                        : ordered.ThenBy(GetSortValue, StringComparer.OrdinalIgnoreCase);
                }
            }

            return ordered?.ToList() ?? entries;
        }
        catch
        {
            resultCode = 1; // operationsError
            return entries;
        }
    }

    #endregion

    #region VLV (Virtual List View)

    private static List<DirectoryObject> ApplyVlv(
        List<DirectoryObject> entries, VlvRequestControl vlv, SortRequestControl sortRequest,
        out int targetPosition, out int contentCount)
    {
        contentCount = entries.Count;

        if (vlv.AssertionValue != null && vlv.AssertionValue.Length > 0 && sortRequest?.SortKeys.Count > 0)
        {
            // greaterThanOrEqual: find the first entry >= the assertion value
            var assertionStr = Encoding.UTF8.GetString(vlv.AssertionValue);
            var sortAttr = sortRequest.SortKeys[0].AttributeName;

            targetPosition = entries.FindIndex(e =>
            {
                var val = e.GetAttribute(sortAttr)?.GetFirstString() ?? "";
                return string.Compare(val, assertionStr, StringComparison.OrdinalIgnoreCase) >= 0;
            });

            if (targetPosition < 0)
                targetPosition = entries.Count; // past end
        }
        else
        {
            // byOffset: vlv.Offset is 1-based
            targetPosition = Math.Max(0, vlv.Offset - 1);
        }

        // Calculate window: [targetPosition - beforeCount, targetPosition + afterCount]
        var windowStart = Math.Max(0, targetPosition - vlv.BeforeCount);
        var windowEnd = Math.Min(entries.Count - 1, targetPosition + vlv.AfterCount);

        if (windowStart >= entries.Count)
            return [];

        var windowCount = windowEnd - windowStart + 1;
        if (windowCount <= 0)
            return [];

        // Adjust targetPosition to be 1-based for the response
        targetPosition = targetPosition + 1; // 1-based in VLV response

        return entries.GetRange(windowStart, windowCount);
    }

    #endregion

    #region Referral Generation

    /// <summary>
    /// Generate referrals when searching across naming context boundaries.
    /// Per RFC 4511 section 4.5.3 and MS-ADTS 3.1.1.3.1.2.
    /// </summary>
    private List<SearchResultReference> GenerateReferrals(string baseDn, SearchScope scope, LdapConnectionState state)
    {
        var referrals = new List<SearchResultReference>();

        if (scope == SearchScope.BaseObject)
            return referrals; // No referrals for base-level searches

        // Check if the search base is the root of a naming context
        var currentNc = _namingContextService.GetNamingContext(baseDn);
        if (currentNc == null)
            return referrals;

        // For subtree or one-level searches, check if there are subordinate naming contexts
        var allNcs = _namingContextService.GetAllNamingContexts();
        foreach (var nc in allNcs)
        {
            // Skip the current NC
            if (string.Equals(nc.Dn, currentNc.Dn, StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if this NC is subordinate to the search base
            if (nc.Dn.EndsWith(baseDn, StringComparison.OrdinalIgnoreCase) &&
                nc.Dn.Length > baseDn.Length)
            {
                // For one-level scope, only include immediate children
                if (scope == SearchScope.SingleLevel)
                {
                    var parentDn = GetParentDn(nc.Dn);
                    if (!string.Equals(parentDn, baseDn, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var referralUri = $"ldap://{nc.DnsName}/{nc.Dn}";
                referrals.Add(new SearchResultReference
                {
                    Uris = [referralUri]
                });
            }
        }

        return referrals;
    }

    private static string GetParentDn(string dn)
    {
        var firstComma = dn.IndexOf(',');
        return firstComma >= 0 ? dn[(firstComma + 1)..] : string.Empty;
    }

    #endregion

    #region Entry Building

    private async Task<SearchResultEntry> BuildSearchResultEntryAsync(
        DirectoryObject obj, List<string> requestedAttrs, bool typesOnly,
        LdapConnectionState state, ControlProcessingResult controls, CancellationToken ct)
    {
        var entry = new SearchResultEntry();

        // Cache for DN lookups during extended DN resolution to avoid repeated store queries
        var dnCache = new Dictionary<string, DirectoryObject>(StringComparer.OrdinalIgnoreCase);

        // Apply extended DN format if requested
        entry.ObjectName = FormatDn(obj, controls.ExtendedDn);

        // Determine which attributes to return
        var attrsToReturn = requestedAttrs.Count > 0
            ? new List<string>(requestedAttrs)
            : GetDefaultAttributes();

        bool returnAllUser = attrsToReturn.Contains("*");
        bool returnAllOperational = attrsToReturn.Contains("+");

        if (returnAllUser)
            attrsToReturn = GetAllAttributes(obj);

        // When ShowDeleted control is active and the object is a tombstone,
        // ensure isDeleted and lastKnownParent are included in returned attributes
        if (controls.ShowDeleted && obj.IsDeleted)
        {
            if (!attrsToReturn.Contains("isDeleted", StringComparer.OrdinalIgnoreCase))
                attrsToReturn.Add("isDeleted");
            if (!attrsToReturn.Contains("lastKnownParent", StringComparer.OrdinalIgnoreCase))
                attrsToReturn.Add("lastKnownParent");
        }

        // Parse range retrieval requests from attribute names
        var rangeRequests = new Dictionary<string, (int Start, int End)?>(StringComparer.OrdinalIgnoreCase);
        var cleanedAttrs = new List<string>();
        foreach (var attrSpec in attrsToReturn)
        {
            var parsed = ParseRangeRequest(attrSpec);
            if (parsed.HasRange)
            {
                rangeRequests[parsed.AttributeName] = (parsed.RangeStart, parsed.RangeEnd);
                cleanedAttrs.Add(parsed.AttributeName);
            }
            else
            {
                cleanedAttrs.Add(attrSpec);
            }
        }

        foreach (var attrName in cleanedAttrs)
        {
            if (attrName == "*" || attrName == "+" || attrName == "1.1") continue;

            // Check if this is a constructed attribute
            if (_constructedAttributes.IsConstructedAttribute(attrName))
            {
                if (!returnAllOperational && !requestedAttrs.Contains(attrName, StringComparer.OrdinalIgnoreCase)
                    && !requestedAttrs.Contains("+"))
                    continue; // Constructed attributes must be explicitly requested (or via "+")

                var computed = await _constructedAttributes.ComputeAttributeAsync(attrName, obj, state.TenantId, ct);
                if (computed != null && computed.Values.Count > 0)
                {
                    await AddAttributeToEntryAsync(entry, attrName, computed, typesOnly, null, controls.ExtendedDn, obj, state.TenantId, dnCache, ct);
                }
                continue;
            }

            // ACL-based attribute filtering with group membership and object-specific ACEs
            if (state.BoundSid != null)
            {
                var callerGroups = state.GroupSids ?? (IReadOnlySet<string>)new HashSet<string>();
                if (!_accessControl.CheckAttributeAccess(state.BoundSid, callerGroups, obj, attrName, isWrite: false, _schemaService))
                {
                    _logger.LogTrace("ACL denied read access to {Attr} on {DN} for {Sid}", attrName, obj.DistinguishedName, state.BoundSid);
                    continue;
                }
            }

            var attr = obj.GetAttribute(attrName);
            if (attr is null || attr.Values.Count == 0) continue;

            // Range retrieval
            (int Start, int End)? range = rangeRequests.GetValueOrDefault(attrName);

            // Auto-range if too many values and no explicit range was requested
            if (range == null && attr.Values.Count > MaxValuesPerAttribute)
            {
                range = (0, MaxValuesPerAttribute - 1);
            }

            await AddAttributeToEntryAsync(entry, attrName, attr, typesOnly, range, controls.ExtendedDn, obj, state.TenantId, dnCache, ct);
        }

        // If "+" was requested, add constructed/operational attributes
        if (returnAllOperational)
        {
            foreach (var constructedName in _constructedAttributes.GetConstructedAttributeNames())
            {
                // Skip if already added
                if (entry.Attributes.Any(a => string.Equals(a.Name, constructedName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var computed = await _constructedAttributes.ComputeAttributeAsync(constructedName, obj, state.TenantId, ct);
                if (computed != null && computed.Values.Count > 0)
                {
                    await AddAttributeToEntryAsync(entry, constructedName, computed, typesOnly, null, controls.ExtendedDn, obj, state.TenantId, dnCache, ct);
                }
            }
        }

        return entry;
    }

    /// <summary>
    /// Attributes that must be returned as binary OCTET STRING values in LDAP responses.
    /// The stored string values are converted to their binary wire format.
    /// </summary>
    private static readonly HashSet<string> BinaryLdapAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "objectSid",
        "objectGUID",
        "tokenGroups",
        "tokenGroupsGlobalAndUniversal",
        "tokenGroupsNoGCAcceptable",
    };

    private async Task AddAttributeToEntryAsync(
        SearchResultEntry entry, string attrName, DirectoryAttribute attr,
        bool typesOnly, (int Start, int End)? range, ExtendedDnControl extDn,
        DirectoryObject obj, string tenantId, Dictionary<string, DirectoryObject> dnCache,
        CancellationToken ct)
    {
        if (typesOnly)
        {
            var name = range != null
                ? $"{attrName};range={range.Value.Start}-{range.Value.End}"
                : attrName;
            entry.Attributes.Add((name, []));
            return;
        }

        var values = attr.Values;

        // Apply range retrieval
        if (range != null)
        {
            var start = Math.Max(0, range.Value.Start);
            var end = Math.Min(values.Count - 1, range.Value.End);
            if (end < 0 || start > values.Count - 1)
            {
                // Empty range
                entry.Attributes.Add(($"{attrName};range={start}-*", []));
                return;
            }

            var rangedValues = values.Skip(start).Take(end - start + 1)
                .Select(v => MarshalAttributeValue(attrName, v)).ToList();

            // If we returned all remaining values, use "*" as end marker
            var endMarker = end >= values.Count - 1 ? "*" : end.ToString();
            entry.Attributes.Add(($"{attrName};range={start}-{endMarker}", rangedValues));
            return;
        }

        // Marshal values — binary attributes get converted from string to byte[],
        // DN-syntax attributes may get extended DN formatting.
        // Values that are already byte[] (e.g., nTSecurityDescriptor) are passed through.
        var marshalledValues = new List<object>(values.Count);
        foreach (var v in values)
        {
            // Binary attributes: convert stored string to binary wire format
            if (BinaryLdapAttributes.Contains(attrName))
            {
                marshalledValues.Add(MarshalAttributeValue(attrName, v));
                continue;
            }

            // Values already in byte[] form (e.g., nTSecurityDescriptor) pass through as-is
            if (v is byte[])
            {
                marshalledValues.Add(v);
                continue;
            }

            var strVal = v?.ToString() ?? string.Empty;

            // Extended DN: for DN-syntax attributes, prepend GUID and SID
            if (extDn != null && IsDnValue(strVal))
            {
                marshalledValues.Add(await FormatExtendedDnValueAsync(strVal, extDn, tenantId, dnCache, ct));
                continue;
            }

            marshalledValues.Add(strVal);
        }

        entry.Attributes.Add((attrName, marshalledValues));
    }

    /// <summary>
    /// Convert a single attribute value to its LDAP wire representation.
    /// For binary attributes (objectSid, objectGUID, tokenGroups), string values
    /// are converted to their proper binary encoding. Values that are already byte[]
    /// are passed through unchanged.
    /// </summary>
    private static object MarshalAttributeValue(string attrName, object value)
    {
        if (value is byte[] bytes)
            return bytes;

        var strVal = value?.ToString() ?? string.Empty;
        var attrLower = attrName.ToLowerInvariant();

        switch (attrLower)
        {
            case "objectsid":
            case "tokengroups":
            case "tokengroupsglobalanduniversal":
            case "tokengroupsnogcacceptable":
                if (SidUtils.IsStringSid(strVal))
                    return SidUtils.StringSidToBytes(strVal);
                break;

            case "objectguid":
                if (SidUtils.IsStringGuid(strVal))
                    return SidUtils.GuidToLdapBytes(strVal);
                break;
        }

        // Fallback: return as string
        return strVal;
    }

    /// <summary>
    /// Format a DN with extended DN information (GUID and optionally SID).
    /// Option 0 = hex format: &lt;GUID=hex&gt;&lt;SID=hex&gt;dn
    /// Option 1 = string format: &lt;GUID=guid-string&gt;&lt;SID=sid-string&gt;dn
    /// </summary>
    private static string FormatDn(DirectoryObject obj, ExtendedDnControl extDn)
    {
        if (extDn == null)
            return obj.DistinguishedName;

        var sb = new StringBuilder();
        sb.Append("<GUID=");

        if (extDn.Option == 0)
        {
            // Hex format
            if (Guid.TryParse(obj.ObjectGuid, out var guid))
                sb.Append(Convert.ToHexString(guid.ToByteArray()));
            else
                sb.Append(obj.ObjectGuid);
        }
        else
        {
            // String format
            sb.Append(obj.ObjectGuid);
        }

        sb.Append('>');

        if (obj.ObjectSid != null)
        {
            sb.Append("<SID=");
            sb.Append(obj.ObjectSid);
            sb.Append('>');
        }

        sb.Append(obj.DistinguishedName);
        return sb.ToString();
    }

    /// <summary>
    /// Resolves a DN-valued attribute to include GUID and SID in extended DN format.
    /// Looks up the referenced object from the store (with per-search caching) and
    /// formats as: &lt;GUID=hex&gt;&lt;SID=sid&gt;DN (format 0) or &lt;GUID={guid}&gt;&lt;SID=sid&gt;DN (format 1).
    /// Returns the DN as-is if the referenced object cannot be found (graceful degradation).
    /// </summary>
    private async Task<string> FormatExtendedDnValueAsync(
        string dnValue, ExtendedDnControl extDn, string tenantId,
        Dictionary<string, DirectoryObject> dnCache, CancellationToken ct)
    {
        // Lookup the referenced object, using the per-search cache to avoid repeated queries
        if (!dnCache.TryGetValue(dnValue, out var refObj))
        {
            try
            {
                refObj = await _store.GetByDnAsync(tenantId, dnValue, ct);
            }
            catch
            {
                // If the lookup fails, return DN as-is
                refObj = null;
            }
            dnCache[dnValue] = refObj;
        }

        if (refObj == null)
            return dnValue;

        return FormatDn(refObj, extDn);
    }

    private static bool IsDnValue(string value)
    {
        // Simple heuristic: DN values typically contain "CN=", "OU=", "DC=" components
        return value.Contains('=') && value.Contains(',') &&
               (value.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("DC=", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Range Retrieval Parsing

    private static (string AttributeName, bool HasRange, int RangeStart, int RangeEnd) ParseRangeRequest(string attrSpec)
    {
        // Format: attributeName;range=N-M  or  attributeName;range=N-*
        var semicolonIdx = attrSpec.IndexOf(";range=", StringComparison.OrdinalIgnoreCase);
        if (semicolonIdx < 0)
            return (attrSpec, false, 0, 0);

        var attrName = attrSpec[..semicolonIdx];
        var rangeSpec = attrSpec[(semicolonIdx + 7)..]; // skip ";range="

        var dashIdx = rangeSpec.IndexOf('-');
        if (dashIdx < 0)
            return (attrName, false, 0, 0);

        if (!int.TryParse(rangeSpec[..dashIdx], out var start))
            return (attrName, false, 0, 0);

        var endStr = rangeSpec[(dashIdx + 1)..];
        var end = endStr == "*" ? int.MaxValue : int.TryParse(endStr, out var e) ? e : int.MaxValue;

        return (attrName, true, start, end);
    }

    #endregion

    #region Response Controls

    private static List<LdapControl> BuildResponseControls(
        ControlProcessingResult controls, SearchResult result,
        int sortResultCode, int vlvTargetPosition, int vlvContentCount)
    {
        var responseControls = new List<LdapControl>();

        // Paged results response
        if (controls.PagedResults != null)
        {
            responseControls.Add(new LdapControl
            {
                Oid = LdapConstants.Controls.PagedResults,
                Value = LdapControlHandler.BuildPagedResultsResponse(
                    result.TotalEstimate, result.ContinuationToken),
            });
        }

        // Sort response
        if (controls.SortRequest != null)
        {
            responseControls.Add(new LdapControl
            {
                Oid = LdapConstants.Controls.ServerSortResponse,
                Value = LdapControlHandler.BuildSortResponse(sortResultCode),
            });
        }

        // VLV response
        if (controls.VlvRequest != null)
        {
            responseControls.Add(new LdapControl
            {
                Oid = LdapConstants.Controls.VlvResponse,
                Value = LdapControlHandler.BuildVlvResponse(vlvTargetPosition, vlvContentCount, 0),
            });
        }

        return responseControls.Count > 0 ? responseControls : null;
    }

    #endregion

    #region Default / All Attributes

    private static List<string> GetDefaultAttributes() =>
    [
        "distinguishedName", "objectGUID", "objectClass", "objectCategory",
        "cn", "name", "displayName", "sAMAccountName", "userPrincipalName",
        "memberOf", "member", "description", "mail",
        "whenCreated", "whenChanged", "userAccountControl",
    ];

    private static List<string> GetAllAttributes(DirectoryObject obj)
    {
        var attrs = new List<string>
        {
            "distinguishedName", "objectGUID", "objectClass", "objectCategory",
            "cn", "displayName", "sAMAccountName", "userPrincipalName",
            "memberOf", "member", "description", "mail",
            "whenCreated", "whenChanged", "userAccountControl",
            "objectSid", "primaryGroupId", "servicePrincipalName",
            "uSNCreated", "uSNChanged",
        };

        // Add all attributes from the dictionary
        foreach (var key in obj.Attributes.Keys)
        {
            if (!attrs.Contains(key, StringComparer.OrdinalIgnoreCase))
                attrs.Add(key);
        }

        return attrs;
    }

    #endregion

    #region Object Visibility Filtering (MS-ADTS 7.1.3.4)

    /// <summary>
    /// Check if a search result entry is visible to the caller.
    /// Per MS-ADTS 7.1.3.4, an object is visible if the caller has LIST_CONTENTS on its parent.
    /// Admin/SYSTEM callers skip this check for performance.
    /// </summary>
    private async Task<bool> IsObjectVisibleAsync(
        DirectoryObject entry, string callerSid, IReadOnlySet<string> callerGroups,
        LdapConnectionState state, CancellationToken ct)
    {
        // Fast path: admin/SYSTEM skip visibility checks
        if (_accessControl.IsAdminOrSystem(callerSid, callerGroups))
            return true;

        // Check LIST_CONTENTS on the parent container
        var parentDn = entry.ParentDn;
        if (string.IsNullOrEmpty(parentDn))
        {
            // Root objects (naming contexts) — try to compute parent from DN
            try
            {
                var parsed = DistinguishedName.Parse(entry.DistinguishedName);
                parentDn = parsed.Parent().ToString();
            }
            catch
            {
                return true; // Can't determine parent — allow visibility
            }
        }

        if (string.IsNullOrEmpty(parentDn))
            return true; // Top-level object, always visible

        var parentObj = await _store.GetByDnAsync(state.TenantId, parentDn, ct);
        if (parentObj is null)
            return true; // Parent doesn't exist (shouldn't happen), allow

        return _accessControl.CheckAccess(callerSid, callerGroups, parentObj, AccessMask.ListContents);
    }

    #endregion
}
