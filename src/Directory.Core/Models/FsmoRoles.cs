namespace Directory.Core.Models;

public enum FsmoRoleType
{
    SchemaMaster,
    DomainNamingMaster,
    RidMaster,
    PdcEmulator,
    InfrastructureMaster
}

public class FsmoRoleHolder
{
    public required FsmoRoleType RoleType { get; set; }
    public required string HolderDn { get; set; }
    public required string HolderServerName { get; set; }
}
