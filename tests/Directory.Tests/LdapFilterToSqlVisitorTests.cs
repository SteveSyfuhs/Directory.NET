using Directory.Core.Interfaces;
using Directory.CosmosDb;
using Xunit;

namespace Directory.Tests;

public class LdapFilterToSqlVisitorTests
{
    [Fact]
    public void GenerateSql_SimpleEquality_ProducesWhereClause()
    {
        // Arrange
        var filter = new EqualityFilterNode("cn", "John");

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Equal("c.cn = @p0", sql);
        Assert.Single(parameters);
        Assert.Equal("@p0", parameters[0].Name);
        Assert.Equal("John", parameters[0].Value);
    }

    [Fact]
    public void GenerateSql_AndFilter_ProducesSqlAnd()
    {
        // Arrange
        var filter = new AndFilterNode([
            new EqualityFilterNode("cn", "John"),
            new EqualityFilterNode("sn", "Doe"),
        ]);

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("AND", sql);
        Assert.Equal(2, parameters.Count);
    }

    [Fact]
    public void GenerateSql_OrFilter_ProducesSqlOr()
    {
        // Arrange
        var filter = new OrFilterNode([
            new EqualityFilterNode("cn", "Alice"),
            new EqualityFilterNode("cn", "Bob"),
        ]);

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("OR", sql);
        Assert.Equal(2, parameters.Count);
    }

    [Fact]
    public void GenerateSql_NotFilter_ProducesSqlNot()
    {
        // Arrange
        var filter = new NotFilterNode(new EqualityFilterNode("cn", "John"));

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.StartsWith("NOT (", sql);
        Assert.Single(parameters);
    }

    [Fact]
    public void GenerateSql_SubstringInitialOnly_ProducesStartsWith()
    {
        // Arrange
        var filter = new SubstringFilterNode("cn", "Jo", [], null);

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("STARTSWITH", sql);
        Assert.Single(parameters);
        Assert.Equal("Jo", parameters[0].Value);
    }

    [Fact]
    public void GenerateSql_SubstringFinalOnly_ProducesEndsWith()
    {
        // Arrange
        var filter = new SubstringFilterNode("cn", null, [], "son");

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("ENDSWITH", sql);
        Assert.Single(parameters);
        Assert.Equal("son", parameters[0].Value);
    }

    [Fact]
    public void GenerateSql_SubstringAnyMiddle_ProducesContains()
    {
        // Arrange
        var filter = new SubstringFilterNode("cn", null, ["ohn"], null);

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("CONTAINS", sql);
        Assert.Single(parameters);
    }

    [Fact]
    public void GenerateSql_PresenceFilter_ProducesIsDefined()
    {
        // Arrange
        var filter = new PresenceFilterNode("mail");

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("IS_DEFINED", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void GenerateSql_PresenceObjectClass_ProducesTrue()
    {
        // Arrange — objectClass=* is special-cased to "true" (always present)
        var filter = new PresenceFilterNode("objectClass");

        // Act
        var (sql, _) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Equal("true", sql);
    }

    [Fact]
    public void GenerateSql_ArrayAttribute_ProducesArrayContains()
    {
        // Arrange — objectClass is an array attribute
        var filter = new EqualityFilterNode("objectClass", "user");

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("ARRAY_CONTAINS", sql);
        Assert.Contains("c.objectClass", sql);
        Assert.Single(parameters);
        Assert.Equal("user", parameters[0].Value);
    }

    [Fact]
    public void GenerateSql_CustomAttribute_UsesAttributesDictPath()
    {
        // Arrange — unknown attributes map to c.attributes.<name>
        var filter = new EqualityFilterNode("myCustomAttr", "value1");

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains("c.attributes.myCustomAttr", sql);
        Assert.Contains("ARRAY_CONTAINS", sql); // attributes dict uses .Values array
        Assert.Single(parameters);
    }

    [Fact]
    public void GenerateSql_GreaterOrEqual_ProducesCorrectOperator()
    {
        // Arrange
        var filter = new GreaterOrEqualFilterNode("whenCreated", "20240101000000.0Z");

        // Act
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // Assert
        Assert.Contains(">=", sql);
        Assert.Contains("c.whenCreated", sql);
        Assert.Equal("20240101000000.0Z", parameters[0].Value);
    }

    // ── Edge case: empty AND/OR nodes ────────────────────────────────────────

    [Fact]
    public void GenerateSql_EmptyAndNode_ProducesTrue()
    {
        // An AND with no children is vacuously true (identity element for conjunction)
        var filter = new AndFilterNode([]);
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal("true", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void GenerateSql_EmptyOrNode_ProducesFalse()
    {
        // An OR with no children is vacuously false (identity element for disjunction)
        var filter = new OrFilterNode([]);
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal("false", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void GenerateSql_SingleChildAndNode_UnwrapsWithoutAnd()
    {
        // An AND with exactly one child should reduce to the child expression directly
        var filter = new AndFilterNode([new EqualityFilterNode("cn", "Solo")]);
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.DoesNotContain("AND", sql);
        Assert.Contains("c.cn", sql);
        Assert.Single(parameters);
    }

    [Fact]
    public void GenerateSql_SingleChildOrNode_UnwrapsWithoutOr()
    {
        var filter = new OrFilterNode([new EqualityFilterNode("cn", "Solo")]);
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.DoesNotContain("OR", sql);
        Assert.Contains("c.cn", sql);
        Assert.Single(parameters);
    }

    // ── Edge case: deeply nested filters ────────────────────────────────────

    [Fact]
    public void GenerateSql_FiveLevelDeepNesting_ProducesNestedClauses()
    {
        // Build: (&(&(&(&(cn=A)(sn=B))(mail=C))(objectClass=user))(description=D))
        FilterNode filter =
            new AndFilterNode([
                new AndFilterNode([
                    new AndFilterNode([
                        new AndFilterNode([
                            new EqualityFilterNode("cn", "A"),
                            new EqualityFilterNode("sn", "B"),
                        ]),
                        new EqualityFilterNode("mail", "C"),
                    ]),
                    new EqualityFilterNode("objectClass", "user"),
                ]),
                new EqualityFilterNode("description", "D"),
            ]);

        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Contains("AND", sql);
        Assert.Equal(5, parameters.Count);
    }

    // ── Edge case: extensible match filter ───────────────────────────────────

    [Fact]
    public void GenerateSql_ExtensibleMatchWithAttribute_TreatsAsEquality()
    {
        // (userAccountControl:1.2.840.113556.1.4.803:=2) — bitwise AND matching rule
        // The visitor falls back to equality on the attribute
        var filter = new ExtensibleMatchFilterNode(
            matchingRule: "1.2.840.113556.1.4.803",
            attribute: "userAccountControl",
            value: "2",
            dnAttributes: false);

        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.NotNull(sql);
        Assert.NotEmpty(sql);
        // Should contain the attribute path and the parameter
        Assert.Single(parameters);
    }

    [Fact]
    public void GenerateSql_ExtensibleMatchWithoutAttribute_ProducesTrue()
    {
        // When no attribute is supplied the visitor returns "true" (filter in memory)
        var filter = new ExtensibleMatchFilterNode(
            matchingRule: "1.2.840.113556.1.4.803",
            attribute: null,
            value: "2",
            dnAttributes: false);

        var (sql, _) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal("true", sql);
    }

    // ── Edge case: substring with only "any" components ─────────────────────

    [Fact]
    public void GenerateSql_SubstringOnlyAnyComponents_ProducesContainsForEach()
    {
        // Filter: (cn=*foo*bar*) — initial=null, any=["foo","bar"], final=null
        var filter = new SubstringFilterNode("cn", null, ["foo", "bar"], null);

        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Contains("CONTAINS", sql);
        Assert.Equal(2, parameters.Count);
        Assert.Equal("foo", parameters[0].Value);
        Assert.Equal("bar", parameters[1].Value);
    }

    // ── Edge case: binary-escaped values in equality filters ─────────────────

    [Fact]
    public void GenerateSql_ObjectSidBinaryEscaped_NormalisesToStringSid()
    {
        // Build the binary-escaped representation of a well-known SID S-1-5-18 (SYSTEM)
        // Binary: 01 01 00 00 00 00 00 05 12 00 00 00
        var binaryEscaped = "\\01\\01\\00\\00\\00\\00\\00\\05\\12\\00\\00\\00";

        var filter = new EqualityFilterNode("objectSid", binaryEscaped);
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // The visitor should have decoded the binary and the parameter value should
        // be the canonical string SID, not the raw escaped bytes
        var paramValue = parameters[0].Value?.ToString() ?? "";
        Assert.StartsWith("S-", paramValue);
    }

    [Fact]
    public void GenerateSql_ObjectSidStringForm_PassesThroughUnchanged()
    {
        var stringSid = "S-1-5-21-3623811015-3361044348-30300820-500";
        var filter = new EqualityFilterNode("objectSid", stringSid);
        var (_, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal(stringSid, parameters[0].Value);
    }

    // ── Edge case: special characters in filter values ───────────────────────

    [Fact]
    public void GenerateSql_ValueWithParentheses_PassedAsParameterValue()
    {
        // The LDAP filter parser decodes \28/\29 to literal parens before reaching the visitor.
        // The visitor must pass the decoded value through unchanged.
        var filter = new EqualityFilterNode("cn", "John (Jr.)");
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal("John (Jr.)", parameters[0].Value);
        Assert.Contains("@p0", sql);
    }

    [Fact]
    public void GenerateSql_ValueWithAsterisk_PassedAsParameterValue()
    {
        // An equality filter whose value contains a literal asterisk (already decoded from \2a)
        var filter = new EqualityFilterNode("description", "Tier*1 admin");
        var (_, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal("Tier*1 admin", parameters[0].Value);
    }

    [Fact]
    public void GenerateSql_ValueWithBackslash_PassedAsParameterValue()
    {
        // Literal backslash decoded from \5c
        var filter = new EqualityFilterNode("description", @"Domain\Admins");
        var (_, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Equal(@"Domain\Admins", parameters[0].Value);
    }

    // ── Edge case: LessOrEqual comparison ────────────────────────────────────

    [Fact]
    public void GenerateSql_LessOrEqual_ProducesCorrectOperator()
    {
        var filter = new LessOrEqualFilterNode("usnChanged", "9999");
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        Assert.Contains("<=", sql);
        Assert.Contains("c.usnChanged", sql);
        Assert.Equal("9999", parameters[0].Value);
    }

    // ── Edge case: approxMatch falls back to equality ───────────────────────

    [Fact]
    public void GenerateSql_ApproxMatch_TreatsAsEquality()
    {
        var filter = new ApproxMatchFilterNode("cn", "Johnson");
        var (sql, parameters) = LdapFilterToSqlVisitor.GenerateSql(filter);

        // ApproxMatch maps to equality in the visitor
        Assert.Contains("c.cn", sql);
        Assert.Single(parameters);
        Assert.Equal("Johnson", parameters[0].Value);
    }
}
