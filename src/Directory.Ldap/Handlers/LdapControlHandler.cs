using Directory.Ldap.Protocol;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Handlers;

public class LdapControlHandler
{
    private readonly ILogger<LdapControlHandler> _logger;

    public LdapControlHandler(ILogger<LdapControlHandler> logger)
    {
        _logger = logger;
    }

    public ControlProcessingResult ProcessRequestControls(IReadOnlyList<LdapControl> controls)
    {
        var result = new ControlProcessingResult();
        if (controls == null || controls.Count == 0) return result;

        foreach (var control in controls)
        {
            switch (control.Oid)
            {
                case LdapConstants.Controls.PagedResults:
                    result.PagedResults = ParsePagedResultsControl(control.Value);
                    break;
                case LdapConstants.Controls.ServerSort:
                    result.SortRequest = ParseSortControl(control.Value);
                    break;
                case LdapConstants.Controls.ShowDeleted:
                    result.ShowDeleted = true;
                    break;
                case LdapConstants.Controls.TreeDelete:
                    result.TreeDelete = true;
                    break;
                case LdapConstants.Controls.DirSync:
                    result.DirSync = ParseDirSyncControl(control.Value);
                    break;
                case LdapConstants.Controls.SdFlags:
                    result.SdFlags = ParseSdFlagsControl(control.Value);
                    break;
                case LdapConstants.Controls.ExtendedDn:
                    result.ExtendedDn = ParseExtendedDnControl(control.Value);
                    break;
                case LdapConstants.Controls.LazyCommit:
                    result.LazyCommit = true;
                    break;
                case LdapConstants.Controls.Notification:
                    result.Notification = true;
                    break;
                case LdapConstants.Controls.Asq:
                    result.AsqAttribute = ParseAsqControl(control.Value);
                    break;
                case LdapConstants.Controls.Vlv:
                    result.VlvRequest = ParseVlvControl(control.Value);
                    break;
                case LdapConstants.Controls.PermissiveModify:
                    result.PermissiveModify = true;
                    break;
                case LdapConstants.Controls.ManageDsaIT:
                    result.ManageDsaIT = true;
                    break;
                case LdapConstants.Controls.PersistentSearch:
                    result.PersistentSearch = ParsePersistentSearchControl(control.Value);
                    break;
                default:
                    if (control.Criticality)
                    {
                        result.UnsupportedCritical = control.Oid;
                        return result;
                    }
                    _logger.LogDebug("Ignoring non-critical control: {Oid}", control.Oid);
                    break;
            }
        }
        return result;
    }

    private static PagedResultsControl ParsePagedResultsControl(byte[] value)
    {
        if (value == null) return null;
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            var pageSize = (int)seq.ReadInteger();
            var cookie = seq.ReadOctetString();
            return new PagedResultsControl { PageSize = pageSize, Cookie = cookie.Length > 0 ? System.Text.Encoding.UTF8.GetString(cookie) : null };
        }
        catch { return null; }
    }

    private static SortRequestControl ParseSortControl(byte[] value)
    {
        if (value == null) return null;
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            var keys = new List<SortKey>();
            while (seq.HasData)
            {
                var keySeq = seq.ReadSequence();
                var attrType = keySeq.ReadOctetString();
                var attrName = System.Text.Encoding.UTF8.GetString(attrType);
                var reverseOrder = false;
                if (keySeq.HasData)
                {
                    // Check for ordering rule or reverse order
                    var tag = keySeq.PeekTag();
                    if (tag.TagValue == 1) // CONTEXT[1] reverseOrder
                    {
                        reverseOrder = keySeq.ReadBoolean(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 1));
                    }
                }
                keys.Add(new SortKey { AttributeName = attrName, ReverseOrder = reverseOrder });
            }
            return new SortRequestControl { SortKeys = keys };
        }
        catch { return null; }
    }

    private static DirSyncControl ParseDirSyncControl(byte[] value)
    {
        if (value == null) return null;
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            var flags = (int)seq.ReadInteger();
            var maxBytes = (int)seq.ReadInteger();
            var cookie = seq.ReadOctetString();
            return new DirSyncControl { Flags = flags, MaxBytes = maxBytes, Cookie = cookie.Length > 0 ? cookie : null };
        }
        catch { return null; }
    }

    private static int ParseSdFlagsControl(byte[] value)
    {
        if (value == null) return 0xF; // All parts
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            return (int)seq.ReadInteger();
        }
        catch { return 0xF; }
    }

    private static ExtendedDnControl ParseExtendedDnControl(byte[] value)
    {
        if (value == null) return new ExtendedDnControl { Option = 0 };
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            var option = (int)seq.ReadInteger();
            return new ExtendedDnControl { Option = option };
        }
        catch { return new ExtendedDnControl { Option = 0 }; }
    }

    private static string ParseAsqControl(byte[] value)
    {
        if (value == null) return null;
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            return System.Text.Encoding.UTF8.GetString(seq.ReadOctetString());
        }
        catch { return null; }
    }

    private static VlvRequestControl ParseVlvControl(byte[] value)
    {
        if (value == null) return null;
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            var beforeCount = (int)seq.ReadInteger();
            var afterCount = (int)seq.ReadInteger();
            // Target can be byOffset or greaterThanOrEqual
            int offset = 0, contentCount = 0;
            byte[] assertionValue = null;
            if (seq.HasData)
            {
                var tag = seq.PeekTag();
                if (tag.TagValue == 0) // byOffset
                {
                    var inner = seq.ReadSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 0, true));
                    offset = (int)inner.ReadInteger();
                    contentCount = (int)inner.ReadInteger();
                }
                else if (tag.TagValue == 1) // greaterThanOrEqual
                {
                    assertionValue = seq.ReadOctetString(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 1));
                }
            }
            return new VlvRequestControl { BeforeCount = beforeCount, AfterCount = afterCount, Offset = offset, ContentCount = contentCount, AssertionValue = assertionValue };
        }
        catch { return null; }
    }

    private static PersistentSearchControl ParsePersistentSearchControl(byte[] value)
    {
        if (value == null) return null;
        try
        {
            var reader = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.BER);
            var seq = reader.ReadSequence();
            var changeTypes = (int)seq.ReadInteger();
            var changesOnly = seq.ReadBoolean();
            var returnECs = seq.ReadBoolean();
            return new PersistentSearchControl
            {
                ChangeTypes = changeTypes,
                ChangesOnly = changesOnly,
                ReturnEntryChangeControls = returnECs,
            };
        }
        catch { return null; }
    }

    // Response control builders

    /// <summary>
    /// Build an Entry Change Notification control value per draft-ietf-ldapext-psearch.
    /// SEQUENCE { changeType ENUMERATED, previousDN LDAPDN OPTIONAL, changeNumber INTEGER OPTIONAL }
    /// </summary>
    public static byte[] BuildEntryChangeNotification(int changeType, string previousDn = null, long? changeNumber = null)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(changeType);
            if (previousDn != null)
            {
                writer.WriteOctetString(System.Text.Encoding.UTF8.GetBytes(previousDn));
            }
            if (changeNumber.HasValue)
            {
                writer.WriteInteger(changeNumber.Value);
            }
        }
        return writer.Encode();
    }

    public static byte[] BuildPagedResultsResponse(int totalEstimate, string continuationToken)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(totalEstimate);
            writer.WriteOctetString(continuationToken != null ? System.Text.Encoding.UTF8.GetBytes(continuationToken) : []);
        }
        return writer.Encode();
    }

    public static byte[] BuildSortResponse(int resultCode)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(resultCode);
        }
        return writer.Encode();
    }

    public static byte[] BuildDirSyncResponse(int flags, int maxBytes, byte[] cookie)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(flags);
            writer.WriteInteger(maxBytes);
            writer.WriteOctetString(cookie ?? []);
        }
        return writer.Encode();
    }

    public static byte[] BuildVlvResponse(int targetPosition, int contentCount, int resultCode)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(targetPosition);
            writer.WriteInteger(contentCount);
            writer.WriteInteger(resultCode);
        }
        return writer.Encode();
    }
}

// Control data structures
public class ControlProcessingResult
{
    public PagedResultsControl PagedResults { get; set; }
    public SortRequestControl SortRequest { get; set; }
    public bool ShowDeleted { get; set; }
    public bool TreeDelete { get; set; }
    public DirSyncControl DirSync { get; set; }
    public int SdFlags { get; set; } = 0xF;
    public ExtendedDnControl ExtendedDn { get; set; }
    public bool LazyCommit { get; set; }
    public bool Notification { get; set; }
    public string AsqAttribute { get; set; }
    public VlvRequestControl VlvRequest { get; set; }
    public bool PermissiveModify { get; set; }
    public bool ManageDsaIT { get; set; }
    public PersistentSearchControl PersistentSearch { get; set; }
    public string UnsupportedCritical { get; set; }
}

public class PagedResultsControl
{
    public int PageSize { get; init; }
    public string Cookie { get; init; }
}

public class SortRequestControl
{
    public List<SortKey> SortKeys { get; init; } = [];
}

public class SortKey
{
    public string AttributeName { get; init; } = "";
    public string MatchingRule { get; init; }
    public bool ReverseOrder { get; init; }
}

public class DirSyncControl
{
    public int Flags { get; init; }
    public int MaxBytes { get; init; }
    public byte[] Cookie { get; init; }
}

public class ExtendedDnControl
{
    public int Option { get; init; } // 0 = hex, 1 = string
}

public class VlvRequestControl
{
    public int BeforeCount { get; init; }
    public int AfterCount { get; init; }
    public int Offset { get; init; }
    public int ContentCount { get; init; }
    public byte[] AssertionValue { get; init; }
}

/// <summary>
/// Persistent Search Control value per draft-ietf-ldapext-psearch.
/// SEQUENCE { changeTypes INTEGER, changesOnly BOOLEAN, returnECs BOOLEAN }
/// changeTypes bitmask: add=1, delete=2, modify=4, modDN=8
/// </summary>
public class PersistentSearchControl
{
    /// <summary>Bitmask of change types to monitor: add=1, delete=2, modify=4, modDN=8.</summary>
    public int ChangeTypes { get; init; }

    /// <summary>If true, only send entries that change after the search begins (no initial results).</summary>
    public bool ChangesOnly { get; init; }

    /// <summary>If true, include the Entry Change Notification control with each result.</summary>
    public bool ReturnEntryChangeControls { get; init; }
}
