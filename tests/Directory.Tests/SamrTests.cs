using Directory.Rpc.Ndr;
using Directory.Rpc.Samr;
using Xunit;

namespace Directory.Tests;

public class SamrSidTests
{
    // ────────────────────────────────────────────────────────────────
    // SID construction and NDR round-trip via NdrWriter / NdrReader
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-1013")]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-0-0")]
    public void NdrRoundTrip_WriteThenRead_ReturnsSameSid(string sid)
    {
        // Arrange
        var writer = new NdrWriter();
        writer.WriteRpcSid(sid);
        var bytes = writer.ToArray();

        // Act
        var reader = new NdrReader(bytes);
        var parsed = reader.ReadRpcSid();

        // Assert
        Assert.Equal(sid, parsed);
    }

    [Fact]
    public void NdrWrite_DomainSid_ProducesNonEmptyBytes()
    {
        var writer = new NdrWriter();
        writer.WriteRpcSid("S-1-5-21-3623811015-3361044348-30300820");
        var bytes = writer.ToArray();

        Assert.NotEmpty(bytes);
        // Revision (1 byte) + SubAuthCount (1 byte) + Authority (6 bytes) + padding + 4 subauths (4 bytes each)
        Assert.True(bytes.Length >= 8 + 16);
    }

    // ────────────────────────────────────────────────────────────────
    // RID extraction from SID string
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-500", 500u)]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-501", 501u)]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-502", 502u)]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-512", 512u)]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-513", 513u)]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-1013", 1013u)]
    public void ExtractRid_FromSid_ReturnsLastSubAuthority(string sid, uint expectedRid)
    {
        // Act
        var lastDash = sid.LastIndexOf('-');
        uint rid = uint.Parse(sid[(lastDash + 1)..]);

        // Assert
        Assert.Equal(expectedRid, rid);
    }

    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-500", "S-1-5-21-3623811015-3361044348-30300820")]
    [InlineData("S-1-5-32-544", "S-1-5-32")]
    public void ExtractDomainSid_FromSid_ReturnsWithoutRid(string sid, string expectedDomainSid)
    {
        var lastDash = sid.LastIndexOf('-');
        var domainSid = sid[..lastDash];

        Assert.Equal(expectedDomainSid, domainSid);
    }

    // ────────────────────────────────────────────────────────────────
    // Domain SID + RID composition
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820", 500u, "S-1-5-21-3623811015-3361044348-30300820-500")]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820", 512u, "S-1-5-21-3623811015-3361044348-30300820-512")]
    [InlineData("S-1-5-32", 544u, "S-1-5-32-544")]
    public void ComposeSid_DomainSidPlusRid_ProducesExpectedSid(string domainSid, uint rid, string expected)
    {
        var composed = $"{domainSid}-{rid}";
        Assert.Equal(expected, composed);
    }

    // ────────────────────────────────────────────────────────────────
    // Well-known RID mapping
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void WellKnownRids_Administrator_Is500()
    {
        Assert.Equal(500u, ExtractRid("S-1-5-21-0-0-0-500"));
    }

    [Fact]
    public void WellKnownRids_Guest_Is501()
    {
        Assert.Equal(501u, ExtractRid("S-1-5-21-0-0-0-501"));
    }

    [Fact]
    public void WellKnownRids_Krbtgt_Is502()
    {
        Assert.Equal(502u, ExtractRid("S-1-5-21-0-0-0-502"));
    }

    [Fact]
    public void WellKnownRids_DomainAdmins_Is512()
    {
        Assert.Equal(512u, ExtractRid("S-1-5-21-0-0-0-512"));
    }

    [Fact]
    public void WellKnownRids_DomainUsers_Is513()
    {
        Assert.Equal(513u, ExtractRid("S-1-5-21-0-0-0-513"));
    }

    [Fact]
    public void WellKnownRids_DomainComputers_Is515()
    {
        Assert.Equal(515u, ExtractRid("S-1-5-21-0-0-0-515"));
    }

    [Fact]
    public void WellKnownRids_DomainControllers_Is516()
    {
        Assert.Equal(516u, ExtractRid("S-1-5-21-0-0-0-516"));
    }

    [Fact]
    public void SamrConstants_RidDomainUsers_Is513()
    {
        Assert.Equal(513u, SamrConstants.RidDomainUsers);
    }

    [Fact]
    public void SamrConstants_RidDomainComputers_Is515()
    {
        Assert.Equal(515u, SamrConstants.RidDomainComputers);
    }

    // ────────────────────────────────────────────────────────────────
    // SID parsing — S-1-5-21-... format validation
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-500", 1, 5)]
    [InlineData("S-1-1-0", 1, 1)]
    [InlineData("S-1-0-0", 1, 0)]
    public void ParseSid_ExtractsRevisionAndAuthority(string sid, byte expectedRevision, long expectedAuthority)
    {
        var parts = sid.Split('-');
        var revision = byte.Parse(parts[1]);
        var authority = long.Parse(parts[2]);

        Assert.Equal(expectedRevision, revision);
        Assert.Equal(expectedAuthority, authority);
    }

    [Theory]
    [InlineData("S-1-5-21-3623811015-3361044348-30300820-500", 5)]
    [InlineData("S-1-5-32-544", 2)]
    [InlineData("S-1-5-18", 1)]
    [InlineData("S-1-1-0", 1)]
    public void ParseSid_SubAuthorityCount_IsCorrect(string sid, int expectedCount)
    {
        var parts = sid.Split('-');
        int subAuthCount = parts.Length - 3;

        Assert.Equal(expectedCount, subAuthCount);
    }

    // ────────────────────────────────────────────────────────────────
    // SAM account name validation (max 20 chars, restricted chars)
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("john.doe", true)]
    [InlineData("MACHINE$", true)]
    [InlineData("ab", true)]
    [InlineData("abcdefghijklmnopqrst", true)]   // exactly 20 chars
    [InlineData("abcdefghijklmnopqrstu", false)]  // 21 chars — too long
    [InlineData("", false)]
    public void ValidateSamAccountName_LengthCheck(string name, bool expected)
    {
        bool isValid = !string.IsNullOrEmpty(name) && name.Length <= 20;
        Assert.Equal(expected, isValid);
    }

    [Theory]
    [InlineData("user/name", false)]
    [InlineData("user\\name", false)]
    [InlineData("user[name", false)]
    [InlineData("user]name", false)]
    [InlineData("user:name", false)]
    [InlineData("user|name", false)]
    [InlineData("user<name", false)]
    [InlineData("user>name", false)]
    [InlineData("user+name", false)]
    [InlineData("user=name", false)]
    [InlineData("user;name", false)]
    [InlineData("user,name", false)]
    [InlineData("user\"name", false)]
    [InlineData("validname", true)]
    [InlineData("COMPUTER$", true)]
    public void ValidateSamAccountName_SpecialCharsRejected(string name, bool expected)
    {
        // Per MS-SAMR, these characters are forbidden in sAMAccountName
        char[] forbidden = ['/', '\\', '[', ']', ':', '|', '<', '>', '+', '=', ';', ',', '"'];
        bool isValid = !string.IsNullOrEmpty(name)
            && name.Length <= 20
            && !name.Any(c => forbidden.Contains(c));

        Assert.Equal(expected, isValid);
    }

    // ────────────────────────────────────────────────────────────────
    // SAMR constants
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SamrConstants_InterfaceId_MatchesMsSamr()
    {
        Assert.Equal(new Guid("12345778-1234-abcd-ef00-0123456789ac"), SamrConstants.InterfaceId);
    }

    [Fact]
    public void SamrConstants_StatusSuccess_IsZero()
    {
        Assert.Equal(0u, SamrConstants.StatusSuccess);
    }

    [Fact]
    public void SamrConstants_UfNormalAccount_Is0x200()
    {
        Assert.Equal(0x00000200u, SamrConstants.UfNormalAccount);
    }

    [Fact]
    public void SamrConstants_UfWorkstationTrustAccount_Is0x1000()
    {
        Assert.Equal(0x00001000u, SamrConstants.UfWorkstationTrustAccount);
    }

    [Fact]
    public void SamrConstants_UfServerTrustAccount_Is0x2000()
    {
        Assert.Equal(0x00002000u, SamrConstants.UfServerTrustAccount);
    }

    // ────────────────────────────────────────────────────────────────
    // NdrWriter / NdrReader basic round-trip for primitives
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NdrRoundTrip_UInt32_ReturnsOriginal()
    {
        var writer = new NdrWriter();
        writer.WriteUInt32(0xDEADBEEF);
        var reader = new NdrReader(writer.ToArray());
        Assert.Equal(0xDEADBEEFu, reader.ReadUInt32());
    }

    [Fact]
    public void NdrRoundTrip_Bytes_ReturnsOriginal()
    {
        var original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var writer = new NdrWriter();
        writer.WriteBytes(original);
        var reader = new NdrReader(writer.ToArray());
        var result = reader.ReadBytes(8).ToArray();
        Assert.Equal(original, result);
    }

    [Fact]
    public void NdrRoundTrip_ContextHandle_ReturnsOriginal()
    {
        var guid = Guid.NewGuid();
        var writer = new NdrWriter();
        writer.WriteContextHandle(42, guid);
        var reader = new NdrReader(writer.ToArray());
        var (attr, parsedGuid) = reader.ReadContextHandle();
        Assert.Equal(42u, attr);
        Assert.Equal(guid, parsedGuid);
    }

    // ────────────────────────────────────────────────────────────────
    // Helper
    // ────────────────────────────────────────────────────────────────

    private static uint ExtractRid(string sid)
    {
        var lastDash = sid.LastIndexOf('-');
        return lastDash > 0 && uint.TryParse(sid[(lastDash + 1)..], out var rid) ? rid : 0;
    }
}
