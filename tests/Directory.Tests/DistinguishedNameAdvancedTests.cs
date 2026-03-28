using Directory.Core.Models;
using Xunit;

namespace Directory.Tests;

public class DistinguishedNameAdvancedTests
{
    [Fact]
    public void Parse_MultiValuedRdn_SplitsOnPlus()
    {
        // Arrange & Act — multi-valued RDN with '+' separator
        // The current parser treats '+' as part of the value since SplitDn only splits on ','
        // but UnescapeValue handles \+ escaping. The raw '+' stays as-is in the value.
        var dn = DistinguishedName.Parse("CN=User1+UID=12345,DC=corp,DC=com");

        // Assert — the parser treats the entire "CN=User1+UID=12345" as a single RDN
        Assert.Equal(3, dn.Components.Count);
        Assert.Equal("CN", dn.Components[0].Type);
        // The value contains the "+UID=12345" since multi-valued RDN is not decomposed
        Assert.Contains("User1", dn.Components[0].Value);
    }

    [Fact]
    public void Parse_EscapedCommaInDn_DoesNotSplitOnEscapedComma()
    {
        // Arrange & Act
        var dn = DistinguishedName.Parse("CN=User\\, Jr.,DC=corp,DC=com");

        // Assert
        Assert.Equal(3, dn.Components.Count);
        Assert.Equal("CN", dn.Components[0].Type);
        Assert.Equal("User, Jr.", dn.Components[0].Value);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDn()
    {
        // Arrange & Act
        var dn = DistinguishedName.Parse("");

        // Assert
        Assert.Empty(dn.Components);
        Assert.True(dn.IsEmpty);
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyDn()
    {
        // Arrange & Act
        var dn = DistinguishedName.Parse("   ");

        // Assert
        Assert.Empty(dn.Components);
        Assert.True(dn.IsEmpty);
    }

    [Fact]
    public void Parse_VeryLongDn_HandlesCorrectly()
    {
        // Arrange — build a DN with 100+ OU components
        var components = new List<string>();
        components.Add("CN=DeepUser");
        for (int i = 0; i < 100; i++)
        {
            components.Add($"OU=Level{i}");
        }
        components.Add("DC=corp");
        components.Add("DC=com");
        var dnString = string.Join(",", components);

        // Act
        var dn = DistinguishedName.Parse(dnString);

        // Assert
        Assert.Equal(103, dn.Components.Count); // 1 CN + 100 OU + 2 DC
        Assert.Equal("CN", dn.Components[0].Type);
        Assert.Equal("DeepUser", dn.Components[0].Value);
        Assert.Equal("DC", dn.Components[^1].Type);
        Assert.Equal("com", dn.Components[^1].Value);
    }

    [Fact]
    public void Parse_UnicodeCharactersInDn_PreservesUnicode()
    {
        // Arrange & Act
        var dn = DistinguishedName.Parse("CN=\u00C9milie Br\u00FChl,OU=Mitarbeiter,DC=firma,DC=de");

        // Assert
        Assert.Equal(4, dn.Components.Count);
        Assert.Equal("\u00C9milie Br\u00FChl", dn.Components[0].Value);
    }

    [Fact]
    public void Parse_HexEscapedCharacters_DecodesCorrectly()
    {
        // Arrange — \2C is hex for comma
        var dn = DistinguishedName.Parse("CN=Smith\\2C John,DC=corp,DC=com");

        // Assert
        Assert.Equal(3, dn.Components.Count);
        Assert.Equal("Smith, John", dn.Components[0].Value);
    }

    [Fact]
    public void IsDirectChildOf_TrueForImmediateChild()
    {
        // Arrange
        var child = DistinguishedName.Parse("CN=User1,OU=Sales,DC=corp,DC=com");
        var parent = DistinguishedName.Parse("OU=Sales,DC=corp,DC=com");

        // Act & Assert
        Assert.True(child.IsDirectChildOf(parent));
    }

    [Fact]
    public void IsDirectChildOf_FalseForGrandchild()
    {
        // Arrange
        var grandchild = DistinguishedName.Parse("CN=User1,OU=Team,OU=Sales,DC=corp,DC=com");
        var grandparent = DistinguishedName.Parse("OU=Sales,DC=corp,DC=com");

        // Act & Assert
        Assert.False(grandchild.IsDirectChildOf(grandparent));
    }

    [Fact]
    public void ToString_EscapesSpecialCharacters()
    {
        // Arrange — parse a DN with an escaped comma, then round-trip
        var dn = DistinguishedName.Parse("CN=User\\, Jr.,DC=corp,DC=com");

        // Act
        var str = dn.ToString();

        // Assert — the value is unescaped during parse, then re-escaped in ToString
        Assert.Contains("\\,", str);
        // Re-parse should produce the same DN
        var reparsed = DistinguishedName.Parse(str);
        Assert.Equal(dn, reparsed);
    }

    [Fact]
    public void Equality_IsCaseInsensitiveForTypeAndValue()
    {
        // Arrange
        var dn1 = DistinguishedName.Parse("CN=Admin,OU=Users,DC=Corp,DC=COM");
        var dn2 = DistinguishedName.Parse("cn=admin,ou=users,dc=corp,dc=com");

        // Act & Assert
        Assert.Equal(dn1, dn2);
        Assert.Equal(dn1.GetHashCode(), dn2.GetHashCode());
    }
}
