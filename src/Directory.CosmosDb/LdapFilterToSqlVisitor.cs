using System.Text;
using Directory.Core.Interfaces;
using Directory.Core.Models;

namespace Directory.CosmosDb;

/// <summary>
/// Translates an LDAP filter AST into a Cosmos DB SQL WHERE clause.
/// Handles equality, substring, presence, comparison, and boolean operators.
/// </summary>
public class LdapFilterToSqlVisitor : IFilterVisitor<string>
{
    private readonly List<(string Name, object Value)> _parameters = [];
    private int _paramIndex;

    public IReadOnlyList<(string Name, object Value)> Parameters => _parameters;

    /// <summary>
    /// Map LDAP attribute names to Cosmos DB document property paths.
    /// Well-known attributes are top-level properties; others are in the attributes dict.
    /// </summary>
    private static string MapAttributePath(string ldapAttr)
    {
        return ldapAttr.ToLowerInvariant() switch
        {
            "distinguishedname" => "c.distinguishedName",
            "objectguid" => "c.objectGuid",
            "objectsid" => "c.objectSid",
            "samaccountname" => "c.samAccountName",
            "userprincipalname" => "c.userPrincipalName",
            "cn" => "c.cn",
            "displayname" => "c.displayName",
            "description" => "c.description",
            "objectclass" => "c.objectClass",
            "objectcategory" => "c.objectCategory",
            "memberof" => "c.memberOf",
            "member" => "c.member",
            "serviceprincipalname" => "c.servicePrincipalName",
            "useraccountcontrol" => "c.userAccountControl",
            "mail" => "c.mail",
            "whencreated" => "c.whenCreated",
            "whenchanged" => "c.whenChanged",
            "usnchanged" => "c.usnChanged",
            "usncreated" => "c.usnCreated",
            "isdeleted" => "c.isDeleted",
            "parentdn" => "c.parentDn",
            "primarygroupid" => "c.primaryGroupId",
            "grouptype" => "c.groupType",
            "pwdlastset" => "c.pwdLastSet",
            "lastlogon" => "c.lastLogon",
            "badpwdcount" => "c.badPwdCount",
            "dnshostname" => "c.attributes.dNSHostName",
            "givenname" => "c.attributes.givenName",
            "sn" => "c.attributes.sn",
            "title" => "c.attributes.title",
            "department" => "c.attributes.department",
            _ => $"c.attributes.{ldapAttr}",
        };
    }

    private string AddParameter(object value)
    {
        var name = $"@p{_paramIndex++}";
        _parameters.Add((name, value));
        return name;
    }

    public string VisitAnd(AndFilterNode node)
    {
        if (node.Children.Count == 0) return "true";
        if (node.Children.Count == 1) return node.Children[0].Accept(this);

        var parts = node.Children.Select(c => c.Accept(this));
        return $"({string.Join(" AND ", parts)})";
    }

    public string VisitOr(OrFilterNode node)
    {
        if (node.Children.Count == 0) return "false";
        if (node.Children.Count == 1) return node.Children[0].Accept(this);

        var parts = node.Children.Select(c => c.Accept(this));
        return $"({string.Join(" OR ", parts)})";
    }

    public string VisitNot(NotFilterNode node)
    {
        return $"NOT ({node.Child.Accept(this)})";
    }

    public string VisitEquality(EqualityFilterNode node)
    {
        var path = MapAttributePath(node.Attribute);

        // For objectSid and objectGUID, the filter value may be binary-escaped.
        // Decode it to the stored string format for Cosmos DB comparison.
        var filterValue = NormalizeBinaryFilterValue(node.Attribute, node.Value);
        var param = AddParameter(filterValue);
        var useLower = !IsBinaryAttribute(node.Attribute);

        // For array-type attributes (objectClass, memberOf, member, servicePrincipalName),
        // use EXISTS with LOWER() for case-insensitive matching
        if (IsArrayAttribute(node.Attribute))
        {
            if (useLower)
                return $"EXISTS(SELECT VALUE v FROM v IN {path} WHERE LOWER(v) = LOWER({param}))";
            return $"ARRAY_CONTAINS({path}, {param})";
        }

        // For attributes stored in the Attributes dictionary, check the Values array
        if (path.StartsWith("c.attributes."))
        {
            if (useLower)
                return $"EXISTS(SELECT VALUE v FROM v IN {path}.Values WHERE LOWER(v) = LOWER({param}))";
            return $"ARRAY_CONTAINS({path}.Values, {param})";
        }

        if (useLower)
            return $"LOWER({path}) = LOWER({param})";
        return $"{path} = {param}";
    }

    public string VisitSubstring(SubstringFilterNode node)
    {
        var path = MapAttributePath(node.Attribute);

        // For attributes in the dictionary, we need to check Values[0]
        var targetPath = path.StartsWith("c.attributes.")
            ? $"{path}.Values[0]"
            : path;

        // Build CONTAINS/STARTSWITH/ENDSWITH depending on components
        var conditions = new List<string>();

        if (node.Initial is not null)
        {
            var param = AddParameter(node.Initial);
            conditions.Add($"STARTSWITH({targetPath}, {param}, true)");
        }

        foreach (var any in node.Any)
        {
            var param = AddParameter(any);
            conditions.Add($"CONTAINS({targetPath}, {param}, true)");
        }

        if (node.Final is not null)
        {
            var param = AddParameter(node.Final);
            conditions.Add($"ENDSWITH({targetPath}, {param}, true)");
        }

        return conditions.Count switch
        {
            0 => $"IS_DEFINED({path})", // Just "*" = presence test
            1 => conditions[0],
            _ => $"({string.Join(" AND ", conditions)})",
        };
    }

    public string VisitGreaterOrEqual(GreaterOrEqualFilterNode node)
    {
        var path = MapAttributePath(node.Attribute);
        var param = AddParameter(node.Value);

        if (!IsBinaryAttribute(node.Attribute))
            return $"LOWER({path}) >= LOWER({param})";
        return $"{path} >= {param}";
    }

    public string VisitLessOrEqual(LessOrEqualFilterNode node)
    {
        var path = MapAttributePath(node.Attribute);
        var param = AddParameter(node.Value);

        if (!IsBinaryAttribute(node.Attribute))
            return $"LOWER({path}) <= LOWER({param})";
        return $"{path} <= {param}";
    }

    public string VisitPresence(PresenceFilterNode node)
    {
        // Special case: objectClass=* matches everything (it's always present)
        if (node.Attribute.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
            return "true";

        var path = MapAttributePath(node.Attribute);

        if (path.StartsWith("c.attributes."))
        {
            return $"IS_DEFINED({path})";
        }

        return $"IS_DEFINED({path}) AND {path} != null";
    }

    public string VisitApproxMatch(ApproxMatchFilterNode node)
    {
        // Approximate match — treat as case-insensitive equality
        return VisitEquality(new EqualityFilterNode(node.Attribute, node.Value));
    }

    public string VisitExtensibleMatch(ExtensibleMatchFilterNode node)
    {
        // Basic extensible match support — treat as equality on the attribute
        if (node.Attribute is not null)
        {
            return VisitEquality(new EqualityFilterNode(node.Attribute, node.Value));
        }

        // If no attribute specified, this is a complex case; return true and filter in memory
        return "true";
    }

    /// <summary>
    /// Generate the full SQL WHERE clause for a filter.
    /// </summary>
    public static (string WhereClause, IReadOnlyList<(string Name, object Value)> Parameters) GenerateSql(FilterNode filter)
    {
        var visitor = new LdapFilterToSqlVisitor();
        var where = filter.Accept(visitor);
        return (where, visitor.Parameters);
    }

    private static bool IsArrayAttribute(string attr)
    {
        return attr.ToLowerInvariant() switch
        {
            "objectclass" or "memberof" or "member" or "serviceprincipalname" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Identifies binary/GUID/SID attributes that should NOT have LOWER() applied
    /// because their values are not case-sensitive strings (they are binary-encoded
    /// or have fixed canonical representations).
    /// </summary>
    private static bool IsBinaryAttribute(string attr)
    {
        return attr.ToLowerInvariant() switch
        {
            "objectguid" or "objectsid" or "ntsecuritydescriptor"
                or "usercertificate" or "cacertificate" or "logonhours"
                or "sidhistory" or "msds-generationid" => true,
            _ => false,
        };
    }

    /// <summary>
    /// For objectSid and objectGUID filters, LDAP clients may send binary-escaped values
    /// (e.g., "\01\05\00\00..."). Since these attributes are stored as strings in Cosmos DB,
    /// decode the binary to the corresponding string format for comparison.
    /// </summary>
    private static string NormalizeBinaryFilterValue(string attribute, string value)
    {
        var attrLower = attribute.ToLowerInvariant();

        if (attrLower == "objectsid")
        {
            var decoded = SidUtils.TryDecodeFilterSid(value);
            if (decoded != null)
                return decoded;
        }
        else if (attrLower == "objectguid")
        {
            var decoded = SidUtils.TryDecodeFilterGuid(value);
            if (decoded != null)
                return decoded;
        }

        return value;
    }
}
