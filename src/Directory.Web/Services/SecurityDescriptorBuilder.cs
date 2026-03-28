using System.Buffers.Binary;

namespace Directory.Web.Services;

/// <summary>
/// Builds Windows NT Security Descriptors (binary format) from component parts.
/// Implements building per MS-DTYP 2.4.6 (SECURITY_DESCRIPTOR) in self-relative format.
/// </summary>
public class SecurityDescriptorBuilder
{
    /// <summary>
    /// Parse a SID string (S-1-5-21-...) into its binary representation.
    /// </summary>
    public static byte[] ParseSidString(string sidString)
    {
        if (string.IsNullOrWhiteSpace(sidString))
            throw new ArgumentException("SID string is empty.");

        var parts = sidString.Split('-');
        if (parts.Length < 3 || parts[0] != "S")
            throw new ArgumentException($"Invalid SID format: {sidString}");

        byte revision = byte.Parse(parts[1]);
        long authority = long.Parse(parts[2]);
        int subAuthorityCount = parts.Length - 3;

        var sid = new byte[8 + subAuthorityCount * 4];
        sid[0] = revision;
        sid[1] = (byte)subAuthorityCount;

        // 6-byte big-endian authority
        for (int i = 5; i >= 0; i--)
        {
            sid[2 + (5 - i)] = (byte)((authority >> (i * 8)) & 0xFF);
        }

        for (int i = 0; i < subAuthorityCount; i++)
        {
            uint subAuth = uint.Parse(parts[3 + i]);
            BinaryPrimitives.WriteUInt32LittleEndian(sid.AsSpan(8 + i * 4), subAuth);
        }

        return sid;
    }

    /// <summary>
    /// Build a complete self-relative security descriptor from parts.
    /// </summary>
    public static byte[] Build(
        string ownerSid,
        string groupSid,
        SecurityDescriptorControl control,
        List<AceDto> daclAces,
        List<AceDto> saclAces)
    {
        var ownerBytes = ParseSidString(ownerSid);
        var groupBytes = ParseSidString(groupSid);
        var daclBytes = daclAces != null ? BuildAcl(daclAces) : null;
        var saclBytes = saclAces != null ? BuildAcl(saclAces) : null;

        // Ensure self-relative and presence flags
        control |= SecurityDescriptorControl.SelfRelative;
        if (daclBytes != null) control |= SecurityDescriptorControl.DaclPresent;
        if (saclBytes != null) control |= SecurityDescriptorControl.SaclPresent;

        // Calculate offsets: header is 20 bytes
        int offset = 20;

        int offsetSacl = 0;
        if (saclBytes != null)
        {
            offsetSacl = offset;
            offset += saclBytes.Length;
        }

        int offsetDacl = 0;
        if (daclBytes != null)
        {
            offsetDacl = offset;
            offset += daclBytes.Length;
        }

        int offsetOwner = offset;
        offset += ownerBytes.Length;

        int offsetGroup = offset;
        offset += groupBytes.Length;

        var descriptor = new byte[offset];

        // Header
        descriptor[0] = 1; // Revision
        descriptor[1] = 0; // Sbz1
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(2), (ushort)control);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(4), offsetOwner);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(8), offsetGroup);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(12), offsetSacl);
        BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(16), offsetDacl);

        // Copy data
        if (saclBytes != null)
            saclBytes.CopyTo(descriptor, offsetSacl);
        if (daclBytes != null)
            daclBytes.CopyTo(descriptor, offsetDacl);
        ownerBytes.CopyTo(descriptor, offsetOwner);
        groupBytes.CopyTo(descriptor, offsetGroup);

        return descriptor;
    }

    /// <summary>
    /// Replace just the owner SID in an existing security descriptor.
    /// </summary>
    public static byte[] ReplaceOwner(byte[] existingDescriptor, string newOwnerSid)
    {
        var parser = new SecurityDescriptorParser();
        var info = parser.Parse(existingDescriptor);

        var daclAces = ConvertAceInfoListToDto(info.Dacl);
        var saclAces = ConvertAceInfoListToDto(info.Sacl);

        return Build(newOwnerSid, info.Group, info.Control, daclAces, saclAces);
    }

    /// <summary>
    /// Replace the DACL in an existing security descriptor.
    /// </summary>
    public static byte[] ReplaceDacl(byte[] existingDescriptor, List<AceDto> newDaclAces)
    {
        var parser = new SecurityDescriptorParser();
        var info = parser.Parse(existingDescriptor);

        var saclAces = ConvertAceInfoListToDto(info.Sacl);

        return Build(info.Owner, info.Group, info.Control, newDaclAces, saclAces);
    }

    /// <summary>
    /// Add a single ACE to the DACL of an existing security descriptor.
    /// </summary>
    public static byte[] AddDaclAce(byte[] existingDescriptor, AceDto newAce)
    {
        var parser = new SecurityDescriptorParser();
        var info = parser.Parse(existingDescriptor);

        var daclAces = ConvertAceInfoListToDto(info.Dacl);
        daclAces.Add(newAce);

        var saclAces = ConvertAceInfoListToDto(info.Sacl);
        return Build(info.Owner, info.Group, info.Control, daclAces, saclAces);
    }

    /// <summary>
    /// Remove an ACE at a specific index from the DACL.
    /// </summary>
    public static byte[] RemoveDaclAce(byte[] existingDescriptor, int aceIndex)
    {
        var parser = new SecurityDescriptorParser();
        var info = parser.Parse(existingDescriptor);

        var daclAces = ConvertAceInfoListToDto(info.Dacl);
        if (aceIndex < 0 || aceIndex >= daclAces.Count)
            throw new ArgumentOutOfRangeException(nameof(aceIndex), $"ACE index {aceIndex} is out of range (0-{daclAces.Count - 1}).");

        daclAces.RemoveAt(aceIndex);

        var saclAces = ConvertAceInfoListToDto(info.Sacl);
        return Build(info.Owner, info.Group, info.Control, daclAces, saclAces);
    }

    /// <summary>
    /// Set or clear the DaclProtected flag (inheritance control).
    /// When disabling inheritance (setting DaclProtected), inherited ACEs are converted to explicit ACEs.
    /// </summary>
    public static byte[] SetInheritance(byte[] existingDescriptor, bool enabled)
    {
        var parser = new SecurityDescriptorParser();
        var info = parser.Parse(existingDescriptor);

        var control = info.Control;
        var daclAces = ConvertAceInfoListToDto(info.Dacl);

        if (!enabled)
        {
            // Disabling inheritance: set DaclProtected, convert inherited ACEs to explicit
            control |= SecurityDescriptorControl.DaclProtected;
            foreach (var ace in daclAces)
            {
                // Remove INHERITED_ACE flag from all ACEs, making them explicit
                ace.Flags.Remove("INHERITED_ACE");
            }
        }
        else
        {
            // Enabling inheritance: clear DaclProtected
            control &= ~SecurityDescriptorControl.DaclProtected;
        }

        var saclAces = ConvertAceInfoListToDto(info.Sacl);
        return Build(info.Owner, info.Group, control, daclAces, saclAces);
    }

    private static List<AceDto> ConvertAceInfoListToDto(List<AceInfo> aces)
    {
        return aces.Select(a => new AceDto
        {
            Type = a.Type.StartsWith("Deny", StringComparison.OrdinalIgnoreCase) ? "deny" : "allow",
            PrincipalSid = a.Principal,
            AccessMask = ParseAccessMask(a.AccessMask),
            Flags = new List<string>(a.Flags),
            ObjectType = a.ObjectType,
            InheritedObjectType = a.InheritedObjectType,
            IsObjectAce = a.ObjectType != null || a.InheritedObjectType != null,
        }).ToList();
    }

    private static uint ParseAccessMask(string mask)
    {
        if (mask.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(mask, 16);
        if (uint.TryParse(mask, out var val))
            return val;
        return 0;
    }

    /// <summary>
    /// Build an ACL (DACL or SACL) from a list of ACE DTOs.
    /// </summary>
    private static byte[] BuildAcl(List<AceDto> aces)
    {
        var aceBytes = new List<byte[]>();
        foreach (var ace in aces)
        {
            aceBytes.Add(BuildAce(ace));
        }

        int aclSize = 8; // ACL header size
        foreach (var ab in aceBytes) aclSize += ab.Length;

        var acl = new byte[aclSize];
        acl[0] = 2; // Revision
        acl[1] = 0; // Sbz1
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(2), (ushort)aclSize);
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(4), (ushort)aces.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(6), 0); // Sbz2

        int pos = 8;
        foreach (var ab in aceBytes)
        {
            ab.CopyTo(acl, pos);
            pos += ab.Length;
        }

        return acl;
    }

    private static byte[] BuildAce(AceDto ace)
    {
        byte aceType = GetAceTypeCode(ace);
        byte aceFlags = EncodeAceFlags(ace.Flags);
        var sidBytes = ParseSidString(ace.PrincipalSid);

        bool isObject = ace.IsObjectAce || ace.ObjectType != null || ace.InheritedObjectType != null;

        if (isObject)
        {
            // Object ACE: Header(4) + Mask(4) + ObjectFlags(4) + [ObjectType(16)] + [InheritedObjectType(16)] + SID
            uint objectFlags = 0;
            int guidSize = 0;

            if (!string.IsNullOrEmpty(ace.ObjectType))
            {
                objectFlags |= 0x01; // ACE_OBJECT_TYPE_PRESENT
                guidSize += 16;
            }
            if (!string.IsNullOrEmpty(ace.InheritedObjectType))
            {
                objectFlags |= 0x02; // ACE_INHERITED_OBJECT_TYPE_PRESENT
                guidSize += 16;
            }

            int aceSize = 4 + 4 + 4 + guidSize + sidBytes.Length;
            var data = new byte[aceSize];

            data[0] = aceType;
            data[1] = aceFlags;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)aceSize);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ace.AccessMask);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), objectFlags);

            int pos = 12;
            if (!string.IsNullOrEmpty(ace.ObjectType))
            {
                Guid.Parse(ace.ObjectType).TryWriteBytes(data.AsSpan(pos));
                pos += 16;
            }
            if (!string.IsNullOrEmpty(ace.InheritedObjectType))
            {
                Guid.Parse(ace.InheritedObjectType).TryWriteBytes(data.AsSpan(pos));
                pos += 16;
            }

            sidBytes.CopyTo(data, pos);
            return data;
        }
        else
        {
            // Standard ACE: Header(4) + Mask(4) + SID
            int aceSize = 4 + 4 + sidBytes.Length;
            var data = new byte[aceSize];

            data[0] = aceType;
            data[1] = aceFlags;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)aceSize);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ace.AccessMask);
            sidBytes.CopyTo(data, 8);

            return data;
        }
    }

    private static byte GetAceTypeCode(AceDto ace)
    {
        bool isObject = ace.IsObjectAce || ace.ObjectType != null || ace.InheritedObjectType != null;

        return ace.Type.ToLowerInvariant() switch
        {
            "deny" when isObject => 0x06,
            "deny" => 0x01,
            "audit" when isObject => 0x07,
            "audit" => 0x02,
            "allow" when isObject => 0x05,
            "allow" => 0x00,
            _ => 0x00,
        };
    }

    private static byte EncodeAceFlags(List<string> flags)
    {
        byte result = 0;
        foreach (var flag in flags)
        {
            result |= flag.ToUpperInvariant() switch
            {
                "OBJECT_INHERIT" => 0x01,
                "CONTAINER_INHERIT" => 0x02,
                "NO_PROPAGATE_INHERIT" => 0x04,
                "INHERIT_ONLY" => 0x08,
                "INHERITED_ACE" => 0x10,
                "SUCCESSFUL_ACCESS" => 0x40,
                "FAILED_ACCESS" => 0x80,
                _ => 0x00,
            };
        }
        return result;
    }
}

/// <summary>
/// Data transfer object for ACE operations. Used in security editing endpoints.
/// </summary>
public class AceDto
{
    public string Type { get; set; } = "allow"; // "allow", "deny", "audit"
    public string PrincipalSid { get; set; } = "";
    public uint AccessMask { get; set; }
    public List<string> Flags { get; set; } = [];
    public string ObjectType { get; set; }
    public string InheritedObjectType { get; set; }
    public bool IsObjectAce { get; set; }
}
