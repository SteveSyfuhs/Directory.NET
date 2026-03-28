using System.Security.Cryptography;

namespace Directory.Security.Mfa;

/// <summary>
/// Implements RFC 6238 Time-based One-Time Password (TOTP) generation and validation.
/// Uses HMAC-SHA1 with 30-second time steps and 6-digit codes for compatibility
/// with Google Authenticator and other standard TOTP apps.
/// </summary>
public class TotpService
{
    private const int TimeStepSeconds = 30;
    private const int CodeDigits = 6;
    private const int AllowedDrift = 1; // Allow 1 step in each direction

    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    /// <summary>
    /// Generates a cryptographically random TOTP secret and returns it as a Base32-encoded string.
    /// </summary>
    public string GenerateSecret(int byteLength = 20)
    {
        var secretBytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base32Encode(secretBytes);
    }

    /// <summary>
    /// Generates an otpauth:// provisioning URI suitable for QR code scanning.
    /// </summary>
    public string GenerateProvisioningUri(string secret, string accountName, string issuer = "Directory.NET")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);

        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits={CodeDigits}&period={TimeStepSeconds}";
    }

    /// <summary>
    /// Validates a TOTP code against the given secret, allowing for clock drift of +/- 1 time step.
    /// </summary>
    public bool ValidateCode(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != CodeDigits)
            return false;

        if (!int.TryParse(code, out _))
            return false;

        var secretBytes = Base32Decode(base32Secret);
        var currentTimeStep = GetCurrentTimeStep();

        for (var offset = -AllowedDrift; offset <= AllowedDrift; offset++)
        {
            var computedCode = ComputeTotp(secretBytes, currentTimeStep + offset);
            if (CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(computedCode),
                System.Text.Encoding.UTF8.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes a TOTP code for the given secret and time step.
    /// RFC 6238: TOTP = HOTP(K, T) where T = floor((Current Unix time) / timeStep)
    /// RFC 4226: HOTP(K, C) = Truncate(HMAC-SHA1(K, C)) mod 10^Digits
    /// </summary>
    private static string ComputeTotp(byte[] secret, long timeStep)
    {
        // Convert time step to big-endian 8-byte array
        var timeBytes = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            timeBytes[i] = (byte)(timeStep & 0xFF);
            timeStep >>= 8;
        }

        // HMAC-SHA1
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(timeBytes);

        // Dynamic truncation (RFC 4226 section 5.4)
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var otp = binaryCode % (int)Math.Pow(10, CodeDigits);
        return otp.ToString().PadLeft(CodeDigits, '0');
    }

    private static long GetCurrentTimeStep()
    {
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return unixTime / TimeStepSeconds;
    }

    /// <summary>
    /// Encodes a byte array to a Base32 string (RFC 4648).
    /// </summary>
    public static string Base32Encode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;

        var result = new char[(data.Length * 8 + 4) / 5];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result[index++] = Base32Chars[(buffer >> bitsLeft) & 0x1F];
            }
        }

        if (bitsLeft > 0)
        {
            result[index++] = Base32Chars[(buffer << (5 - bitsLeft)) & 0x1F];
        }

        return new string(result, 0, index);
    }

    /// <summary>
    /// Decodes a Base32-encoded string to a byte array (RFC 4648).
    /// </summary>
    public static byte[] Base32Decode(string base32)
    {
        if (string.IsNullOrEmpty(base32)) return [];

        base32 = base32.TrimEnd('=').ToUpperInvariant();

        var output = new byte[base32.Length * 5 / 8];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var c in base32)
        {
            int value;
            if (c is >= 'A' and <= 'Z')
                value = c - 'A';
            else if (c is >= '2' and <= '7')
                value = c - '2' + 26;
            else
                throw new FormatException($"Invalid Base32 character: {c}");

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output[index++] = (byte)(buffer >> bitsLeft);
            }
        }

        return output[..index];
    }
}
