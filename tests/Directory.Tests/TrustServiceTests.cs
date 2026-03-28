using Directory.Kerberos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Directory.Tests;

public class TrustServiceTests
{
    private readonly InMemoryDirectoryStore _store;
    private readonly TrustedRealmService _service;

    public TrustServiceTests()
    {
        _store = new InMemoryDirectoryStore();
        var options = Options.Create(new KerberosOptions { DefaultRealm = "CORP.COM" });
        _service = new TrustedRealmService(_store, options, NullLogger<TrustedRealmService>.Instance);
    }

    [Fact]
    public async Task FindTrust_ByDnsName_ReturnsTrust()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var result = _service.FindTrust("PARTNER.COM");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("PARTNER.COM", result.TargetRealm);
    }

    [Fact]
    public async Task FindTrust_ByNetBiosName_ReturnsTrust()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var result = _service.FindTrust("PARTNER");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("PARTNER.COM", result.TargetRealm);
    }

    [Fact]
    public async Task FindTrust_CaseInsensitive_ReturnsTrust()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var result = _service.FindTrust("partner.com");

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void FindTrust_NonExistent_ReturnsNull()
    {
        var result = _service.FindTrust("UNKNOWN.COM");
        Assert.Null(result);
    }

    [Fact]
    public async Task HasTrust_Exists_ReturnsTrue()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act & Assert
        Assert.True(_service.HasTrust("PARTNER.COM"));
    }

    [Fact]
    public void HasTrust_NotExists_ReturnsFalse()
    {
        Assert.False(_service.HasTrust("UNKNOWN.COM"));
    }

    [Fact]
    public async Task VerifyTrust_InboundOnly_CannotIssueOutboundReferrals()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Inbound, TrustType.External);
        await _service.AddTrustAsync(trust);

        // Act
        var result = _service.VerifyTrust("PARTNER.COM");

        // Assert
        Assert.True(result.IsOperational);
        Assert.Equal(TrustDirection.Inbound, result.Direction);
    }

    [Fact]
    public async Task ComputeTrustPath_DirectTrust_ReturnsSingleHop()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Outbound, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var path = _service.ComputeTrustPath("PARTNER.COM");

        // Assert
        Assert.NotNull(path);
        Assert.Single(path);
        Assert.Equal("PARTNER.COM", path[0]);
    }

    [Fact]
    public async Task ComputeTrustPath_TransitiveForestTrust_CanReachChildDomain()
    {
        // Arrange: CORP.COM trusts PARTNER.COM (forest trust, transitive, outbound)
        // and CHILD.PARTNER.COM is a child of PARTNER.COM (shares suffix)
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Outbound, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var path = _service.ComputeTrustPath("CHILD.PARTNER.COM");

        // Assert
        Assert.NotNull(path);
        Assert.Contains("PARTNER.COM", path);
        Assert.Contains("CHILD.PARTNER.COM", path);
    }

    [Fact]
    public async Task ComputeTrustPath_NonTransitiveTrust_BlocksPath()
    {
        // Arrange: External trust with NonTransitive attribute
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Outbound, TrustType.External,
            TrustAttributes.NonTransitive);
        await _service.AddTrustAsync(trust);

        // Act — trying to reach a child of the partner through non-transitive trust
        var path = _service.ComputeTrustPath("CHILD.PARTNER.COM");

        // Assert — non-transitive trust should not enable transitive path
        Assert.Null(path);
    }

    [Fact]
    public async Task ComputeTrustPath_InboundOnly_CannotRefer()
    {
        // Arrange: Inbound-only trust means the partner trusts us, but we cannot refer to them
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Inbound, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var path = _service.ComputeTrustPath("PARTNER.COM");

        // Assert — inbound trust means we cannot issue outbound referrals
        Assert.Null(path);
    }

    [Fact]
    public async Task ComputeTrustPath_BidirectionalTrust_AllowsBothDirections()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest);
        await _service.AddTrustAsync(trust);

        // Act
        var path = _service.ComputeTrustPath("PARTNER.COM");

        // Assert
        Assert.NotNull(path);
        Assert.Single(path);
    }

    [Fact]
    public void DeriveInterRealmKey_ProducesConsistentKeys()
    {
        // Arrange & Act
        var key1 = TrustedRealmService.DeriveInterRealmKey("SharedSecret123", "CORP.COM", "PARTNER.COM");
        var key2 = TrustedRealmService.DeriveInterRealmKey("SharedSecret123", "CORP.COM", "PARTNER.COM");

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal(32, key1.Length); // AES-256 key length
    }

    [Fact]
    public void DeriveInterRealmKey_DifferentSecrets_ProduceDifferentKeys()
    {
        // Arrange & Act
        var key1 = TrustedRealmService.DeriveInterRealmKey("Secret1", "CORP.COM", "PARTNER.COM");
        var key2 = TrustedRealmService.DeriveInterRealmKey("Secret2", "CORP.COM", "PARTNER.COM");

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveInterRealmKey_DifferentRealms_ProduceDifferentKeys()
    {
        // Arrange & Act
        var key1 = TrustedRealmService.DeriveInterRealmKey("SharedSecret", "CORP.COM", "PARTNER.COM");
        var key2 = TrustedRealmService.DeriveInterRealmKey("SharedSecret", "CORP.COM", "OTHER.COM");

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public async Task GetTrusts_ReturnsAllTrusts()
    {
        // Arrange
        await _service.AddTrustAsync(CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest));
        await _service.AddTrustAsync(CreateTrust("OTHER.COM", "OTHER", TrustDirection.Outbound, TrustType.External));

        // Act
        var trusts = _service.GetTrusts();

        // Assert
        Assert.Equal(2, trusts.Count);
    }

    [Fact]
    public async Task RemoveTrustAsync_RemovesTrust()
    {
        // Arrange
        await _service.AddTrustAsync(CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Bidirectional, TrustType.Forest));

        // Act
        var removed = await _service.RemoveTrustAsync("PARTNER.COM");

        // Assert
        Assert.True(removed);
        Assert.Null(_service.FindTrust("PARTNER.COM"));
    }

    [Fact]
    public async Task VerifyTrust_DisabledTrust_ReportsNotOperational()
    {
        // Arrange
        var trust = CreateTrust("PARTNER.COM", "PARTNER", TrustDirection.Disabled, TrustType.External);
        await _service.AddTrustAsync(trust);

        // Act
        var result = _service.VerifyTrust("PARTNER.COM");

        // Assert
        Assert.False(result.IsOperational);
    }

    [Fact]
    public async Task VerifyTrust_NoTrustKey_ReportsNotOperational()
    {
        // Arrange
        var trust = new TrustRelationship
        {
            SourceRealm = "CORP.COM",
            TargetRealm = "PARTNER.COM",
            FlatName = "PARTNER",
            Direction = TrustDirection.Bidirectional,
            TrustType = TrustType.Forest,
            TrustKey = null, // No key
        };
        await _service.AddTrustAsync(trust);

        // Act
        var result = _service.VerifyTrust("PARTNER.COM");

        // Assert
        Assert.False(result.IsOperational);
        Assert.Contains("key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyTrust_NonExistent_ReportsNotFound()
    {
        var result = _service.VerifyTrust("UNKNOWN.COM");
        Assert.False(result.IsOperational);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TrustRelationship CreateTrust(
        string targetRealm,
        string flatName,
        TrustDirection direction,
        TrustType trustType,
        TrustAttributes attributes = TrustAttributes.None)
    {
        return new TrustRelationship
        {
            SourceRealm = "CORP.COM",
            TargetRealm = targetRealm,
            FlatName = flatName,
            Direction = direction,
            TrustType = trustType,
            TrustAttributes = attributes,
            TrustKey = new byte[32], // Placeholder key
            SecurityIdentifier = "S-1-5-21-999999999-999999999-999999999",
        };
    }
}
