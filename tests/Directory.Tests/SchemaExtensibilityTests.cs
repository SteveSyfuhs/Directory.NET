using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Schema;
using Xunit;

namespace Directory.Tests;

public class SchemaExtensibilityTests
{
    private readonly SchemaService _schema = new();

    [Fact]
    public void RegisterAttribute_CustomAttribute_AppearsInLookup()
    {
        // Arrange
        var entry = new AttributeSchemaEntry
        {
            Name = "myCustomAttr",
            LdapDisplayName = "myCustomAttr",
            Oid = "1.3.6.1.4.1.99999.1.1",
            Syntax = "2.5.5.12",
            IsSingleValued = true,
            IsInGlobalCatalog = false,
        };

        // Act
        var registered = _schema.RegisterAttribute(entry);

        // Assert
        Assert.True(registered);
        var retrieved = _schema.GetAttribute("myCustomAttr");
        Assert.NotNull(retrieved);
        Assert.Equal("1.3.6.1.4.1.99999.1.1", retrieved.Oid);
        Assert.True(retrieved.IsSingleValued);
        Assert.False(retrieved.IsBuiltIn);
    }

    [Fact]
    public void RegisterAttribute_DuplicateName_ReturnsFalse()
    {
        // Arrange — "cn" is a built-in attribute
        var entry = new AttributeSchemaEntry
        {
            Name = "cn",
            LdapDisplayName = "cn",
            Oid = "1.3.6.1.4.1.99999.1.999",
            Syntax = "2.5.5.12",
        };

        // Act
        var registered = _schema.RegisterAttribute(entry);

        // Assert
        Assert.False(registered);
    }

    [Fact]
    public void RegisterObjectClass_CustomClass_AppearsInLookup()
    {
        // Arrange
        var entry = new ObjectClassSchemaEntry
        {
            Name = "myCustomClass",
            LdapDisplayName = "myCustomClass",
            Oid = "1.3.6.1.4.1.99999.2.1",
            SuperiorClass = "top",
            ClassType = ObjectClassType.Structural,
            MustHaveAttributes = ["cn"],
            MayHaveAttributes = [],
        };

        // Act
        var registered = _schema.RegisterObjectClass(entry);

        // Assert
        Assert.True(registered);
        var retrieved = _schema.GetObjectClass("myCustomClass");
        Assert.NotNull(retrieved);
        Assert.Equal("top", retrieved.SuperiorClass);
        Assert.False(retrieved.IsBuiltIn);
    }

    [Fact]
    public void RegisterObjectClass_DuplicateOid_ReturnsFalse()
    {
        // Arrange — register one class first
        var first = new ObjectClassSchemaEntry
        {
            Name = "classA",
            LdapDisplayName = "classA",
            Oid = "1.3.6.1.4.1.99999.2.50",
            SuperiorClass = "top",
            ClassType = ObjectClassType.Structural,
        };
        _schema.RegisterObjectClass(first);

        // Try to register another class with the same OID
        var second = new ObjectClassSchemaEntry
        {
            Name = "classB",
            LdapDisplayName = "classB",
            Oid = "1.3.6.1.4.1.99999.2.50", // same OID
            SuperiorClass = "top",
            ClassType = ObjectClassType.Structural,
        };

        // Act
        var registered = _schema.RegisterObjectClass(second);

        // Assert
        Assert.False(registered);
    }

    [Fact]
    public void ValidateAttributeSchema_InvalidSyntaxOid_ReturnsError()
    {
        // Arrange
        var modSvc = new SchemaModificationService(_schema, NullStore.Instance, new NullModLogger());
        var entry = new AttributeSchemaEntry
        {
            LdapDisplayName = "badSyntaxAttr",
            Oid = "1.3.6.1.4.1.99999.1.100",
            Syntax = "9.9.9.9", // not a valid AD syntax OID
        };

        // Act
        var errors = modSvc.ValidateAttributeSchema(entry);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("attributeSyntax"));
    }

    [Fact]
    public void ValidateClassSchema_MissingSuperClass_ReturnsError()
    {
        // Arrange
        var modSvc = new SchemaModificationService(_schema, NullStore.Instance, new NullModLogger());
        var entry = new ObjectClassSchemaEntry
        {
            LdapDisplayName = "orphanClass",
            Oid = "1.3.6.1.4.1.99999.2.200",
            SuperiorClass = "nonExistentSuperClass",
            ClassType = ObjectClassType.Structural,
        };

        // Act
        var errors = modSvc.ValidateClassSchema(entry);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("nonExistentSuperClass"));
    }

    [Fact]
    public void ValidateAttributeSchema_DuplicateOid_ReturnsError()
    {
        // Arrange — cn already exists with a known OID
        var modSvc = new SchemaModificationService(_schema, NullStore.Instance, new NullModLogger());
        var cnAttr = _schema.GetAttribute("cn");
        Assert.NotNull(cnAttr);

        var entry = new AttributeSchemaEntry
        {
            LdapDisplayName = "someNewAttr",
            Oid = cnAttr.Oid, // reuse existing OID
            Syntax = "2.5.5.12",
        };

        // Act
        var errors = modSvc.ValidateAttributeSchema(entry);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("OID") && e.Contains("already used"));
    }

    [Fact]
    public void ValidateAttributeSchema_InvalidOidFormat_ReturnsError()
    {
        // Arrange
        var modSvc = new SchemaModificationService(_schema, NullStore.Instance, new NullModLogger());
        var entry = new AttributeSchemaEntry
        {
            LdapDisplayName = "badOidAttr",
            Oid = "not-an-oid",
            Syntax = "2.5.5.12",
        };

        // Act
        var errors = modSvc.ValidateAttributeSchema(entry);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("valid OID"));
    }

    [Fact]
    public void BuiltInSchemaEntries_AreMarkedImmutable()
    {
        // Arrange & Act
        var cnAttr = _schema.GetAttribute("cn");
        var userClass = _schema.GetObjectClass("user");

        // Assert
        Assert.NotNull(cnAttr);
        Assert.True(cnAttr.IsBuiltIn);
        Assert.NotNull(userClass);
        Assert.True(userClass.IsBuiltIn);
    }

    [Fact]
    public void RegisterAttribute_IncrementsSchemaVersion()
    {
        // Arrange
        var versionBefore = _schema.SchemaVersion;
        var entry = new AttributeSchemaEntry
        {
            Name = "versionTestAttr",
            LdapDisplayName = "versionTestAttr",
            Oid = "1.3.6.1.4.1.99999.1.500",
            Syntax = "2.5.5.12",
        };

        // Act
        _schema.RegisterAttribute(entry);

        // Assert
        Assert.True(_schema.SchemaVersion > versionBefore);
    }

    /// <summary>
    /// Minimal IDirectoryStore for tests — same pattern as the codebase's NullDirectoryStore.
    /// </summary>
    private sealed class NullStore : IDirectoryStore
    {
        public static readonly NullStore Instance = new();
        public Task<DirectoryObject> GetByDnAsync(string t, string dn, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
        public Task<DirectoryObject> GetByGuidAsync(string t, string g, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
        public Task<DirectoryObject> GetBySamAccountNameAsync(string t, string d, string s, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
        public Task<DirectoryObject> GetByUpnAsync(string t, string u, CancellationToken ct = default) => Task.FromResult<DirectoryObject>(null);
        public Task<SearchResult> SearchAsync(string t, string b, SearchScope s, FilterNode f, string[] a, int sl = 0, int tl = 0, string ct2 = null, int ps = 1000, bool id = false, CancellationToken ct = default) => Task.FromResult(new SearchResult());
        public Task CreateAsync(DirectoryObject obj, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(DirectoryObject obj, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string t, string dn, bool h = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task MoveAsync(string t, string o, string n, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> GetNextUsnAsync(string t, string d, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> ClaimRidPoolAsync(string t, string d, int p, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<IReadOnlyList<DirectoryObject>> GetChildrenAsync(string t, string p, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
        public Task<IReadOnlyList<DirectoryObject>> GetByServicePrincipalNameAsync(string t, string s, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
        public Task<IReadOnlyList<DirectoryObject>> GetByDnsAsync(string t, IEnumerable<string> dns, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DirectoryObject>>([]);
    }

    private sealed class NullModLogger : Microsoft.Extensions.Logging.ILogger<SchemaModificationService>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
    }
}
