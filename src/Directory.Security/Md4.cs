using System.Buffers.Binary;
using System.Text;

namespace Directory.Security;

/// <summary>
/// Pure managed implementation of the MD4 hash algorithm (RFC 1320).
/// Required because Kerberos.NET's LinuxCryptoPal does not support MD4,
/// which is needed for NT hash computation (MD4 of UTF-16LE password).
/// </summary>
internal static class Md4
{
    /// <summary>
    /// Compute the NT hash of a password: MD4(UTF-16LE(password)).
    /// </summary>
    public static byte[] ComputeNTHash(string password)
    {
        var data = Encoding.Unicode.GetBytes(password);
        return Hash(data);
    }

    /// <summary>
    /// Compute the MD4 hash of the given data.
    /// </summary>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        // MD4 state variables
        uint a0 = 0x67452301;
        uint b0 = 0xEFCDAB89;
        uint c0 = 0x98BADCFE;
        uint d0 = 0x10325476;

        // Pre-processing: add padding
        long bitLength = (long)data.Length * 8;
        int paddedLength = data.Length + 1; // +1 for 0x80 byte
        // Pad to 56 mod 64
        while (paddedLength % 64 != 56)
            paddedLength++;
        paddedLength += 8; // +8 for the bit length

        var padded = new byte[paddedLength];
        data.CopyTo(padded);
        padded[data.Length] = 0x80;

        // Append original length in bits as 64-bit little-endian
        BinaryPrimitives.WriteInt64LittleEndian(padded.AsSpan(paddedLength - 8), bitLength);

        // Process each 64-byte block
        var block = new uint[16];
        for (int offset = 0; offset < paddedLength; offset += 64)
        {
            for (int i = 0; i < 16; i++)
            {
                block[i] = BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(offset + i * 4));
            }

            uint a = a0, b = b0, c = c0, d = d0;

            // Round 1
            a = Round1(a, b, c, d, block[0], 3);
            d = Round1(d, a, b, c, block[1], 7);
            c = Round1(c, d, a, b, block[2], 11);
            b = Round1(b, c, d, a, block[3], 19);
            a = Round1(a, b, c, d, block[4], 3);
            d = Round1(d, a, b, c, block[5], 7);
            c = Round1(c, d, a, b, block[6], 11);
            b = Round1(b, c, d, a, block[7], 19);
            a = Round1(a, b, c, d, block[8], 3);
            d = Round1(d, a, b, c, block[9], 7);
            c = Round1(c, d, a, b, block[10], 11);
            b = Round1(b, c, d, a, block[11], 19);
            a = Round1(a, b, c, d, block[12], 3);
            d = Round1(d, a, b, c, block[13], 7);
            c = Round1(c, d, a, b, block[14], 11);
            b = Round1(b, c, d, a, block[15], 19);

            // Round 2
            a = Round2(a, b, c, d, block[0], 3);
            d = Round2(d, a, b, c, block[4], 5);
            c = Round2(c, d, a, b, block[8], 9);
            b = Round2(b, c, d, a, block[12], 13);
            a = Round2(a, b, c, d, block[1], 3);
            d = Round2(d, a, b, c, block[5], 5);
            c = Round2(c, d, a, b, block[9], 9);
            b = Round2(b, c, d, a, block[13], 13);
            a = Round2(a, b, c, d, block[2], 3);
            d = Round2(d, a, b, c, block[6], 5);
            c = Round2(c, d, a, b, block[10], 9);
            b = Round2(b, c, d, a, block[14], 13);
            a = Round2(a, b, c, d, block[3], 3);
            d = Round2(d, a, b, c, block[7], 5);
            c = Round2(c, d, a, b, block[11], 9);
            b = Round2(b, c, d, a, block[15], 13);

            // Round 3
            a = Round3(a, b, c, d, block[0], 3);
            d = Round3(d, a, b, c, block[8], 9);
            c = Round3(c, d, a, b, block[4], 11);
            b = Round3(b, c, d, a, block[12], 15);
            a = Round3(a, b, c, d, block[2], 3);
            d = Round3(d, a, b, c, block[10], 9);
            c = Round3(c, d, a, b, block[6], 11);
            b = Round3(b, c, d, a, block[14], 15);
            a = Round3(a, b, c, d, block[1], 3);
            d = Round3(d, a, b, c, block[9], 9);
            c = Round3(c, d, a, b, block[5], 11);
            b = Round3(b, c, d, a, block[13], 15);
            a = Round3(a, b, c, d, block[3], 3);
            d = Round3(d, a, b, c, block[11], 9);
            c = Round3(c, d, a, b, block[7], 11);
            b = Round3(b, c, d, a, block[15], 15);

            a0 += a;
            b0 += b;
            c0 += c;
            d0 += d;
        }

        // Produce the final hash value (little-endian)
        var result = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0), a0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), b0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), c0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), d0);
        return result;
    }

    private static uint RotateLeft(uint x, int n) => (x << n) | (x >> (32 - n));

    private static uint F(uint x, uint y, uint z) => (x & y) | (~x & z);
    private static uint G(uint x, uint y, uint z) => (x & y) | (x & z) | (y & z);
    private static uint H(uint x, uint y, uint z) => x ^ y ^ z;

    private static uint Round1(uint a, uint b, uint c, uint d, uint xk, int s)
        => RotateLeft(a + F(b, c, d) + xk, s);

    private static uint Round2(uint a, uint b, uint c, uint d, uint xk, int s)
        => RotateLeft(a + G(b, c, d) + xk + 0x5A827999, s);

    private static uint Round3(uint a, uint b, uint c, uint d, uint xk, int s)
        => RotateLeft(a + H(b, c, d) + xk + 0x6ED9EBA1, s);
}
