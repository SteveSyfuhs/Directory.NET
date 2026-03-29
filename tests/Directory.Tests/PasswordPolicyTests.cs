using Directory.Core.Interfaces;
using Directory.Security;
using Xunit;

namespace Directory.Tests;

public class PasswordPolicyTests
{
    private readonly PasswordService _svc = new(null, null);

    // ── Password complexity tests ──────────────────────────────────────

    [Fact]
    public void MeetsComplexityRequirements_UpperLowerDigitSpecial_ReturnsTrue()
    {
        Assert.True(_svc.MeetsComplexityRequirements("P@ssw0rd!"));
    }

    [Fact]
    public void MeetsComplexityRequirements_UpperLowerDigit_ReturnsTrue()
    {
        // 3 out of 4 categories is sufficient
        Assert.True(_svc.MeetsComplexityRequirements("Passw0rd"));
    }

    [Fact]
    public void MeetsComplexityRequirements_OnlyLowercase_ReturnsFalse()
    {
        // Only 1 category — below threshold of 3
        Assert.False(_svc.MeetsComplexityRequirements("password"));
    }

    [Fact]
    public void MeetsComplexityRequirements_TwoCategories_ReturnsFalse()
    {
        // Lower + digit = 2 categories, needs 3
        Assert.False(_svc.MeetsComplexityRequirements("password1"));
    }

    [Fact]
    public void MeetsComplexityRequirements_UsernameInPassword_ReturnsFalse()
    {
        Assert.False(_svc.MeetsComplexityRequirements("jsmith@Pass1!", "jsmith"));
    }

    [Fact]
    public void MeetsComplexityRequirements_UsernameInPasswordCaseInsensitive_ReturnsFalse()
    {
        Assert.False(_svc.MeetsComplexityRequirements("JSMITH@Pass1!", "jsmith"));
    }

    [Fact]
    public void MeetsComplexityRequirements_NullUsername_SkipsUsernameCheck()
    {
        Assert.True(_svc.MeetsComplexityRequirements("P@ssw0rd!"));
    }

    // ── Password too short ─────────────────────────────────────────────

    [Fact]
    public void MeetsComplexityRequirements_TooShort_ReturnsFalse()
    {
        // Less than 7 characters
        Assert.False(_svc.MeetsComplexityRequirements("Ab1!"));
    }

    [Fact]
    public void MeetsComplexityRequirements_ExactlyMinLength_ReturnsTrue()
    {
        // 7 characters with 3+ categories
        Assert.True(_svc.MeetsComplexityRequirements("Ab1!xyz"));
    }

    [Fact]
    public void MeetsComplexityRequirements_SixCharsComplex_ReturnsFalse()
    {
        // 6 chars, all categories met, but still too short
        Assert.False(_svc.MeetsComplexityRequirements("Ab1!x@"));
    }

    // ── Kerberos key derivation ────────────────────────────────────────

    [Fact]
    public void DeriveKerberosKeys_ProducesTwoKeys()
    {
        var keys = _svc.DeriveKerberosKeys("user@REALM.COM", "password", "REALM.COM");
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void DeriveKerberosKeys_AES256KeyIs32Bytes()
    {
        var keys = _svc.DeriveKerberosKeys("user@REALM.COM", "password", "REALM.COM");
        var aes256 = keys.First(k => k.EncryptionType == 18); // AES256
        Assert.Equal(32, aes256.KeyValue.Length);
    }

    [Fact]
    public void DeriveKerberosKeys_AES128KeyIs16Bytes()
    {
        var keys = _svc.DeriveKerberosKeys("user@REALM.COM", "password", "REALM.COM");
        var aes128 = keys.First(k => k.EncryptionType == 17); // AES128
        Assert.Equal(16, aes128.KeyValue.Length);
    }

    [Fact]
    public void DeriveKerberosKeys_EncryptionTypes_AreCorrect()
    {
        var keys = _svc.DeriveKerberosKeys("user@REALM.COM", "password", "REALM.COM");
        Assert.Equal(18, keys[0].EncryptionType); // AES256
        Assert.Equal(17, keys[1].EncryptionType); // AES128
    }

    [Fact]
    public void DeriveKerberosKeys_SameInput_ProducesDeterministicOutput()
    {
        var keys1 = _svc.DeriveKerberosKeys("user@REALM.COM", "password", "REALM.COM");
        var keys2 = _svc.DeriveKerberosKeys("user@REALM.COM", "password", "REALM.COM");

        for (int i = 0; i < keys1.Count; i++)
        {
            Assert.Equal(keys1[i].KeyValue, keys2[i].KeyValue);
        }
    }

    [Fact]
    public void DeriveKerberosKeys_DifferentRealms_ProduceDifferentKeys()
    {
        var keys1 = _svc.DeriveKerberosKeys("user@REALM1.COM", "password", "REALM1.COM");
        var keys2 = _svc.DeriveKerberosKeys("user@REALM2.COM", "password", "REALM2.COM");

        // AES keys are realm-dependent; at least the AES256 key should differ
        Assert.NotEqual(
            Convert.ToHexString(keys1[0].KeyValue),
            Convert.ToHexString(keys2[0].KeyValue));
    }

    [Fact]
    public void DeriveKerberosKeys_DifferentPasswords_ProduceDifferentKeys()
    {
        var keys1 = _svc.DeriveKerberosKeys("user@REALM.COM", "password1", "REALM.COM");
        var keys2 = _svc.DeriveKerberosKeys("user@REALM.COM", "password2", "REALM.COM");

        Assert.NotEqual(
            Convert.ToHexString(keys1[0].KeyValue),
            Convert.ToHexString(keys2[0].KeyValue));
    }
}
