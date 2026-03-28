using Directory.Core.Models;
using Directory.Security.Claims;
using Xunit;

namespace Directory.Tests;

public class ClaimsTests
{
    // ── ClaimsSet model tests ──────────────────────────────────────────

    [Fact]
    public void ClaimsSet_GetOrCreateArray_CreatesNewArrayForSourceType()
    {
        var claimsSet = new ClaimsSet();
        var adArray = claimsSet.GetOrCreateArray(ClaimSourceType.AD);

        Assert.NotNull(adArray);
        Assert.Equal(ClaimSourceType.AD, adArray.ClaimSourceType);
        Assert.Single(claimsSet.ClaimsArrays);
    }

    [Fact]
    public void ClaimsSet_GetOrCreateArray_ReturnsExistingIfPresent()
    {
        var claimsSet = new ClaimsSet();
        var first = claimsSet.GetOrCreateArray(ClaimSourceType.AD);
        var second = claimsSet.GetOrCreateArray(ClaimSourceType.AD);

        Assert.Same(first, second);
        Assert.Single(claimsSet.ClaimsArrays);
    }

    [Fact]
    public void ClaimsSet_FindClaim_ReturnsClaim()
    {
        var claimsSet = new ClaimsSet();
        var array = claimsSet.GetOrCreateArray(ClaimSourceType.AD);
        array.ClaimEntries.Add(new ClaimEntry
        {
            Id = "ad://ext/department",
            Type = ClaimValueType.STRING,
            StringValues = ["Engineering"]
        });

        var found = claimsSet.FindClaim("ad://ext/department");

        Assert.NotNull(found);
        Assert.Equal("ad://ext/department", found.Id);
        Assert.Contains("Engineering", found.StringValues);
    }

    [Fact]
    public void ClaimsSet_FindClaim_ReturnsNullWhenNotFound()
    {
        var claimsSet = new ClaimsSet();
        claimsSet.GetOrCreateArray(ClaimSourceType.AD);

        Assert.Null(claimsSet.FindClaim("ad://ext/nonexistent"));
    }

    [Fact]
    public void ClaimsSet_FindClaim_CaseInsensitive()
    {
        var claimsSet = new ClaimsSet();
        var array = claimsSet.GetOrCreateArray(ClaimSourceType.AD);
        array.ClaimEntries.Add(new ClaimEntry
        {
            Id = "ad://ext/Department",
            Type = ClaimValueType.STRING,
            StringValues = ["Sales"]
        });

        var found = claimsSet.FindClaim("ad://ext/department");
        Assert.NotNull(found);
    }

    // ── ClaimEntry value types ─────────────────────────────────────────

    [Fact]
    public void ClaimEntry_StringValues_ValueCount()
    {
        var entry = new ClaimEntry
        {
            Id = "test",
            Type = ClaimValueType.STRING,
            StringValues = ["a", "b", "c"]
        };

        Assert.Equal(3, entry.ValueCount);
    }

    [Fact]
    public void ClaimEntry_Int64Values_ValueCount()
    {
        var entry = new ClaimEntry
        {
            Id = "test",
            Type = ClaimValueType.INT64,
            Int64Values = [100L, 200L]
        };

        Assert.Equal(2, entry.ValueCount);
    }

    [Fact]
    public void ClaimEntry_BooleanValues_ValueCount()
    {
        var entry = new ClaimEntry
        {
            Id = "test",
            Type = ClaimValueType.BOOLEAN,
            BooleanValues = [true, false]
        };

        Assert.Equal(2, entry.ValueCount);
    }

    [Fact]
    public void ClaimEntry_NoValues_ValueCountIsZero()
    {
        var entry = new ClaimEntry
        {
            Id = "test",
            Type = ClaimValueType.STRING,
        };

        Assert.Equal(0, entry.ValueCount);
    }

    [Fact]
    public void ClaimEntry_Clone_ProducesDeepCopy()
    {
        var original = new ClaimEntry
        {
            Id = "ad://ext/department",
            Type = ClaimValueType.STRING,
            StringValues = ["Engineering"]
        };

        var clone = original.Clone();

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Type, clone.Type);
        Assert.Equal(original.StringValues, clone.StringValues);

        // Verify it's a deep copy: modifying clone doesn't affect original
        clone.StringValues.Add("Sales");
        Assert.Single(original.StringValues);
    }

    // ── ClaimTypeDefinition tests ──────────────────────────────────────

    [Fact]
    public void ClaimTypeDefinition_ClaimId_FollowsAdExtFormat()
    {
        var def = new ClaimTypeDefinition { Name = "department" };

        Assert.Equal("ad://ext/department", def.ClaimId);
    }

    [Fact]
    public void ClaimTypeDefinition_Defaults()
    {
        var def = new ClaimTypeDefinition();

        Assert.Equal(ClaimValueType.STRING, def.ValueType);
        Assert.False(def.IsDisabled);
        Assert.Empty(def.AppliesToClasses);
    }

    // ── Claims with department and title attributes ────────────────────

    [Fact]
    public void DefaultAttributeMappings_DepartmentClaimId()
    {
        // The ClaimsProvider has default mappings for common AD attributes.
        // Verify the claim entry model can hold department data.
        var entry = new ClaimEntry
        {
            Id = "ad://ext/department",
            Type = ClaimValueType.STRING,
            StringValues = ["Engineering"]
        };

        Assert.Equal("ad://ext/department", entry.Id);
        Assert.Equal(ClaimValueType.STRING, entry.Type);
        Assert.Single(entry.StringValues);
    }

    [Fact]
    public void DefaultAttributeMappings_TitleClaimId()
    {
        var entry = new ClaimEntry
        {
            Id = "ad://ext/title",
            Type = ClaimValueType.STRING,
            StringValues = ["Senior Engineer"]
        };

        Assert.Equal("ad://ext/title", entry.Id);
    }

    // ── Empty claims for user with no claim-generating attributes ──────

    [Fact]
    public void ClaimsSet_EmptyUser_HasNoClaimEntries()
    {
        var claimsSet = new ClaimsSet();
        var array = claimsSet.GetOrCreateArray(ClaimSourceType.AD);

        // No claim entries added — user has no attributes
        Assert.Empty(array.ClaimEntries);
        Assert.Empty(claimsSet.GetAllClaims());
    }

    [Fact]
    public void ClaimsSet_GetAllClaims_AcrossMultipleArrays()
    {
        var claimsSet = new ClaimsSet();

        var adArray = claimsSet.GetOrCreateArray(ClaimSourceType.AD);
        adArray.ClaimEntries.Add(new ClaimEntry
        {
            Id = "ad://ext/department",
            Type = ClaimValueType.STRING,
            StringValues = ["IT"]
        });

        var certArray = claimsSet.GetOrCreateArray(ClaimSourceType.Certificate);
        certArray.ClaimEntries.Add(new ClaimEntry
        {
            Id = "cert://issuer",
            Type = ClaimValueType.STRING,
            StringValues = ["CN=RootCA"]
        });

        var allClaims = claimsSet.GetAllClaims().ToList();
        Assert.Equal(2, allClaims.Count);
    }

    // ── ClaimValueType enum values ─────────────────────────────────────

    [Fact]
    public void ClaimValueType_EnumValues_MatchMsCta()
    {
        Assert.Equal(1, (int)ClaimValueType.INT64);
        Assert.Equal(2, (int)ClaimValueType.UINT64);
        Assert.Equal(3, (int)ClaimValueType.STRING);
        Assert.Equal(5, (int)ClaimValueType.SID);
        Assert.Equal(6, (int)ClaimValueType.BOOLEAN);
    }

    [Fact]
    public void ClaimSourceType_EnumValues_MatchMsCta()
    {
        Assert.Equal(1, (int)ClaimSourceType.AD);
        Assert.Equal(2, (int)ClaimSourceType.Certificate);
        Assert.Equal(4, (int)ClaimSourceType.TransformPolicy);
    }

    // ── SID claim entry ────────────────────────────────────────────────

    [Fact]
    public void ClaimEntry_SidValues_CanHoldGroupSids()
    {
        // Group SIDs would be represented as binary SID values
        var sidBytes1 = AccessControlEntry.SerializeSid("S-1-5-21-1-2-3-512");
        var sidBytes2 = AccessControlEntry.SerializeSid("S-1-5-21-1-2-3-513");

        var entry = new ClaimEntry
        {
            Id = "ad://ext/groupSids",
            Type = ClaimValueType.SID,
            SidValues = [sidBytes1, sidBytes2]
        };

        Assert.Equal(2, entry.ValueCount);
        Assert.Equal(ClaimValueType.SID, entry.Type);
    }

    // ── ClaimsBlob transport container ─────────────────────────────────

    [Fact]
    public void ClaimsBlob_Defaults()
    {
        var blob = new ClaimsBlob();

        Assert.Empty(blob.Data);
        Assert.Equal(0u, blob.UncompressedSize);
        Assert.Equal((ushort)0, blob.CompressionFormat);
        Assert.Equal((ushort)0, blob.ReservedType);
        Assert.Equal(0u, blob.ReservedFieldSize);
    }
}
