using System.Buffers.Binary;

namespace Directory.Core.Models;

[Flags]
public enum SdControlFlags : ushort
{
    OwnerDefaulted = 0x0001,
    GroupDefaulted = 0x0002,
    DaclPresent = 0x0004,
    DaclDefaulted = 0x0008,
    SaclPresent = 0x0010,
    SaclDefaulted = 0x0020,
    DaclAutoInherited = 0x0400,
    SaclAutoInherited = 0x0800,
    DaclProtected = 0x1000,
    SaclProtected = 0x2000,
    SelfRelative = 0x8000
}

public enum AceType : byte
{
    AccessAllowed = 0,
    AccessDenied = 1,
    SystemAudit = 2,
    AccessAllowedObject = 5,
    AccessDeniedObject = 6
}

[Flags]
public enum AceFlags : byte
{
    None = 0,
    ObjectInherit = 0x01,
    ContainerInherit = 0x02,
    NoPropagateInherit = 0x04,
    InheritOnly = 0x08,
    Inherited = 0x10
}

public static class AccessMask
{
    public const int CreateChild = 0x00000001;
    public const int DeleteChild = 0x00000002;
    public const int ListContents = 0x00000004;
    public const int WriteProperty = 0x00000010;
    public const int ReadProperty = 0x00000020;
    public const int DeleteTree = 0x00000040;
    public const int ListObject = 0x00000080;
    public const int ControlAccess = 0x00000100;
    public const int DeleteObject = 0x00010000;
    public const int ReadControl = 0x00020000;
    public const int WriteDacl = 0x00040000;
    public const int WriteOwner = 0x00080000;
    public const int GenericAll = 0x10000000;
    public const int GenericExecute = 0x20000000;
    public const int GenericWrite = unchecked((int)0x40000000);
    public const int GenericRead = unchecked((int)0x80000000);
}

public class AccessControlEntry
{
    public AceType Type { get; set; }
    public AceFlags Flags { get; set; }
    public int Mask { get; set; }
    public string TrusteeSid { get; set; } = string.Empty;
    public Guid? ObjectType { get; set; }
    public Guid? InheritedObjectType { get; set; }

    public byte[] Serialize()
    {
        var sidBytes = SerializeSid(TrusteeSid);
        bool isObject = Type == AceType.AccessAllowedObject || Type == AceType.AccessDeniedObject;
        int objectDataLen = 0;
        uint objectFlags = 0;

        if (isObject)
        {
            objectDataLen += 4; // object flags field
            if (ObjectType.HasValue) { objectFlags |= 1; objectDataLen += 16; }
            if (InheritedObjectType.HasValue) { objectFlags |= 2; objectDataLen += 16; }
        }

        int aceSize = 4 + 4 + objectDataLen + sidBytes.Length; // header(4) + mask(4) + object data + SID
        var buffer = new byte[aceSize];

        buffer[0] = (byte)Type;
        buffer[1] = (byte)Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)aceSize);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), Mask);

        int offset = 8;
        if (isObject)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), objectFlags);
            offset += 4;
            if (ObjectType.HasValue)
            {
                ObjectType.Value.ToByteArray().CopyTo(buffer, offset);
                offset += 16;
            }
            if (InheritedObjectType.HasValue)
            {
                InheritedObjectType.Value.ToByteArray().CopyTo(buffer, offset);
                offset += 16;
            }
        }

        sidBytes.CopyTo(buffer, offset);
        return buffer;
    }

    public static AccessControlEntry Deserialize(ReadOnlySpan<byte> data)
    {
        var ace = new AccessControlEntry
        {
            Type = (AceType)data[0],
            Flags = (AceFlags)data[1],
            Mask = BinaryPrimitives.ReadInt32LittleEndian(data[4..])
        };

        int offset = 8;
        bool isObject = ace.Type == AceType.AccessAllowedObject || ace.Type == AceType.AccessDeniedObject;

        if (isObject)
        {
            uint objectFlags = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;
            if ((objectFlags & 1) != 0)
            {
                ace.ObjectType = new Guid(data.Slice(offset, 16));
                offset += 16;
            }
            if ((objectFlags & 2) != 0)
            {
                ace.InheritedObjectType = new Guid(data.Slice(offset, 16));
                offset += 16;
            }
        }

        ace.TrusteeSid = DeserializeSid(data[offset..]);
        return ace;
    }

    public static byte[] SerializeSid(string sid)
    {
        var parts = sid.Split('-');
        // S-1-{authority}-{sub1}-{sub2}-...
        byte revision = byte.Parse(parts[1]);
        long authority = long.Parse(parts[2]);
        int subCount = parts.Length - 3;

        var buffer = new byte[8 + subCount * 4];
        buffer[0] = revision;
        buffer[1] = (byte)subCount;
        // 6-byte big-endian authority
        for (int i = 0; i < 6; i++)
            buffer[2 + i] = (byte)(authority >> (8 * (5 - i)));

        for (int i = 0; i < subCount; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8 + i * 4), uint.Parse(parts[3 + i]));

        return buffer;
    }

    internal static string DeserializeSid(ReadOnlySpan<byte> data)
    {
        byte revision = data[0];
        byte subCount = data[1];
        long authority = 0;
        for (int i = 0; i < 6; i++)
            authority = (authority << 8) | data[2 + i];

        var parts = new string[3 + subCount];
        parts[0] = "S";
        parts[1] = revision.ToString();
        parts[2] = authority.ToString();

        for (int i = 0; i < subCount; i++)
            parts[3 + i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8 + i * 4, 4)).ToString();

        return string.Join('-', parts);
    }
}

public class AccessControlList
{
    public List<AccessControlEntry> Aces { get; set; } = [];

    public byte[] Serialize()
    {
        var aceData = new List<byte[]>();
        int totalAceSize = 0;
        foreach (var ace in Aces)
        {
            var b = ace.Serialize();
            aceData.Add(b);
            totalAceSize += b.Length;
        }

        var buffer = new byte[8 + totalAceSize]; // ACL header is 8 bytes
        buffer[0] = 2; // AclRevision
        buffer[1] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)buffer.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)Aces.Count);
        // 2 bytes reserved at offset 6

        int offset = 8;
        foreach (var b in aceData)
        {
            b.CopyTo(buffer, offset);
            offset += b.Length;
        }

        return buffer;
    }

    public static AccessControlList Deserialize(ReadOnlySpan<byte> data)
    {
        var acl = new AccessControlList();
        int aceCount = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        int offset = 8;

        for (int i = 0; i < aceCount; i++)
        {
            int aceSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 2, 2));
            acl.Aces.Add(AccessControlEntry.Deserialize(data.Slice(offset, aceSize)));
            offset += aceSize;
        }

        return acl;
    }
}

public class SecurityDescriptor
{
    public string OwnerSid { get; set; }
    public string GroupSid { get; set; }
    public AccessControlList Dacl { get; set; }
    public AccessControlList Sacl { get; set; }
    public SdControlFlags Control { get; set; }

    public byte[] Serialize()
    {
        var ownerBytes = OwnerSid != null ? AccessControlEntry.SerializeSid(OwnerSid) : [];
        var groupBytes = GroupSid != null ? AccessControlEntry.SerializeSid(GroupSid) : [];
        var saclBytes = Sacl != null && Control.HasFlag(SdControlFlags.SaclPresent) ? Sacl.Serialize() : [];
        var daclBytes = Dacl != null && Control.HasFlag(SdControlFlags.DaclPresent) ? Dacl.Serialize() : [];

        var flags = Control | SdControlFlags.SelfRelative;
        int headerSize = 20;
        int offset = headerSize;

        int ownerOffset = ownerBytes.Length > 0 ? offset : 0;
        offset += ownerBytes.Length;

        int groupOffset = groupBytes.Length > 0 ? offset : 0;
        offset += groupBytes.Length;

        int saclOffset = saclBytes.Length > 0 ? offset : 0;
        offset += saclBytes.Length;

        int daclOffset = daclBytes.Length > 0 ? offset : 0;
        offset += daclBytes.Length;

        var buffer = new byte[offset];
        buffer[0] = 1; // Revision
        buffer[1] = 0; // Sbz1
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)flags);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), ownerOffset);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), groupOffset);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12), saclOffset);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), daclOffset);

        if (ownerBytes.Length > 0) ownerBytes.CopyTo(buffer, ownerOffset);
        if (groupBytes.Length > 0) groupBytes.CopyTo(buffer, groupOffset);
        if (saclBytes.Length > 0) saclBytes.CopyTo(buffer, saclOffset);
        if (daclBytes.Length > 0) daclBytes.CopyTo(buffer, daclOffset);

        return buffer;
    }

    public static SecurityDescriptor Deserialize(ReadOnlySpan<byte> data)
    {
        var sd = new SecurityDescriptor
        {
            Control = (SdControlFlags)BinaryPrimitives.ReadUInt16LittleEndian(data[2..])
        };

        int ownerOffset = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        int groupOffset = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        int saclOffset = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        int daclOffset = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);

        if (ownerOffset > 0)
            sd.OwnerSid = AccessControlEntry.DeserializeSid(data[ownerOffset..]);

        if (groupOffset > 0)
            sd.GroupSid = AccessControlEntry.DeserializeSid(data[groupOffset..]);

        if (saclOffset > 0 && sd.Control.HasFlag(SdControlFlags.SaclPresent))
            sd.Sacl = AccessControlList.Deserialize(data[saclOffset..]);

        if (daclOffset > 0 && sd.Control.HasFlag(SdControlFlags.DaclPresent))
            sd.Dacl = AccessControlList.Deserialize(data[daclOffset..]);

        return sd;
    }
}

public static class WellKnownSids
{
    public const string Everyone = "S-1-1-0";
    public const string AuthenticatedUsers = "S-1-5-11";
    public const string Administrators = "S-1-5-32-544";
    public const string System = "S-1-5-18";
    public const string CreatorOwner = "S-1-3-0";
    public const string Self = "S-1-5-10";

    // Domain-relative RIDs
    public const int DomainAdminsRid = 512;
    public const int DomainUsersRid = 513;
    public const int DomainComputersRid = 515;
    public const int DomainControllersRid = 516;

    public static string DomainAdmins(string domainSid) => $"{domainSid}-{DomainAdminsRid}";
    public static string DomainUsers(string domainSid) => $"{domainSid}-{DomainUsersRid}";
    public static string DomainComputers(string domainSid) => $"{domainSid}-{DomainComputersRid}";
    public static string DomainControllers(string domainSid) => $"{domainSid}-{DomainControllersRid}";
}
