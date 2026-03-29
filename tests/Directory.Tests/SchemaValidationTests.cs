using Directory.Core.Models;
using Directory.Schema;
using Xunit;

namespace Directory.Tests;

public class SchemaValidationTests
{
    private readonly SchemaService _schema = new();

    [Fact]
    public void ValidateUser_WithRequiredAttributes_Succeeds()
    {
        var user = new DirectoryObject
        {
            DistinguishedName = "CN=TestUser,OU=Users,DC=directory,DC=local",
            ObjectClass = ["top", "person", "organizationalPerson", "user"],
            ObjectCategory = "user",
            Cn = "TestUser",
        };

        var isValid = _schema.ValidateObject(user, out var errors);
        Assert.True(isValid, string.Join("; ", errors));
    }

    [Fact]
    public void ValidateUser_MissingCn_Fails()
    {
        var user = new DirectoryObject
        {
            DistinguishedName = "CN=TestUser,OU=Users,DC=directory,DC=local",
            ObjectClass = ["top", "person"],
            ObjectCategory = "person",
        };

        var isValid = _schema.ValidateObject(user, out var errors);
        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("cn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAttribute_ReturnsSchemaEntry()
    {
        var attr = _schema.GetAttribute("sAMAccountName");
        Assert.NotNull(attr);
        Assert.True(attr.IsSingleValued);
        Assert.True(attr.IsIndexed);
        Assert.True(attr.IsInGlobalCatalog);
    }

    [Fact]
    public void GetObjectClass_ReturnsEntry()
    {
        var cls = _schema.GetObjectClass("user");
        Assert.NotNull(cls);
        Assert.Equal("organizationalPerson", cls.SuperiorClass);
    }

    [Fact]
    public void GlobalCatalogAttributes_ContainsExpected()
    {
        var gcAttrs = _schema.GetGlobalCatalogAttributes();
        Assert.Contains("sAMAccountName", gcAttrs);
        Assert.Contains("mail", gcAttrs);
        Assert.Contains("objectGUID", gcAttrs);
    }
}

public class DistinguishedNameTests
{
    [Fact]
    public void Parse_SimpleDn_Succeeds()
    {
        var dn = DistinguishedName.Parse("CN=User1,OU=Sales,DC=corp,DC=com");
        Assert.Equal(4, dn.Components.Count);
        Assert.Equal("CN", dn.Components[0].Type);
        Assert.Equal("User1", dn.Components[0].Value);
    }

    [Fact]
    public void Parent_RemovesFirstComponent()
    {
        var dn = DistinguishedName.Parse("CN=User1,OU=Sales,DC=corp,DC=com");
        var parent = dn.Parent();
        Assert.Equal("OU=Sales,DC=corp,DC=com", parent.ToString());
    }

    [Fact]
    public void IsDescendantOf_ReturnsTrue()
    {
        var child = DistinguishedName.Parse("CN=User1,OU=Sales,DC=corp,DC=com");
        var parent = DistinguishedName.Parse("DC=corp,DC=com");
        Assert.True(child.IsDescendantOf(parent));
    }

    [Fact]
    public void IsDescendantOf_ReturnsFalse_ForSameDn()
    {
        var dn = DistinguishedName.Parse("DC=corp,DC=com");
        Assert.False(dn.IsDescendantOf(dn));
    }

    [Fact]
    public void GetDomainDn_ExtractsDcComponents()
    {
        var dn = DistinguishedName.Parse("CN=User1,OU=Sales,DC=corp,DC=com");
        Assert.Equal("DC=corp,DC=com", dn.GetDomainDn());
    }

    [Fact]
    public void GetDomainDnsName_ConvertsToFqdn()
    {
        var dn = DistinguishedName.Parse("CN=User1,OU=Sales,DC=corp,DC=example,DC=com");
        Assert.Equal("corp.example.com", dn.GetDomainDnsName());
    }

    [Fact]
    public void Equality_CaseInsensitive()
    {
        var dn1 = DistinguishedName.Parse("CN=User1,DC=corp,DC=com");
        var dn2 = DistinguishedName.Parse("cn=user1,dc=corp,dc=com");
        Assert.Equal(dn1, dn2);
    }
}

public class LdapFilterParserTests
{
    [Fact]
    public void ParseText_SimpleEquality()
    {
        var filter = Directory.Ldap.Protocol.LdapFilterParser.ParseText("(cn=John)");
        var eq = Assert.IsType<Directory.Core.Interfaces.EqualityFilterNode>(filter);
        Assert.Equal("cn", eq.Attribute);
        Assert.Equal("John", eq.Value);
    }

    [Fact]
    public void ParseText_Presence()
    {
        var filter = Directory.Ldap.Protocol.LdapFilterParser.ParseText("(objectClass=*)");
        var pres = Assert.IsType<Directory.Core.Interfaces.PresenceFilterNode>(filter);
        Assert.Equal("objectClass", pres.Attribute);
    }

    [Fact]
    public void ParseText_And()
    {
        var filter = Directory.Ldap.Protocol.LdapFilterParser.ParseText("(&(objectClass=user)(cn=John))");
        var and = Assert.IsType<Directory.Core.Interfaces.AndFilterNode>(filter);
        Assert.Equal(2, and.Children.Count);
    }

    [Fact]
    public void ParseText_Substring()
    {
        var filter = Directory.Ldap.Protocol.LdapFilterParser.ParseText("(cn=Jo*n)");
        var sub = Assert.IsType<Directory.Core.Interfaces.SubstringFilterNode>(filter);
        Assert.Equal("cn", sub.Attribute);
        Assert.Equal("Jo", sub.Initial);
        Assert.Equal("n", sub.Final);
    }

    [Fact]
    public void ParseText_Not()
    {
        var filter = Directory.Ldap.Protocol.LdapFilterParser.ParseText("(!(cn=John))");
        var not = Assert.IsType<Directory.Core.Interfaces.NotFilterNode>(filter);
        Assert.IsType<Directory.Core.Interfaces.EqualityFilterNode>(not.Child);
    }
}

public class PasswordServiceTests
{
    [Fact]
    public void MeetsComplexityRequirements_ValidPassword()
    {
        var svc = new Directory.Security.PasswordService(null, null);
        Assert.True(svc.MeetsComplexityRequirements("P@ssw0rd!"));
    }

    [Fact]
    public void MeetsComplexityRequirements_TooShort()
    {
        var svc = new Directory.Security.PasswordService(null, null);
        Assert.False(svc.MeetsComplexityRequirements("Ab1!"));
    }

    [Fact]
    public void DeriveKerberosKeys_ProducesTwoKeys()
    {
        var svc = new Directory.Security.PasswordService(null, null);
        var keys = svc.DeriveKerberosKeys("user@REALM", "password", "REALM");
        Assert.Equal(2, keys.Count);
        Assert.Equal(18, keys[0].EncryptionType); // AES256
        Assert.Equal(17, keys[1].EncryptionType); // AES128
    }
}
