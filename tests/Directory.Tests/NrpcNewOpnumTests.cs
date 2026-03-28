using Directory.Rpc.Nrpc;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for the newer NRPC opnum constants, LogonControl levels,
/// legacy authentication operations, and secure channel enhancements.
/// </summary>
public class NrpcNewOpnumTests
{
    // ────────────────────────────────────────────────────────────────
    // LogonControl
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NetrLogonControl2Ex_OpnumIs18()
    {
        Assert.Equal((ushort)18, NrpcConstants.OpNetrLogonControl2Ex);
    }

    [Fact]
    public void NetrLogonControl_OpnumIs12()
    {
        Assert.Equal((ushort)12, NrpcConstants.OpNetrLogonControl);
    }

    [Fact]
    public void LogonControl_QueryLevelConstants_Exist()
    {
        // NETLOGON_INFO_1 uses ControlQuery (level 1)
        Assert.Equal(1u, NrpcConstants.ControlQuery);
        // NETLOGON_INFO_2 uses ControlRediscover (level 5) and ControlTcQuery (level 6)
        Assert.Equal(5u, NrpcConstants.ControlRediscover);
        Assert.Equal(6u, NrpcConstants.ControlTcQuery);
        // NETLOGON_INFO_3 uses ControlReplicate (level 2)
        Assert.Equal(2u, NrpcConstants.ControlReplicate);
    }

    // ────────────────────────────────────────────────────────────────
    // LogonSamLogoff
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void LogonSamLogoff_OpnumIs3()
    {
        Assert.Equal((ushort)3, NrpcConstants.OpNetrLogonSamLogoff);
    }

    [Fact]
    public void LogonSamLogoff_IsInformational_NoSideEffects()
    {
        // LogonSamLogoff (opnum 3) is informational per MS-NRPC 3.5.4.3.7.
        // It should succeed with STATUS_SUCCESS. We verify the constant exists
        // and the status code for success is defined.
        Assert.Equal(0u, NrpcConstants.StatusSuccess);
        Assert.Equal((ushort)3, NrpcConstants.OpNetrLogonSamLogoff);
    }

    // ────────────────────────────────────────────────────────────────
    // Legacy Auth
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ServerAuthenticate2_OpnumIs15()
    {
        Assert.Equal((ushort)15, NrpcConstants.OpNetrServerAuthenticate2);
    }

    [Fact]
    public void ServerAuthenticate_LegacyOpnumIs5()
    {
        Assert.Equal((ushort)5, NrpcConstants.OpNetrServerAuthenticate);
    }

    // ────────────────────────────────────────────────────────────────
    // TrustPasswordsGet
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void TrustPasswordsGet_OpnumIs42()
    {
        Assert.Equal((ushort)42, NrpcConstants.OpNetrServerTrustPasswordsGet);
    }

    // ────────────────────────────────────────────────────────────────
    // AddressToSiteNames
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddressToSiteNames_OpnumIs33()
    {
        Assert.Equal((ushort)33, NrpcConstants.OpDsrAddressToSiteNamesW);
    }

    [Fact]
    public void DefaultSiteName_IsDefaultFirstSiteName()
    {
        // DcInstanceInfo uses "Default-First-Site-Name" as the default site.
        var dcInfo = new Directory.Core.Models.DcInstanceInfo();
        Assert.Equal("Default-First-Site-Name", dcInfo.SiteName);
    }

    // ────────────────────────────────────────────────────────────────
    // Secure Channel Enhancements
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NegotiateFlags_IncludeAes()
    {
        // The NegotiateSupportedFlags advertised by the DC must include the AES bit
        Assert.True(
            (NrpcConstants.NegotiateSupportedFlags & NrpcConstants.NegotiateAes) != 0,
            "NegotiateSupportedFlags should include NegotiateAes (0x01000000)");

        // Also verify the AES flag value itself
        Assert.Equal(0x01000000u, NrpcConstants.NegotiateAes);
    }

    [Fact]
    public void GetCapabilities_OpnumIs21()
    {
        // NetrLogonGetCapabilities (opnum 21) returns the negotiated flags
        Assert.Equal((ushort)21, NrpcConstants.OpNetrLogonGetCapabilities);
    }
}
