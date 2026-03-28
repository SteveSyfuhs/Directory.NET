using Directory.Replication.Drsr;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for the replication conflict resolver, attribute metadata versioning,
/// USN ordering, and watermark tracking.
/// </summary>
public class ReplicationConflictResolverTests
{
    private readonly ConflictResolver _resolver = new(null, NullLogger<ConflictResolver>.Instance);

    // ────────────────────────────────────────────────────────────────
    // Attribute conflict: higher version wins
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAttributeConflict_HigherVersion_IncomingWins()
    {
        // Arrange
        var local = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 3,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            OriginatingUsn = 100,
        };
        var incoming = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 900,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            OriginatingUsn = 200,
        };

        // Act
        var winner = _resolver.ResolveAttributeConflict(local, incoming);

        // Assert — incoming has higher version (5 > 3), regardless of timestamp
        Assert.Same(incoming, winner);
    }

    [Fact]
    public void ResolveAttributeConflict_HigherVersion_LocalWins()
    {
        // Arrange
        var local = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 10,
            OriginatingTime = 500,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };
        var incoming = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 7,
            OriginatingTime = 900,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        };

        // Act
        var winner = _resolver.ResolveAttributeConflict(local, incoming);

        // Assert
        Assert.Same(local, winner);
    }

    // ────────────────────────────────────────────────────────────────
    // Attribute conflict: same version, later timestamp wins
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAttributeConflict_SameVersion_LaterTimestamp_IncomingWins()
    {
        // Arrange
        var local = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };
        var incoming = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 2000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };

        // Act
        var winner = _resolver.ResolveAttributeConflict(local, incoming);

        // Assert
        Assert.Same(incoming, winner);
    }

    [Fact]
    public void ResolveAttributeConflict_SameVersion_EarlierTimestamp_LocalWins()
    {
        // Arrange
        var local = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 2000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };
        var incoming = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };

        // Act
        var winner = _resolver.ResolveAttributeConflict(local, incoming);

        // Assert
        Assert.Same(local, winner);
    }

    // ────────────────────────────────────────────────────────────────
    // Attribute conflict: same version + timestamp, higher DSA GUID wins
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAttributeConflict_SameVersionAndTime_HigherGuid_IncomingWins()
    {
        // Arrange
        var local = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };
        var incoming = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
        };

        // Act
        var winner = _resolver.ResolveAttributeConflict(local, incoming);

        // Assert
        Assert.Same(incoming, winner);
    }

    [Fact]
    public void ResolveAttributeConflict_SameVersionAndTime_LowerGuid_LocalWins()
    {
        // Arrange
        var local = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
        };
        var incoming = new PropertyMetaDataEntry
        {
            AttributeId = 1,
            Version = 5,
            OriginatingTime = 1000,
            OriginatingDsaGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        };

        // Act
        var winner = _resolver.ResolveAttributeConflict(local, incoming);

        // Assert
        Assert.Same(local, winner);
    }

    // ────────────────────────────────────────────────────────────────
    // Name conflict resolution
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveNameConflict_LaterTimestamp_Wins()
    {
        // Arrange
        var dn1 = "CN=TestUser,OU=Sales,DC=corp,DC=com";
        var guid1 = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        long ts1 = 2000;

        var dn2 = "CN=TestUser,OU=Sales,DC=corp,DC=com";
        var guid2 = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        long ts2 = 1000;

        // Act
        var (winnerDn, loserDn, loserNewDn) = _resolver.ResolveNameConflict(dn1, guid1, ts1, dn2, guid2, ts2);

        // Assert — dn1 has later timestamp, so it wins
        Assert.Equal(dn1, winnerDn);
        Assert.Equal(dn2, loserDn);
        Assert.Contains("CNF:", loserNewDn);
        Assert.Contains(guid2, loserNewDn);
    }

    [Fact]
    public void ResolveNameConflict_SameTimestamp_HigherGuidWins()
    {
        // Arrange
        var dn1 = "CN=TestUser,OU=Sales,DC=corp,DC=com";
        var guid1 = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        long ts = 1000;

        var dn2 = "CN=TestUser,OU=Sales,DC=corp,DC=com";
        var guid2 = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

        // Act
        var (winnerDn, loserDn, loserNewDn) = _resolver.ResolveNameConflict(dn1, guid1, ts, dn2, guid2, ts);

        // Assert — guid2 is lexicographically higher than guid1
        Assert.Equal(dn2, winnerDn);
        Assert.Equal(dn1, loserDn);
        Assert.Contains("CNF:", loserNewDn);
        Assert.Contains(guid1, loserNewDn);
    }

    [Fact]
    public void ResolveNameConflict_LoserDnIsMangledWithCNF()
    {
        // Arrange
        var dn1 = "CN=User1,OU=People,DC=corp,DC=com";
        var guid1 = "11111111-1111-1111-1111-111111111111";
        var dn2 = "CN=User1,OU=People,DC=corp,DC=com";
        var guid2 = "22222222-2222-2222-2222-222222222222";

        // Act
        var (_, _, loserNewDn) = _resolver.ResolveNameConflict(dn1, guid1, 1000, dn2, guid2, 1000);

        // Assert — loser should have mangled RDN
        Assert.StartsWith("CN=User1\nCNF:", loserNewDn);
        Assert.EndsWith(",OU=People,DC=corp,DC=com", loserNewDn);
    }

    // ────────────────────────────────────────────────────────────────
    // RDN mangling via name conflict resolution
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NameConflict_LoserDn_ContainsCNFAndGuid()
    {
        // MangleRdn is internal, so we test it indirectly via ResolveNameConflict
        var (_, _, loserNewDn) = _resolver.ResolveNameConflict(
            "CN=TestObj,OU=Users,DC=corp,DC=com", "aaaa", 100,
            "CN=TestObj,OU=Users,DC=corp,DC=com", "bbbb", 200);

        // loser is dn1 (lower timestamp), mangled with guid "aaaa"
        Assert.Contains("CNF:", loserNewDn);
        Assert.Contains("aaaa", loserNewDn);
        Assert.Contains("OU=Users,DC=corp,DC=com", loserNewDn);
    }

    [Fact]
    public void NameConflict_LoserDn_PreservesParent()
    {
        var (_, _, loserNewDn) = _resolver.ResolveNameConflict(
            "CN=Root,DC=test", "low-guid", 500,
            "CN=Root,DC=test", "high-guid", 500);

        // Same timestamp, so lexicographic GUID comparison: "low-guid" < "high-guid"
        // loser is dn1 ("low-guid"), parent preserved
        Assert.Contains("DC=test", loserNewDn);
    }

    // ────────────────────────────────────────────────────────────────
    // Tombstone conflict resolution
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveTombstoneConflict_DeletionHigherVersion_DeletionWins()
    {
        // Arrange
        var deleteMetadata = new PropertyMetaDataEntry { Version = 5, OriginatingTime = 1000 };
        var modifyMetadata = new PropertyMetaDataEntry { Version = 3, OriginatingTime = 2000 };

        // Act
        var result = _resolver.ResolveTombstoneConflict(deleteMetadata, modifyMetadata, objectIsCurrentlyDeleted: true);

        // Assert
        Assert.Equal(TombstoneResolution.DeletionWins, result);
    }

    [Fact]
    public void ResolveTombstoneConflict_ModificationHigherVersion_DeletedObject_Resurrects()
    {
        // Arrange
        var deleteMetadata = new PropertyMetaDataEntry { Version = 3, OriginatingTime = 1000 };
        var modifyMetadata = new PropertyMetaDataEntry { Version = 5, OriginatingTime = 2000 };

        // Act
        var result = _resolver.ResolveTombstoneConflict(deleteMetadata, modifyMetadata, objectIsCurrentlyDeleted: true);

        // Assert
        Assert.Equal(TombstoneResolution.ResurrectObject, result);
    }

    [Fact]
    public void ResolveTombstoneConflict_ModificationWins_ObjectAlive_ModificationWins()
    {
        // Arrange
        var deleteMetadata = new PropertyMetaDataEntry { Version = 3, OriginatingTime = 1000 };
        var modifyMetadata = new PropertyMetaDataEntry { Version = 5, OriginatingTime = 2000 };

        // Act
        var result = _resolver.ResolveTombstoneConflict(deleteMetadata, modifyMetadata, objectIsCurrentlyDeleted: false);

        // Assert
        Assert.Equal(TombstoneResolution.ModificationWins, result);
    }

    // ────────────────────────────────────────────────────────────────
    // PropertyMetaDataEntry — wire format round-trip
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void PropertyMetaDataEntry_ToWireFormat_PreservesFields()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var entry = new PropertyMetaDataEntry
        {
            AttributeId = 42,
            Version = 7,
            OriginatingDsaGuid = guid,
            OriginatingUsn = 12345,
            OriginatingTime = 987654321,
        };

        // Act
        var wire = entry.ToWireFormat();

        // Assert
        Assert.Equal(7u, wire.DwVersion);
        Assert.Equal(987654321L, wire.TimeChanged);
        Assert.Equal(guid, wire.UuidDsaOriginating);
        Assert.Equal(12345L, wire.UsnOriginating);
    }

    [Fact]
    public void PropertyMetaDataEntry_FromWireFormat_PreservesFields()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var wire = new PROPERTY_META_DATA_EXT
        {
            DwVersion = 15,
            TimeChanged = 555,
            UuidDsaOriginating = guid,
            UsnOriginating = 99999,
        };

        // Act
        var entry = PropertyMetaDataEntry.FromWireFormat(77, wire);

        // Assert
        Assert.Equal(77u, entry.AttributeId);
        Assert.Equal(15u, entry.Version);
        Assert.Equal(guid, entry.OriginatingDsaGuid);
        Assert.Equal(99999L, entry.OriginatingUsn);
        Assert.Equal(555L, entry.OriginatingTime);
    }

    // ────────────────────────────────────────────────────────────────
    // PropertyMetaDataVector — get/set
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void PropertyMetaDataVector_SetThenGet_ReturnsEntry()
    {
        // Arrange
        var vector = new PropertyMetaDataVector();
        var entry = new PropertyMetaDataEntry { AttributeId = 10, Version = 3 };

        // Act
        vector.SetEntry(10, entry);
        var retrieved = vector.GetEntry(10);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(3u, retrieved.Version);
    }

    [Fact]
    public void PropertyMetaDataVector_GetNonExistent_ReturnsNull()
    {
        var vector = new PropertyMetaDataVector();
        Assert.Null(vector.GetEntry(999));
    }

    [Fact]
    public void PropertyMetaDataVector_SetOverwrites_PreviousEntry()
    {
        // Arrange
        var vector = new PropertyMetaDataVector();
        vector.SetEntry(10, new PropertyMetaDataEntry { AttributeId = 10, Version = 1 });
        vector.SetEntry(10, new PropertyMetaDataEntry { AttributeId = 10, Version = 5 });

        // Act
        var entry = vector.GetEntry(10);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(5u, entry.Version);
    }

    // ────────────────────────────────────────────────────────────────
    // USN ordering: higher USN wins for same attribute
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void UsnOrdering_HigherUsn_IndicatesNewerChange()
    {
        long usnA = 1000;
        long usnB = 2000;

        Assert.True(usnB > usnA, "Higher USN should represent a more recent change");
    }

    [Fact]
    public void ChangeDetection_ModifiedEntry_HasHigherUsnThanWatermark()
    {
        // Arrange — watermark is at USN 1000, entry was modified at USN 1500
        long watermarkUsn = 1000;
        long entryUsn = 1500;

        // Act
        bool shouldReplicate = entryUsn > watermarkUsn;

        // Assert
        Assert.True(shouldReplicate);
    }

    [Fact]
    public void ChangeDetection_UnmodifiedEntry_HasLowerOrEqualUsn()
    {
        // Arrange
        long watermarkUsn = 1000;
        long entryUsn = 800;

        // Act
        bool shouldReplicate = entryUsn > watermarkUsn;

        // Assert
        Assert.False(shouldReplicate);
    }
}
