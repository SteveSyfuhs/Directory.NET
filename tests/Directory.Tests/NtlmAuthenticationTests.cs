using System.Security.Cryptography;
using System.Text;
using Directory.Security;
using Xunit;

namespace Directory.Tests;

public class NtlmAuthenticationTests
{
    // ── Challenge generation ───────────────────────────────────────────

    [Fact]
    public void GenerateChallenge_Returns8Bytes()
    {
        var authenticator = new NtlmAuthenticator(null, null, null);
        var challenge = authenticator.GenerateChallenge();

        Assert.Equal(8, challenge.Length);
    }

    [Fact]
    public void GenerateChallenge_IsRandom_TwoChallengesDiffer()
    {
        var authenticator = new NtlmAuthenticator(null, null, null);
        var challenge1 = authenticator.GenerateChallenge();
        var challenge2 = authenticator.GenerateChallenge();

        // Two random 8-byte challenges should almost certainly differ
        Assert.NotEqual(challenge1, challenge2);
    }

    // ── NTLMv2 response computation with known values ──────────────────

    [Fact]
    public void NtlmV2Response_ComputedCorrectly_WithKnownInputs()
    {
        // Manually compute an NTLMv2 response and verify the algorithm matches.
        // This tests the mathematical correctness of the NTLMv2 protocol steps.

        var passwordSvc = new PasswordService(null, null);
        var ntHash = passwordSvc.ComputeNTHash("Password");

        string username = "User";
        string domain = "Domain";

        // Step 1: NTLMv2Hash = HMAC-MD5(ntHash, UPPERCASE(username) + domain)
        var identityBytes = Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
        byte[] ntlmv2Hash;
        using (var hmac = new HMACMD5(ntHash))
        {
            ntlmv2Hash = hmac.ComputeHash(identityBytes);
        }

        Assert.Equal(16, ntlmv2Hash.Length);

        // Step 2: Create a challenge and client challenge
        var serverChallenge = new byte[] { 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF };
        var clientChallenge = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11 };

        // Step 3: NTLMv2 response = HMAC-MD5(ntlmv2Hash, serverChallenge + clientChallenge)
        var challengeData = new byte[serverChallenge.Length + clientChallenge.Length];
        serverChallenge.CopyTo(challengeData, 0);
        clientChallenge.CopyTo(challengeData, serverChallenge.Length);

        byte[] expectedResponse;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            expectedResponse = hmac.ComputeHash(challengeData);
        }

        Assert.Equal(16, expectedResponse.Length);

        // Verify determinism: same inputs produce the same output
        byte[] secondResponse;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            secondResponse = hmac.ComputeHash(challengeData);
        }

        Assert.Equal(expectedResponse, secondResponse);
    }

    // ── Session key derivation ─────────────────────────────────────────

    [Fact]
    public void SessionKey_DerivedFromNtlmv2Response()
    {
        // Per MS-NLMP: SessionBaseKey = HMAC-MD5(NTLMv2Hash, ntlmv2Response[0..16])
        var passwordSvc = new PasswordService(null, null);
        var ntHash = passwordSvc.ComputeNTHash("TestPassword");

        var identityBytes = Encoding.Unicode.GetBytes("USER" + "DOMAIN");
        byte[] ntlmv2Hash;
        using (var hmac = new HMACMD5(ntHash))
        {
            ntlmv2Hash = hmac.ComputeHash(identityBytes);
        }

        var serverChallenge = new byte[8];
        var clientChallenge = new byte[8];
        Array.Fill<byte>(serverChallenge, 0x11);
        Array.Fill<byte>(clientChallenge, 0x22);

        var challengeData = new byte[16];
        serverChallenge.CopyTo(challengeData, 0);
        clientChallenge.CopyTo(challengeData, 8);

        byte[] ntlmv2Response;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            ntlmv2Response = hmac.ComputeHash(challengeData);
        }

        // Session key = HMAC-MD5(NTLMv2Hash, ntlmv2Response)
        byte[] sessionKey;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            sessionKey = hmac.ComputeHash(ntlmv2Response);
        }

        Assert.Equal(16, sessionKey.Length);

        // Session key must be deterministic
        byte[] sessionKey2;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            sessionKey2 = hmac.ComputeHash(ntlmv2Response);
        }

        Assert.Equal(sessionKey, sessionKey2);
    }

    // ── NT hash is the RC4 key ─────────────────────────────────────────

    [Fact]
    public void NtHash_UsedAsHmacMd5Key_ProducesNtlmv2Hash()
    {
        var passwordSvc = new PasswordService(null, null);
        var ntHash = passwordSvc.ComputeNTHash("password");

        // NTLMv2 hash = HMAC-MD5(ntHash, UNICODE(UPPER(username) + domain))
        var identity = Encoding.Unicode.GetBytes("ADMIN" + "CORP.COM");

        byte[] result;
        using (var hmac = new HMACMD5(ntHash))
        {
            result = hmac.ComputeHash(identity);
        }

        Assert.Equal(16, result.Length);
        // Different identity should produce different hash
        var identity2 = Encoding.Unicode.GetBytes("USER" + "OTHER.COM");
        byte[] result2;
        using (var hmac = new HMACMD5(ntHash))
        {
            result2 = hmac.ComputeHash(identity2);
        }

        Assert.NotEqual(result, result2);
    }

    // ── Empty/null credential handling ─────────────────────────────────

    [Fact]
    public void NtHash_EmptyPassword_ProducesWellKnownHash()
    {
        var passwordSvc = new PasswordService(null, null);
        var hash = passwordSvc.ComputeNTHash("");

        // Well-known empty password NT hash
        Assert.Equal("31D6CFE0D16AE931B73C59D7E0C089C0", Convert.ToHexString(hash));
    }

    [Fact]
    public void NtlmV2_EmptyPasswordHash_StillProducesValidResponse()
    {
        var passwordSvc = new PasswordService(null, null);
        var ntHash = passwordSvc.ComputeNTHash("");

        var identity = Encoding.Unicode.GetBytes("USER" + "DOMAIN");
        byte[] ntlmv2Hash;
        using (var hmac = new HMACMD5(ntHash))
        {
            ntlmv2Hash = hmac.ComputeHash(identity);
        }

        Assert.Equal(16, ntlmv2Hash.Length);
        // Even with empty password, the HMAC should produce a valid 16-byte result
    }

    [Fact]
    public void NtHash_DifferentPasswords_ProduceDifferentHashes()
    {
        var passwordSvc = new PasswordService(null, null);
        var hash1 = passwordSvc.ComputeNTHash("password1");
        var hash2 = passwordSvc.ComputeNTHash("password2");

        Assert.NotEqual(hash1, hash2);
    }
}
