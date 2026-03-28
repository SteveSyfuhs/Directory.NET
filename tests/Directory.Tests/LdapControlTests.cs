using System.Formats.Asn1;
using System.Text;
using Directory.Ldap.Handlers;
using Directory.Ldap.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Directory.Tests;

public class LdapControlTests
{
    private readonly LdapControlHandler _handler;

    public LdapControlTests()
    {
        _handler = new LdapControlHandler(new NullControlLogger());
    }

    [Fact]
    public void ProcessRequestControls_PagedResults_ParsesPageSizeAndCookie()
    {
        // Arrange
        var controlValue = BuildPagedResultsControlValue(500, "token123");
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.PagedResults, Criticality = false, Value = controlValue }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.NotNull(result.PagedResults);
        Assert.Equal(500, result.PagedResults.PageSize);
        Assert.Equal("token123", result.PagedResults.Cookie);
    }

    [Fact]
    public void ProcessRequestControls_PagedResults_EmptyCookie_ParsesAsNull()
    {
        // Arrange
        var controlValue = BuildPagedResultsControlValue(100, null);
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.PagedResults, Criticality = false, Value = controlValue }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.NotNull(result.PagedResults);
        Assert.Equal(100, result.PagedResults.PageSize);
        Assert.Null(result.PagedResults.Cookie);
    }

    [Fact]
    public void ProcessRequestControls_SortControl_ParsesSingleKey()
    {
        // Arrange
        var controlValue = BuildSortControlValue([("cn", false)]);
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.ServerSort, Criticality = false, Value = controlValue }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.NotNull(result.SortRequest);
        Assert.Single(result.SortRequest.SortKeys);
        Assert.Equal("cn", result.SortRequest.SortKeys[0].AttributeName);
        Assert.False(result.SortRequest.SortKeys[0].ReverseOrder);
    }

    [Fact]
    public void ProcessRequestControls_SortControl_MultiKeyWithReverse()
    {
        // Arrange
        var controlValue = BuildSortControlValue([("sn", false), ("givenName", true)]);
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.ServerSort, Criticality = false, Value = controlValue }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.NotNull(result.SortRequest);
        Assert.Equal(2, result.SortRequest.SortKeys.Count);
        Assert.Equal("sn", result.SortRequest.SortKeys[0].AttributeName);
        Assert.False(result.SortRequest.SortKeys[0].ReverseOrder);
        Assert.Equal("givenName", result.SortRequest.SortKeys[1].AttributeName);
        Assert.True(result.SortRequest.SortKeys[1].ReverseOrder);
    }

    [Fact]
    public void ProcessRequestControls_ExtendedDn_NullValue_DefaultsToOption0()
    {
        // Arrange
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.ExtendedDn, Criticality = false, Value = null }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.NotNull(result.ExtendedDn);
        Assert.Equal(0, result.ExtendedDn.Option);
    }

    [Fact]
    public void ProcessRequestControls_ExtendedDn_Option1_StringFormat()
    {
        // Arrange
        var controlValue = BuildExtendedDnControlValue(1);
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.ExtendedDn, Criticality = false, Value = controlValue }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.NotNull(result.ExtendedDn);
        Assert.Equal(1, result.ExtendedDn.Option);
    }

    [Fact]
    public void ProcessRequestControls_SdFlags_ParsesFlagsValue()
    {
        // Arrange — SD flags 0x04 = DACL only
        var controlValue = BuildSdFlagsControlValue(0x04);
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.SdFlags, Criticality = false, Value = controlValue }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.Equal(0x04, result.SdFlags);
    }

    [Fact]
    public void ProcessRequestControls_SdFlags_NullValue_DefaultsToAllParts()
    {
        // Arrange
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.SdFlags, Criticality = false, Value = null }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.Equal(0xF, result.SdFlags);
    }

    [Fact]
    public void ProcessRequestControls_PermissiveModify_SetsFlag()
    {
        // Arrange
        var controls = new List<LdapControl>
        {
            new() { Oid = LdapConstants.Controls.PermissiveModify, Criticality = false, Value = null }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.True(result.PermissiveModify);
    }

    [Fact]
    public void ProcessRequestControls_UnsupportedCriticalControl_SetsUnsupportedCritical()
    {
        // Arrange
        var controls = new List<LdapControl>
        {
            new() { Oid = "1.2.3.4.5.99999", Criticality = true, Value = null }
        };

        // Act
        var result = _handler.ProcessRequestControls(controls);

        // Assert
        Assert.Equal("1.2.3.4.5.99999", result.UnsupportedCritical);
    }

    [Fact]
    public void BuildPagedResultsResponse_EncodesCorrectly()
    {
        // Arrange & Act
        var responseBytes = LdapControlHandler.BuildPagedResultsResponse(42, "nextPageToken");

        // Assert — decode and verify
        var reader = new AsnReader(responseBytes, AsnEncodingRules.BER);
        var seq = reader.ReadSequence();
        var totalEstimate = (int)seq.ReadInteger();
        var cookie = Encoding.UTF8.GetString(seq.ReadOctetString());

        Assert.Equal(42, totalEstimate);
        Assert.Equal("nextPageToken", cookie);
    }

    [Fact]
    public void BuildPagedResultsResponse_NullToken_EncodesEmptyCookie()
    {
        // Arrange & Act
        var responseBytes = LdapControlHandler.BuildPagedResultsResponse(0, null);

        // Assert
        var reader = new AsnReader(responseBytes, AsnEncodingRules.BER);
        var seq = reader.ReadSequence();
        var totalEstimate = (int)seq.ReadInteger();
        var cookie = seq.ReadOctetString();

        Assert.Equal(0, totalEstimate);
        Assert.Empty(cookie);
    }

    // Helper methods to build BER-encoded control values

    private static byte[] BuildPagedResultsControlValue(int pageSize, string cookie)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(pageSize);
            writer.WriteOctetString(cookie != null ? Encoding.UTF8.GetBytes(cookie) : []);
        }
        return writer.Encode();
    }

    private static byte[] BuildSortControlValue(List<(string attrName, bool reverse)> keys)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            foreach (var (attrName, reverse) in keys)
            {
                using (writer.PushSequence())
                {
                    writer.WriteOctetString(Encoding.UTF8.GetBytes(attrName));
                    if (reverse)
                    {
                        writer.WriteBoolean(true, new Asn1Tag(TagClass.ContextSpecific, 1));
                    }
                }
            }
        }
        return writer.Encode();
    }

    private static byte[] BuildExtendedDnControlValue(int option)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(option);
        }
        return writer.Encode();
    }

    private static byte[] BuildSdFlagsControlValue(int flags)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(flags);
        }
        return writer.Encode();
    }

    /// <summary>
    /// Minimal ILogger implementation for tests (no mocking library).
    /// </summary>
    private sealed class NullControlLogger : ILogger<LdapControlHandler>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter) { }
    }
}
