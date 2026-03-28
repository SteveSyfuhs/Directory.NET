namespace Directory.Core.Models;

public class LinkedAttributeDefinition
{
    public required string ForwardLinkName { get; set; }
    public string BackLinkName { get; set; }
    public required int ForwardLinkId { get; set; }
    public int? BackLinkId { get; set; }

    public bool HasBackLink => BackLinkName != null && BackLinkId.HasValue;
}

public static class WellKnownLinks
{
    public static readonly LinkedAttributeDefinition MemberOf = new()
    {
        ForwardLinkName = "member",
        BackLinkName = "memberOf",
        ForwardLinkId = 2,
        BackLinkId = 3
    };

    public static readonly LinkedAttributeDefinition ManagerDirectReports = new()
    {
        ForwardLinkName = "manager",
        BackLinkName = "directReports",
        ForwardLinkId = 42,
        BackLinkId = 43
    };

    public static readonly LinkedAttributeDefinition MsDsMembersOfResourcePropertyList = new()
    {
        ForwardLinkName = "msDS-MembersOfResourcePropertyList",
        BackLinkName = "msDS-MembersOfResourcePropertyListBL",
        ForwardLinkId = 164,
        BackLinkId = 165
    };

    public static readonly LinkedAttributeDefinition MsDsAllowedToActOnBehalf = new()
    {
        ForwardLinkName = "msDS-AllowedToActOnBehalfOfOtherIdentity",
        BackLinkName = null,
        ForwardLinkId = 166,
        BackLinkId = null
    };

    private static readonly LinkedAttributeDefinition[] All =
    [
        MemberOf,
        ManagerDirectReports,
        MsDsMembersOfResourcePropertyList,
        MsDsAllowedToActOnBehalf
    ];

    public static LinkedAttributeDefinition FindByForwardLink(string name)
        => Array.Find(All, l => string.Equals(l.ForwardLinkName, name, StringComparison.OrdinalIgnoreCase));

    public static LinkedAttributeDefinition FindByBackLink(string name)
        => Array.Find(All, l => l.BackLinkName != null &&
            string.Equals(l.BackLinkName, name, StringComparison.OrdinalIgnoreCase));

    public static LinkedAttributeDefinition FindByLinkId(int linkId)
        => Array.Find(All, l => l.ForwardLinkId == linkId || l.BackLinkId == linkId);
}
