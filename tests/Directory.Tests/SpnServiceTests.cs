using Directory.Kerberos;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

public class SpnServiceTests
{
    [Fact]
    public void ParseSpn_HttpWithHost_ReturnsCorrectParts()
    {
        // Arrange & Act
        var (serviceClass, hostname, port, serviceName) = SpnService.ParseSpn("HTTP/web.corp.com");

        // Assert
        Assert.Equal("HTTP", serviceClass);
        Assert.Equal("web.corp.com", hostname);
        Assert.Null(port);
        Assert.Null(serviceName);
    }

    [Fact]
    public void ParseSpn_MSSQLSvcWithPort_ReturnsCorrectParts()
    {
        // Arrange & Act
        var (serviceClass, hostname, port, serviceName) = SpnService.ParseSpn("MSSQLSvc/db.corp.com:1433");

        // Assert
        Assert.Equal("MSSQLSvc", serviceClass);
        Assert.Equal("db.corp.com", hostname);
        Assert.Equal(1433, port);
        Assert.Null(serviceName);
    }

    [Fact]
    public void ParseSpn_LdapWithServiceName_ReturnsCorrectParts()
    {
        // Arrange & Act
        var (serviceClass, hostname, port, serviceName) = SpnService.ParseSpn("ldap/dc1.corp.com/corp.com");

        // Assert
        Assert.Equal("ldap", serviceClass);
        Assert.Equal("dc1.corp.com", hostname);
        Assert.Null(port);
        Assert.Equal("corp.com", serviceName);
    }

    [Fact]
    public void ParseSpn_SingleComponent_ReturnsServiceClassOnly()
    {
        // Arrange & Act
        var (serviceClass, hostname, port, serviceName) = SpnService.ParseSpn("HTTP");

        // Assert
        Assert.Equal("HTTP", serviceClass);
        Assert.Equal("", hostname);
        Assert.Null(port);
        Assert.Null(serviceName);
    }

    [Fact]
    public void Canonicalize_LowercasesHostname()
    {
        // Arrange & Act
        var result = SpnService.Canonicalize("HTTP/WEB.CORP.COM");

        // Assert
        Assert.Equal("HTTP/web.corp.com", result);
    }

    [Fact]
    public void Canonicalize_LowercasesHostnameWithPort()
    {
        // Arrange & Act
        var result = SpnService.Canonicalize("MSSQLSvc/DB.CORP.COM:1433");

        // Assert
        Assert.Equal("MSSQLSvc/db.corp.com:1433", result);
    }

    [Fact]
    public void Canonicalize_PreservesServiceClass()
    {
        // Arrange & Act
        var result = SpnService.Canonicalize("HTTP/web.corp.com");

        // Assert
        Assert.StartsWith("HTTP/", result);
    }

    [Fact]
    public void IsHostAlias_HttpIsAlias_ReturnsTrue()
    {
        Assert.True(SpnService.IsHostAlias("http"));
    }

    [Fact]
    public void IsHostAlias_LdapIsAlias_ReturnsTrue()
    {
        Assert.True(SpnService.IsHostAlias("ldap"));
    }

    [Fact]
    public void IsHostAlias_CaseInsensitive()
    {
        Assert.True(SpnService.IsHostAlias("HTTP"));
        Assert.True(SpnService.IsHostAlias("Ldap"));
    }

    [Fact]
    public void IsHostAlias_UnknownService_ReturnsFalse()
    {
        Assert.False(SpnService.IsHostAlias("MSSQLSvc"));
        Assert.False(SpnService.IsHostAlias("CustomService"));
    }

    [Fact]
    public void ExpandHostSpn_ProducesAllAliases()
    {
        // Arrange & Act
        var expanded = SpnService.ExpandHostSpn("dc1.corp.com").ToList();

        // Assert
        Assert.Contains("http/dc1.corp.com", expanded);
        Assert.Contains("ldap/dc1.corp.com", expanded);
        Assert.Contains("cifs/dc1.corp.com", expanded);
        Assert.Contains("dns/dc1.corp.com", expanded);
        Assert.Contains("gc/dc1.corp.com", expanded);
        Assert.True(expanded.Count > 30); // There are many aliases
    }

    [Fact]
    public async Task ValidateSpnUniquenessAsync_NoConflict_ReturnsTrue()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var service = new SpnService(store, NullLogger<SpnService>.Instance);

        // Act
        var result = await service.ValidateSpnUniquenessAsync("default", "HTTP/web.corp.com");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateSpnUniquenessAsync_DuplicateExists_ReturnsFalse()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var obj = new Core.Models.DirectoryObject
        {
            Id = "cn=web,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=Web,DC=corp,DC=com",
            ObjectClass = ["top", "computer"],
            ObjectCategory = "computer",
            Cn = "Web",
            DomainDn = "DC=corp,DC=com",
            ServicePrincipalName = ["HTTP/web.corp.com"],
        };
        store.Add(obj);
        var service = new SpnService(store, NullLogger<SpnService>.Instance);

        // Act
        var result = await service.ValidateSpnUniquenessAsync("default", "HTTP/web.corp.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateSpnUniquenessAsync_DuplicateOnSameObject_ReturnsTrue()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var obj = new Core.Models.DirectoryObject
        {
            Id = "cn=web,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=Web,DC=corp,DC=com",
            ObjectClass = ["top", "computer"],
            ObjectCategory = "computer",
            Cn = "Web",
            DomainDn = "DC=corp,DC=com",
            ServicePrincipalName = ["HTTP/web.corp.com"],
        };
        store.Add(obj);
        var service = new SpnService(store, NullLogger<SpnService>.Instance);

        // Act — exclude the DN that already owns this SPN
        var result = await service.ValidateSpnUniquenessAsync("default", "HTTP/web.corp.com", "CN=Web,DC=corp,DC=com");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ResolveSpnOwnerAsync_ExactMatch_ReturnsOwner()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var obj = new Core.Models.DirectoryObject
        {
            Id = "cn=web,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=Web,DC=corp,DC=com",
            ObjectClass = ["top", "computer"],
            ObjectCategory = "computer",
            Cn = "Web",
            DomainDn = "DC=corp,DC=com",
            ServicePrincipalName = ["HTTP/web.corp.com"],
        };
        store.Add(obj);
        var service = new SpnService(store, NullLogger<SpnService>.Instance);

        // Act
        var result = await service.ResolveSpnOwnerAsync("default", "HTTP/web.corp.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("CN=Web,DC=corp,DC=com", result.DistinguishedName);
    }

    [Fact]
    public async Task ResolveSpnOwnerAsync_HostAliasFallback_ReturnsOwner()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var obj = new Core.Models.DirectoryObject
        {
            Id = "cn=dc1,dc=corp,dc=com",
            TenantId = "default",
            DistinguishedName = "CN=DC1,DC=corp,DC=com",
            ObjectClass = ["top", "computer"],
            ObjectCategory = "computer",
            Cn = "DC1",
            DomainDn = "DC=corp,DC=com",
            ServicePrincipalName = ["HOST/dc1.corp.com"],
        };
        store.Add(obj);
        var service = new SpnService(store, NullLogger<SpnService>.Instance);

        // Act — ldap is a HOST alias, so it should resolve via HOST/hostname
        var result = await service.ResolveSpnOwnerAsync("default", "ldap/dc1.corp.com");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("CN=DC1,DC=corp,DC=com", result.DistinguishedName);
    }

    [Fact]
    public async Task ResolveSpnOwnerAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var store = new InMemoryDirectoryStore();
        var service = new SpnService(store, NullLogger<SpnService>.Instance);

        // Act
        var result = await service.ResolveSpnOwnerAsync("default", "HTTP/nonexistent.corp.com");

        // Assert
        Assert.Null(result);
    }
}
