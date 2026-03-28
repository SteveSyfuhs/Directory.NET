using Xunit;
using Directory.Ldap.Client;

namespace Directory.Tests;

/// <summary>
/// Tests for the LDAP client models: LdapSearchEntry, LdapSearchResult,
/// LdapModification, LdapBindResult, LdapResult, and LdapClient disposal.
/// </summary>
public class LdapClientTests
{
    // ════════════════════════════════════════════════════════════════
    //  1. LdapSearchEntry Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LdapSearchEntry_GetFirstValue_ReturnsValue()
    {
        var entry = new LdapSearchEntry
        {
            DistinguishedName = "CN=TestUser,DC=corp,DC=com",
            Attributes = new Dictionary<string, List<string>>
            {
                ["cn"] = ["TestUser"],
                ["sAMAccountName"] = ["testuser"],
            }
        };

        Assert.Equal("TestUser", entry.GetFirstValue("cn"));
        Assert.Equal("testuser", entry.GetFirstValue("sAMAccountName"));
    }

    [Fact]
    public void LdapSearchEntry_GetFirstValue_MissingAttribute_ReturnsNull()
    {
        var entry = new LdapSearchEntry
        {
            DistinguishedName = "CN=TestUser,DC=corp,DC=com",
            Attributes = new Dictionary<string, List<string>>
            {
                ["cn"] = ["TestUser"],
            }
        };

        Assert.Null(entry.GetFirstValue("mail"));
        Assert.Null(entry.GetFirstValue("nonExistentAttribute"));
    }

    [Fact]
    public void LdapSearchResult_EmptyEntries_HasZeroCount()
    {
        var result = new LdapSearchResult();

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.ResultCode);
        Assert.Equal("", result.DiagnosticMessage);
    }

    [Fact]
    public void LdapSearchEntry_MultipleValues_AllAccessible()
    {
        var entry = new LdapSearchEntry
        {
            DistinguishedName = "CN=TestGroup,DC=corp,DC=com",
            Attributes = new Dictionary<string, List<string>>
            {
                ["member"] = [
                    "CN=User1,DC=corp,DC=com",
                    "CN=User2,DC=corp,DC=com",
                    "CN=User3,DC=corp,DC=com",
                ],
            }
        };

        Assert.Equal(3, entry.Attributes["member"].Count);
        Assert.Equal("CN=User1,DC=corp,DC=com", entry.GetFirstValue("member"));
        Assert.Equal("CN=User2,DC=corp,DC=com", entry.Attributes["member"][1]);
        Assert.Equal("CN=User3,DC=corp,DC=com", entry.Attributes["member"][2]);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. LdapModification Enum Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LdapModification_AddOperation_HasCorrectEnum()
    {
        Assert.Equal(0, (int)LdapModOperation.Add);
    }

    [Fact]
    public void LdapModification_DeleteOperation_HasCorrectEnum()
    {
        Assert.Equal(1, (int)LdapModOperation.Delete);
    }

    [Fact]
    public void LdapModification_ReplaceOperation_HasCorrectEnum()
    {
        Assert.Equal(2, (int)LdapModOperation.Replace);
    }

    // ════════════════════════════════════════════════════════════════
    //  3. LdapBindResult and LdapResult Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LdapBindResult_Success_Properties()
    {
        var result = new LdapBindResult
        {
            Success = true,
            ResultCode = 0,
            DiagnosticMessage = "",
        };

        Assert.True(result.Success);
        Assert.Equal(0, result.ResultCode);
        Assert.Equal("", result.DiagnosticMessage);
    }

    [Fact]
    public void LdapResult_Failure_Properties()
    {
        var result = new LdapResult
        {
            Success = false,
            ResultCode = 49,
            MatchedDn = "DC=corp,DC=com",
            DiagnosticMessage = "Invalid credentials",
        };

        Assert.False(result.Success);
        Assert.Equal(49, result.ResultCode);
        Assert.Equal("DC=corp,DC=com", result.MatchedDn);
        Assert.Equal("Invalid credentials", result.DiagnosticMessage);
    }

    // ════════════════════════════════════════════════════════════════
    //  4. LdapClient Dispose Safety Test
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LdapClient_NotConnected_DisposeDoesNotThrow()
    {
        // Create an LdapClient and dispose without connecting — should not throw
        var client = new LdapClient();
        await client.DisposeAsync();
    }
}
