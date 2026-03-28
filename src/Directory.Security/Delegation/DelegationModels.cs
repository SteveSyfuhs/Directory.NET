namespace Directory.Security.Delegation;

public class AdminRole
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public List<string> ScopeDns { get; set; } = new();
    public List<string> AssignedMembers { get; set; } = new();
    public bool IsBuiltIn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DelegationPermission
{
    public string Key { get; set; } = "";
    public string Category { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
}

public class EffectivePermissions
{
    public string UserDn { get; set; } = "";
    public List<string> Permissions { get; set; } = new();
    public List<EffectiveRoleSummary> Roles { get; set; } = new();
}

public class EffectiveRoleSummary
{
    public string RoleId { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string AssignedVia { get; set; } = "";
    public List<string> ScopeDns { get; set; } = new();
}
