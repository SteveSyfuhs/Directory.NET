using System.Buffers.Binary;

namespace Directory.Web.Services;

/// <summary>
/// Parses Windows NT Security Descriptors (binary format) into displayable models.
/// Implements parsing per MS-DTYP 2.4.6 (SECURITY_DESCRIPTOR) and MS-ADTS 5.1.3.2.
/// </summary>
public class SecurityDescriptorParser
{
    /// <summary>
    /// Parse raw nTSecurityDescriptor bytes into a displayable model.
    /// </summary>
    public SecurityDescriptorInfo Parse(byte[] descriptor)
    {
        if (descriptor == null || descriptor.Length < 20)
            throw new ArgumentException("Security descriptor is too short or null.");

        var info = new SecurityDescriptorInfo();

        // SECURITY_DESCRIPTOR layout (self-relative):
        // Offset 0: Revision (1 byte)
        // Offset 1: Sbz1 (1 byte)
        // Offset 2-3: Control (2 bytes, little-endian)
        // Offset 4-7: OffsetOwner (4 bytes, LE)
        // Offset 8-11: OffsetGroup (4 bytes, LE)
        // Offset 12-15: OffsetSacl (4 bytes, LE)
        // Offset 16-19: OffsetDacl (4 bytes, LE)

        var control = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(2, 2));
        info.Control = (SecurityDescriptorControl)control;

        var offsetOwner = BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(4, 4));
        var offsetGroup = BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(8, 4));
        var offsetSacl = BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(12, 4));
        var offsetDacl = BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(16, 4));

        if (offsetOwner > 0 && offsetOwner < descriptor.Length)
        {
            info.Owner = ReadSid(descriptor, offsetOwner);
            info.OwnerName = SidFormatter.TryResolveWellKnown(info.Owner);
        }

        if (offsetGroup > 0 && offsetGroup < descriptor.Length)
        {
            info.Group = ReadSid(descriptor, offsetGroup);
            info.GroupName = SidFormatter.TryResolveWellKnown(info.Group);
        }

        if (offsetDacl > 0 && offsetDacl < descriptor.Length)
        {
            info.Dacl = ParseAcl(descriptor, offsetDacl);
        }

        if (offsetSacl > 0 && offsetSacl < descriptor.Length)
        {
            info.Sacl = ParseAcl(descriptor, offsetSacl);
        }

        return info;
    }

    private static string ReadSid(byte[] data, int offset)
    {
        if (offset + 8 > data.Length)
            return "Invalid SID";

        byte revision = data[offset];
        byte subAuthorityCount = data[offset + 1];

        int sidLength = 8 + subAuthorityCount * 4;
        if (offset + sidLength > data.Length)
            return "Invalid SID";

        var sidBytes = data[offset..(offset + sidLength)];
        return SidFormatter.Format(sidBytes);
    }

    private static List<AceInfo> ParseAcl(byte[] data, int offset)
    {
        var aces = new List<AceInfo>();

        if (offset + 8 > data.Length)
            return aces;

        // ACL header: Revision (1), Sbz1 (1), AclSize (2), AceCount (2), Sbz2 (2)
        var aceCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4, 2));
        var pos = offset + 8; // skip ACL header

        for (int i = 0; i < aceCount && pos + 4 <= data.Length; i++)
        {
            var ace = ParseAce(data, pos);
            if (ace != null)
                aces.Add(ace);

            // ACE size is at offset+2 within the ACE header
            var aceSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2, 2));
            if (aceSize < 4) break; // safety
            pos += aceSize;
        }

        return aces;
    }

    private static AceInfo ParseAce(byte[] data, int offset)
    {
        if (offset + 4 > data.Length)
            return null;

        byte aceType = data[offset];
        byte aceFlags = data[offset + 1];
        var aceSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2, 2));

        if (offset + aceSize > data.Length)
            return null;

        var ace = new AceInfo
        {
            Type = GetAceTypeName(aceType),
            IsInherited = (aceFlags & 0x10) != 0, // INHERITED_ACE
            Flags = DecodeAceFlags(aceFlags)
        };

        // Standard ACE layout: Header(4) + Mask(4) + SID
        // Object ACE layout: Header(4) + Mask(4) + Flags(4) + [ObjectType GUID(16)] + [InheritedObjectType GUID(16)] + SID

        if (offset + 8 > data.Length)
            return ace;

        var accessMask = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
        ace.AccessMask = $"0x{accessMask:X8}";
        ace.Permissions = DecodeAccessMask(accessMask);

        int sidOffset;

        // Object ACE types: 0x05 (ACCESS_ALLOWED_OBJECT), 0x06 (ACCESS_DENIED_OBJECT),
        // 0x07 (SYSTEM_AUDIT_OBJECT), 0x08 (SYSTEM_ALARM_OBJECT),
        // 0x0B (ACCESS_ALLOWED_CALLBACK_OBJECT), 0x0C (ACCESS_DENIED_CALLBACK_OBJECT)
        if (IsObjectAce(aceType))
        {
            if (offset + 12 > data.Length)
                return ace;

            var objectFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 8, 4));
            sidOffset = offset + 12;

            if ((objectFlags & 0x01) != 0) // ACE_OBJECT_TYPE_PRESENT
            {
                if (sidOffset + 16 <= data.Length)
                {
                    ace.ObjectType = new Guid(data.AsSpan(sidOffset, 16)).ToString();
                    ace.ObjectTypeName = ResolveSchemaGuid(ace.ObjectType);
                    sidOffset += 16;
                }
            }

            if ((objectFlags & 0x02) != 0) // ACE_INHERITED_OBJECT_TYPE_PRESENT
            {
                if (sidOffset + 16 <= data.Length)
                {
                    ace.InheritedObjectType = new Guid(data.AsSpan(sidOffset, 16)).ToString();
                    ace.InheritedObjectTypeName = ResolveSchemaGuid(ace.InheritedObjectType);
                    sidOffset += 16;
                }
            }
        }
        else
        {
            sidOffset = offset + 8;
        }

        if (sidOffset < data.Length && sidOffset + 8 <= data.Length)
        {
            ace.Principal = ReadSid(data, sidOffset);
            ace.PrincipalName = SidFormatter.TryResolveWellKnown(ace.Principal);
        }

        return ace;
    }

    private static bool IsObjectAce(byte aceType)
    {
        return aceType is 0x05 or 0x06 or 0x07 or 0x08 or 0x0B or 0x0C;
    }

    private static string GetAceTypeName(byte aceType) => aceType switch
    {
        0x00 => "Allow",
        0x01 => "Deny",
        0x02 => "Audit",
        0x03 => "Alarm",
        0x04 => "Allow (Compound)",
        0x05 => "Allow (Object)",
        0x06 => "Deny (Object)",
        0x07 => "Audit (Object)",
        0x08 => "Alarm (Object)",
        0x09 => "Allow (Callback)",
        0x0A => "Deny (Callback)",
        0x0B => "Allow (Callback Object)",
        0x0C => "Deny (Callback Object)",
        0x0D => "Audit (Callback)",
        0x0E => "Alarm (Callback)",
        0x0F => "Audit (Callback Object)",
        0x10 => "Alarm (Callback Object)",
        0x11 => "Mandatory Label",
        0x12 => "Resource Attribute",
        0x13 => "Scoped Policy ID",
        _ => $"Unknown (0x{aceType:X2})"
    };

    private static List<string> DecodeAceFlags(byte flags)
    {
        var result = new List<string>();
        if ((flags & 0x01) != 0) result.Add("OBJECT_INHERIT");
        if ((flags & 0x02) != 0) result.Add("CONTAINER_INHERIT");
        if ((flags & 0x04) != 0) result.Add("NO_PROPAGATE_INHERIT");
        if ((flags & 0x08) != 0) result.Add("INHERIT_ONLY");
        if ((flags & 0x10) != 0) result.Add("INHERITED_ACE");
        if ((flags & 0x40) != 0) result.Add("SUCCESSFUL_ACCESS");
        if ((flags & 0x80) != 0) result.Add("FAILED_ACCESS");
        return result;
    }

    /// <summary>
    /// Decode an AD access mask into permission names per MS-ADTS 5.1.3.2.
    /// </summary>
    private static List<string> DecodeAccessMask(uint mask)
    {
        var perms = new List<string>();

        // Check for full control first
        if ((mask & 0x000F01FF) == 0x000F01FF)
        {
            perms.Add("Full Control");
            return perms;
        }

        // Check for Generic rights (bits 28-31)
        if ((mask & 0x10000000) != 0) perms.Add("GENERIC_ALL");
        if ((mask & 0x20000000) != 0) perms.Add("GENERIC_EXECUTE");
        if ((mask & 0x40000000) != 0) perms.Add("GENERIC_WRITE");
        if ((mask & 0x80000000) != 0) perms.Add("GENERIC_READ");

        // Standard rights (bits 16-23)
        if ((mask & 0x00010000) != 0) perms.Add("Delete");
        if ((mask & 0x00020000) != 0) perms.Add("Read Permissions");
        if ((mask & 0x00040000) != 0) perms.Add("Modify Permissions");
        if ((mask & 0x00080000) != 0) perms.Add("Modify Owner");

        // Directory services specific rights (bits 0-8)
        if ((mask & 0x00000001) != 0) perms.Add("Create Child");
        if ((mask & 0x00000002) != 0) perms.Add("Delete Child");
        if ((mask & 0x00000004) != 0) perms.Add("List Contents");
        if ((mask & 0x00000008) != 0) perms.Add("Self");
        if ((mask & 0x00000010) != 0) perms.Add("Read Property");
        if ((mask & 0x00000020) != 0) perms.Add("Write Property");
        if ((mask & 0x00000040) != 0) perms.Add("Delete Tree");
        if ((mask & 0x00000080) != 0) perms.Add("List Object");
        if ((mask & 0x00000100) != 0) perms.Add("Control Access");

        return perms;
    }

    // Well-known schema/extended-rights GUIDs
    private static readonly Dictionary<string, string> SchemaGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        // Property sets
        ["72e39547-7b18-11d1-adef-00c04fd8d5cd"] = "DNS Host Name Attributes",
        ["b8119fd0-04f6-4762-ab7a-4986c76b3f9a"] = "Other Domain Parameters (for use by SAM)",
        ["c7407360-20bf-11d0-a768-00aa006e0529"] = "Domain Password & Lockout Policies",
        ["e45795b2-9455-11d1-aebd-0000f80367c1"] = "Phone and Mail Options",
        ["59ba2f42-79a2-11d0-9020-00c04fc2d3cf"] = "General Information",
        ["bc0ac240-79a9-11d0-9020-00c04fc2d3cf"] = "Membership",
        ["e48d0154-bcf8-11d1-8702-00c04fb96050"] = "Public Information",
        ["037088f8-0ae1-11d2-b422-00a0c968f939"] = "RAS Information",
        ["5805bc62-bdc9-4428-a5e2-856a0f4c185e"] = "Terminal Server License Server",
        ["4c164200-20c0-11d0-a768-00aa006e0529"] = "Account Restrictions",
        ["5f202010-79a5-11d0-9020-00c04fc2d3cf"] = "Logon Information",
        ["e45795b3-9455-11d1-aebd-0000f80367c1"] = "Web Information",
        ["77b5b886-944a-11d1-aebd-0000f80367c1"] = "Personal Information",

        // Extended rights
        ["00299570-246d-11d0-a768-00aa006e0529"] = "User-Force-Change-Password",
        ["ab721a53-1e2f-11d0-9819-00aa0040529b"] = "User-Change-Password",
        ["1131f6aa-9c07-11d1-f79f-00c04fc2dcd2"] = "DS-Replication-Get-Changes",
        ["1131f6ad-9c07-11d1-f79f-00c04fc2dcd2"] = "DS-Replication-Get-Changes-All",
        ["89e95b76-444d-4c62-991a-0facbeda640c"] = "DS-Replication-Get-Changes-In-Filtered-Set",
        ["1131f6ab-9c07-11d1-f79f-00c04fc2dcd2"] = "DS-Replication-Manage-Topologies",
        ["1131f6ac-9c07-11d1-f79f-00c04fc2dcd2"] = "DS-Replication-Synchronize",
        ["ccc2dc7d-a6ad-4a7a-8846-c04e3cc53501"] = "Unexpire-Password",
        ["edacfd8f-ffb3-11d1-b41d-00a0c968f939"] = "Apply-Group-Policy",
        ["0e10c968-78fb-11d2-90d4-00c04f79dc55"] = "Certificate-Enrollment",
        ["a05b8cc2-17bc-4802-a710-e7c15ab866a2"] = "Certificate-AutoEnrollment",

        // Validated writes
        ["bf9679c0-0de6-11d0-a285-00aa003049e2"] = "Self-Membership",
        ["72e39547-7b18-11d1-adef-00c04fd8d5cd"] = "Validated-DNS-Host-Name",
        ["80863791-dbe9-4eb8-837e-7f0ab55d9ac7"] = "Validated-MS-DS-Additional-DNS-Host-Name",
        ["d31a8757-2447-4545-8081-3bb610cacbf2"] = "Validated-MS-DS-Behavior-Version",
        ["f3a64788-5306-11d1-a9c5-0000f80367c1"] = "Validated-SPN",
    };

    private static string ResolveSchemaGuid(string guid)
    {
        SchemaGuids.TryGetValue(guid, out var name);
        return name;
    }
}

public class SecurityDescriptorInfo
{
    public string Owner { get; set; } = "";
    public string OwnerName { get; set; }
    public string Group { get; set; } = "";
    public string GroupName { get; set; }
    public List<AceInfo> Dacl { get; set; } = [];
    public List<AceInfo> Sacl { get; set; } = [];
    public SecurityDescriptorControl Control { get; set; }
}

public class AceInfo
{
    public string Type { get; set; } = "";
    public string Principal { get; set; } = "";
    public string PrincipalName { get; set; }
    public string AccessMask { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public string ObjectType { get; set; }
    public string ObjectTypeName { get; set; }
    public string InheritedObjectType { get; set; }
    public string InheritedObjectTypeName { get; set; }
    public List<string> Flags { get; set; } = [];
    public bool IsInherited { get; set; }
}

[Flags]
public enum SecurityDescriptorControl : ushort
{
    None = 0,
    OwnerDefaulted = 0x0001,
    GroupDefaulted = 0x0002,
    DaclPresent = 0x0004,
    DaclDefaulted = 0x0008,
    SaclPresent = 0x0010,
    SaclDefaulted = 0x0020,
    DaclAutoInheritReq = 0x0100,
    SaclAutoInheritReq = 0x0200,
    DaclAutoInherited = 0x0400,
    SaclAutoInherited = 0x0800,
    DaclProtected = 0x1000,
    SaclProtected = 0x2000,
    RmControlValid = 0x4000,
    SelfRelative = 0x8000,
}
