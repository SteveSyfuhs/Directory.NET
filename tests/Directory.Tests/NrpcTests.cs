using System.Security.Cryptography;
using Directory.Rpc.Nrpc;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for NRPC (Netlogon Remote Protocol) secure channel operations,
/// session key computation, credential calculation, and constants.
/// </summary>
public class NrpcTests
{
    // ────────────────────────────────────────────────────────────────
    // SecureChannelSession — session key computation
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeSessionKey_AesMode_Returns16Bytes()
    {
        // Arrange
        var sharedSecret = RandomNumberGenerator.GetBytes(16);
        var clientChallenge = RandomNumberGenerator.GetBytes(8);
        var serverChallenge = RandomNumberGenerator.GetBytes(8);

        // Act
        var sessionKey = SecureChannelSession.ComputeSessionKey(
            sharedSecret, clientChallenge, serverChallenge, useAes: true);

        // Assert
        Assert.Equal(16, sessionKey.Length);
    }

    [Fact]
    public void ComputeSessionKey_NonAesMode_Returns16Bytes()
    {
        // Arrange
        var sharedSecret = RandomNumberGenerator.GetBytes(16);
        var clientChallenge = RandomNumberGenerator.GetBytes(8);
        var serverChallenge = RandomNumberGenerator.GetBytes(8);

        // Act
        var sessionKey = SecureChannelSession.ComputeSessionKey(
            sharedSecret, clientChallenge, serverChallenge, useAes: false);

        // Assert
        Assert.Equal(16, sessionKey.Length);
    }

    [Fact]
    public void ComputeSessionKey_SameInputs_ProducesSameKey()
    {
        // Arrange
        var sharedSecret = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                         0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
        var clientChallenge = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 };
        var serverChallenge = new byte[] { 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00 };

        // Act
        var key1 = SecureChannelSession.ComputeSessionKey(sharedSecret, clientChallenge, serverChallenge, useAes: true);
        var key2 = SecureChannelSession.ComputeSessionKey(sharedSecret, clientChallenge, serverChallenge, useAes: true);

        // Assert
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void ComputeSessionKey_DifferentChallenges_ProducesDifferentKeys()
    {
        // Arrange
        var sharedSecret = RandomNumberGenerator.GetBytes(16);
        var clientChallenge1 = RandomNumberGenerator.GetBytes(8);
        var clientChallenge2 = RandomNumberGenerator.GetBytes(8);
        var serverChallenge = RandomNumberGenerator.GetBytes(8);

        // Act
        var key1 = SecureChannelSession.ComputeSessionKey(sharedSecret, clientChallenge1, serverChallenge, useAes: true);
        var key2 = SecureChannelSession.ComputeSessionKey(sharedSecret, clientChallenge2, serverChallenge, useAes: true);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void ComputeSessionKey_AesVsNonAes_ProducesDifferentKeys()
    {
        // Arrange
        var sharedSecret = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                         0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
        var clientChallenge = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 };
        var serverChallenge = new byte[] { 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0x00 };

        // Act
        var aesKey = SecureChannelSession.ComputeSessionKey(sharedSecret, clientChallenge, serverChallenge, useAes: true);
        var nonAesKey = SecureChannelSession.ComputeSessionKey(sharedSecret, clientChallenge, serverChallenge, useAes: false);

        // Assert
        Assert.NotEqual(aesKey, nonAesKey);
    }

    // ────────────────────────────────────────────────────────────────
    // SecureChannelSession — credential computation
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeNetlogonCredential_NonAesMode_Returns8Bytes()
    {
        // Arrange
        var input = RandomNumberGenerator.GetBytes(8);
        var sessionKey = RandomNumberGenerator.GetBytes(16);

        // Act
        var credential = SecureChannelSession.ComputeNetlogonCredential(input, sessionKey, useAes: false);

        // Assert
        Assert.Equal(8, credential.Length);
    }

    [Fact]
    public void ComputeNetlogonCredential_SameInput_ProducesSameOutput()
    {
        // Arrange
        var input = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var sessionKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                       0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };

        // Act — use non-AES (DES) mode which works on all .NET versions
        var cred1 = SecureChannelSession.ComputeNetlogonCredential(input, sessionKey, useAes: false);
        var cred2 = SecureChannelSession.ComputeNetlogonCredential(input, sessionKey, useAes: false);

        // Assert
        Assert.Equal(cred1, cred2);
    }

    [Fact]
    public void ComputeNetlogonCredential_DifferentInput_ProducesDifferentOutput()
    {
        // Arrange
        var input1 = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var input2 = new byte[] { 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88 };
        var sessionKey = RandomNumberGenerator.GetBytes(16);

        // Act — use non-AES (DES) mode
        var cred1 = SecureChannelSession.ComputeNetlogonCredential(input1, sessionKey, useAes: false);
        var cred2 = SecureChannelSession.ComputeNetlogonCredential(input2, sessionKey, useAes: false);

        // Assert
        Assert.NotEqual(cred1, cred2);
    }

    // ────────────────────────────────────────────────────────────────
    // Challenge/response round-trip
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    public void ChallengeResponse_RoundTrip_ClientAndServerCredentialsMatch(bool useAes)
    {
        // Arrange — simulate the secure channel handshake
        var ntHash = RandomNumberGenerator.GetBytes(16);
        var clientChallenge = RandomNumberGenerator.GetBytes(8);
        var serverChallenge = RandomNumberGenerator.GetBytes(8);

        // Act — both sides compute session key from shared secret + challenges
        var sessionKey = SecureChannelSession.ComputeSessionKey(
            ntHash, clientChallenge, serverChallenge, useAes);

        // Client computes its credential from its challenge
        var clientCredential = SecureChannelSession.ComputeNetlogonCredential(
            clientChallenge, sessionKey, useAes);

        // Server independently computes what the client credential should be
        var serverComputedClientCred = SecureChannelSession.ComputeNetlogonCredential(
            clientChallenge, sessionKey, useAes);

        // Server computes its own credential from its challenge
        var serverCredential = SecureChannelSession.ComputeNetlogonCredential(
            serverChallenge, sessionKey, useAes);

        // Assert — both sides should agree
        Assert.Equal(clientCredential, serverComputedClientCred);
        Assert.Equal(8, serverCredential.Length);
        Assert.NotEqual(clientCredential, serverCredential); // Different inputs -> different credentials
    }

    // ────────────────────────────────────────────────────────────────
    // VerifyAuthenticator
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    public void VerifyAuthenticator_ValidCredential_ReturnsNonNull(bool useAes)
    {
        // Arrange — set up a completed session
        var ntHash = RandomNumberGenerator.GetBytes(16);
        var clientChallenge = RandomNumberGenerator.GetBytes(8);
        var serverChallenge = RandomNumberGenerator.GetBytes(8);
        var sessionKey = SecureChannelSession.ComputeSessionKey(ntHash, clientChallenge, serverChallenge, useAes);

        uint negotiateFlags = useAes ? NrpcConstants.NegotiateAes : 0u;

        var session = new SecureChannelSession
        {
            SessionKey = sessionKey,
            NegotiateFlags = negotiateFlags,
            ClientCredential = new NETLOGON_CREDENTIAL { Data = (byte[])clientChallenge.Clone() },
            ServerCredential = new NETLOGON_CREDENTIAL { Data = (byte[])serverChallenge.Clone() },
        };

        // Build authenticator: timestamp + ComputeNetlogonCredential(clientCred + timestamp, sessionKey)
        uint timestamp = 1;
        var authInput = new byte[8];
        Buffer.BlockCopy(clientChallenge, 0, authInput, 0, 8);
        uint credVal = BitConverter.ToUInt32(authInput, 0);
        credVal += timestamp;
        BitConverter.GetBytes(credVal).CopyTo(authInput, 0);
        var authCredential = SecureChannelSession.ComputeNetlogonCredential(authInput, sessionKey, useAes);

        // Act
        var returnAuth = session.VerifyAuthenticator(authCredential, timestamp);

        // Assert
        Assert.NotNull(returnAuth);
        Assert.Equal(8, returnAuth.Length);
    }

    [Fact]
    public void VerifyAuthenticator_InvalidCredential_ReturnsNull()
    {
        // Arrange — use non-AES (DES) mode
        var sessionKey = RandomNumberGenerator.GetBytes(16);
        var session = new SecureChannelSession
        {
            SessionKey = sessionKey,
            NegotiateFlags = 0u, // no AES flag
            ClientCredential = new NETLOGON_CREDENTIAL { Data = RandomNumberGenerator.GetBytes(8) },
        };

        // Garbage credential
        var badCredential = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        // Act
        var result = session.VerifyAuthenticator(badCredential, 1);

        // Assert
        Assert.Null(result);
    }

    // ────────────────────────────────────────────────────────────────
    // NETLOGON_CREDENTIAL structure
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NetlogonCredential_DefaultData_Is8Bytes()
    {
        var cred = new NETLOGON_CREDENTIAL();
        Assert.Equal(8, cred.Data.Length);
    }

    // ────────────────────────────────────────────────────────────────
    // NrpcConstants validation
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void NrpcConstants_InterfaceId_IsCorrect()
    {
        Assert.Equal(new Guid("12345678-1234-abcd-ef00-01234567cffb"), NrpcConstants.InterfaceId);
    }

    [Fact]
    public void NrpcConstants_StatusSuccess_IsZero()
    {
        Assert.Equal(0u, NrpcConstants.StatusSuccess);
    }

    [Fact]
    public void NrpcConstants_NegotiateAes_Is0x01000000()
    {
        Assert.Equal(0x01000000u, NrpcConstants.NegotiateAes);
    }

    [Fact]
    public void NrpcConstants_NegotiateSecureRpc_Is0x40000000()
    {
        Assert.Equal(0x40000000u, NrpcConstants.NegotiateSecureRpc);
    }

    [Fact]
    public void NrpcConstants_SecureChannelWorkstation_Is2()
    {
        Assert.Equal(2, NrpcConstants.SecureChannelWorkstation);
    }

    [Fact]
    public void NrpcConstants_StatusAccessDenied_IsC0000022()
    {
        Assert.Equal(0xC0000022u, NrpcConstants.StatusAccessDenied);
    }

    [Fact]
    public void NrpcConstants_StatusNoTrustSamAccount_IsC000018B()
    {
        Assert.Equal(0xC000018Bu, NrpcConstants.StatusNoTrustSamAccount);
    }

    [Fact]
    public void NrpcConstants_OpNetrServerReqChallenge_Is4()
    {
        Assert.Equal(4, NrpcConstants.OpNetrServerReqChallenge);
    }

    [Fact]
    public void NrpcConstants_OpNetrServerAuthenticate3_Is26()
    {
        Assert.Equal(26, NrpcConstants.OpNetrServerAuthenticate3);
    }

    // ────────────────────────────────────────────────────────────────
    // Machine account password handling (DecryptNewPassword)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void DecryptNewPassword_InvalidLength_ThrowsArgumentException()
    {
        var session = new SecureChannelSession
        {
            SessionKey = RandomNumberGenerator.GetBytes(16),
            NegotiateFlags = NrpcConstants.NegotiateAes,
        };

        // 516 bytes is required; 100 is wrong
        Assert.Throws<ArgumentException>(() => session.DecryptNewPassword(new byte[100]));
    }

    [Fact]
    public void DecryptNewPassword_NonAesMode_RoundTrips()
    {
        // Arrange — build a valid encrypted buffer (516 bytes) using RC4
        var sessionKey = RandomNumberGenerator.GetBytes(16);
        var session = new SecureChannelSession
        {
            SessionKey = sessionKey,
            NegotiateFlags = 0u, // non-AES => uses RC4
        };

        string testPassword = "TestPassword123!";
        var passwordBytes = System.Text.Encoding.Unicode.GetBytes(testPassword);

        // Build NL_TRUST_PASSWORD: 512 bytes of data (password right-justified) + 4 bytes length
        var plainBuffer = new byte[516];
        int offset = 512 - passwordBytes.Length;
        Buffer.BlockCopy(passwordBytes, 0, plainBuffer, offset, passwordBytes.Length);
        BitConverter.GetBytes(passwordBytes.Length).CopyTo(plainBuffer, 512);

        // Encrypt with RC4 (same as what SecureChannelSession.DecryptNewPassword expects)
        var encryptedBuffer = Rc4Transform(sessionKey, plainBuffer);

        // Act
        var decrypted = session.DecryptNewPassword(encryptedBuffer);

        // Assert
        Assert.Equal(testPassword, decrypted);
    }

    /// <summary>
    /// RC4 stream cipher transform for test encryption.
    /// </summary>
    private static byte[] Rc4Transform(byte[] key, byte[] data)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var output = new byte[data.Length];
        int x = 0, y = 0;
        for (int k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            output[k] = (byte)(data[k] ^ s[(s[x] + s[y]) & 0xFF]);
        }
        return output;
    }

    // ────────────────────────────────────────────────────────────────
    // SecureChannelSession — basic properties
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SecureChannelSession_Defaults_AreReasonable()
    {
        var session = new SecureChannelSession();
        Assert.Equal("", session.ComputerName);
        Assert.Equal("", session.AccountName);
        Assert.Empty(session.ClientChallenge);
        Assert.Empty(session.ServerChallenge);
        Assert.Empty(session.SessionKey);
        Assert.Equal(0u, session.NegotiateFlags);
        Assert.NotNull(session.ClientCredential);
        Assert.NotNull(session.ServerCredential);
    }
}
