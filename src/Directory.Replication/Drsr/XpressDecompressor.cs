using System.Buffers.Binary;

namespace Directory.Replication.Drsr;

/// <summary>
/// Decompresses XPRESS compressed data from DRSUAPI GetNCChanges responses.
/// Windows DCs may compress large replication payloads using either Plain LZ77 (MS-XCA section 2.1)
/// or LZ77+Huffman (MS-XCA section 2.3).
/// </summary>
public static class XpressDecompressor
{
    /// <summary>
    /// Auto-detect and decompress XPRESS data. Tries LZ77+Huffman first (requires a 256-byte
    /// Huffman table prefix), then falls back to Plain LZ77.
    /// </summary>
    /// <param name="compressedData">The compressed data buffer.</param>
    /// <param name="uncompressedSize">The expected uncompressed output size.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] Decompress(byte[] compressedData, int uncompressedSize)
    {
        if (compressedData.Length == 0 || uncompressedSize == 0)
            return [];

        // LZ77+Huffman always starts with a 256-byte Huffman table.
        // If we have at least 256 bytes, try Huffman first.
        if (compressedData.Length >= 256)
        {
            try
            {
                return DecompressLz77Huffman(compressedData, uncompressedSize);
            }
            catch
            {
                // Fall through to Plain LZ77
            }
        }

        return DecompressPlainLz77(compressedData, uncompressedSize);
    }

    /// <summary>
    /// Decompress XPRESS Plain LZ77 data per MS-XCA section 2.1.
    /// The compressed buffer contains alternating 32-bit indicator flag words and data.
    /// Each bit in the flags word indicates whether the next item is a literal byte (0)
    /// or a match reference (1).
    /// </summary>
    /// <param name="compressedData">The Plain LZ77 compressed data.</param>
    /// <param name="uncompressedSize">The expected uncompressed output size.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] DecompressPlainLz77(byte[] compressedData, int uncompressedSize)
    {
        var output = new byte[uncompressedSize];
        int outputPos = 0;
        int inputPos = 0;

        while (outputPos < uncompressedSize && inputPos < compressedData.Length)
        {
            // Read 32-bit flags word
            if (inputPos + 4 > compressedData.Length)
                break;

            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(compressedData.AsSpan(inputPos));
            inputPos += 4;

            // Process each of 32 bits (LSB first)
            for (int bit = 0; bit < 32 && outputPos < uncompressedSize; bit++)
            {
                if ((flags & (1u << bit)) == 0)
                {
                    // Bit = 0: literal byte
                    if (inputPos >= compressedData.Length)
                        break;

                    output[outputPos++] = compressedData[inputPos++];
                }
                else
                {
                    // Bit = 1: match reference (2 bytes)
                    if (inputPos + 2 > compressedData.Length)
                        break;

                    ushort matchRef = BinaryPrimitives.ReadUInt16LittleEndian(compressedData.AsSpan(inputPos));
                    inputPos += 2;

                    // Distance is encoded in the upper 13 bits
                    int distance = (matchRef >> 3) + 1;

                    // Length is encoded in the lower 3 bits
                    int length = matchRef & 7;

                    if (length == 7)
                    {
                        // Additional length byte follows
                        if (inputPos >= compressedData.Length)
                            break;

                        int additionalLength = compressedData[inputPos++];
                        if (additionalLength == 255)
                        {
                            // 2-byte length override follows
                            if (inputPos + 2 > compressedData.Length)
                                break;

                            length = BinaryPrimitives.ReadUInt16LittleEndian(compressedData.AsSpan(inputPos));
                            inputPos += 2;

                            // If the 2-byte override is 0, then a 4-byte length follows
                            if (length == 0)
                            {
                                if (inputPos + 4 > compressedData.Length)
                                    break;

                                length = (int)BinaryPrimitives.ReadUInt32LittleEndian(compressedData.AsSpan(inputPos));
                                inputPos += 4;
                            }
                        }
                        else
                        {
                            length = additionalLength + 7;
                        }
                    }

                    // Actual length includes the minimum match of 3
                    length += 3;

                    // Copy match bytes from output (may overlap for run-length encoding)
                    int srcPos = outputPos - distance;
                    if (srcPos < 0)
                        break;

                    for (int i = 0; i < length && outputPos < uncompressedSize; i++)
                    {
                        output[outputPos++] = output[srcPos + i];
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Decompress LZ77+Huffman data per MS-XCA section 2.3.
    /// The data starts with a 256-byte Huffman table (each byte encodes symbol lengths for
    /// two symbols, 4 bits each). This defines a canonical Huffman code for 512 symbols:
    /// 0-255 are literal bytes, 256-511 encode match references.
    /// </summary>
    /// <param name="compressedData">The LZ77+Huffman compressed data.</param>
    /// <param name="uncompressedSize">The expected uncompressed output size.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] DecompressLz77Huffman(byte[] compressedData, int uncompressedSize)
    {
        var output = new byte[uncompressedSize];
        int outputPos = 0;
        int inputPos = 0;

        while (outputPos < uncompressedSize)
        {
            if (inputPos + 256 > compressedData.Length)
                throw new InvalidDataException("Not enough data for Huffman table.");

            // Read 256-byte Huffman table: each byte has two 4-bit code lengths
            var symbolLengths = new int[512];
            for (int i = 0; i < 256; i++)
            {
                byte b = compressedData[inputPos + i];
                symbolLengths[i * 2] = b & 0x0F;       // even symbol
                symbolLengths[i * 2 + 1] = (b >> 4) & 0x0F; // odd symbol
            }

            inputPos += 256;

            // Build canonical Huffman decode table
            var decodeTable = BuildHuffmanDecodeTable(symbolLengths);

            // Set up bit reader for this chunk
            int chunkInputEnd = Math.Min(inputPos + 65536, compressedData.Length);
            uint bitBuffer = 0;
            int bitsInBuffer = 0;

            // Fill bit buffer (read 16 bits at a time, LSB-first)
            void EnsureBits(int needed)
            {
                while (bitsInBuffer < needed && inputPos + 1 < chunkInputEnd)
                {
                    ushort nextWord = BinaryPrimitives.ReadUInt16LittleEndian(compressedData.AsSpan(inputPos));
                    inputPos += 2;
                    bitBuffer |= (uint)nextWord << bitsInBuffer;
                    bitsInBuffer += 16;
                }
            }

            // Initialize with first 32 bits
            EnsureBits(32);

            // Decode symbols until we fill the output or hit end of chunk
            int chunkOutputEnd = Math.Min(outputPos + 65536, uncompressedSize);

            while (outputPos < chunkOutputEnd)
            {
                // Peek at 15 bits (max Huffman code length)
                uint peekBits = bitBuffer & 0x7FFF;

                // Look up symbol in decode table
                var (symbol, codeLen) = decodeTable[peekBits];

                if (codeLen == 0)
                    throw new InvalidDataException("Invalid Huffman code encountered during decompression.");

                // Consume the bits
                bitBuffer >>= codeLen;
                bitsInBuffer -= codeLen;
                EnsureBits(15);

                if (symbol < 256)
                {
                    // Literal byte
                    output[outputPos++] = (byte)symbol;
                }
                else if (symbol == 256 && inputPos >= chunkInputEnd)
                {
                    // End of chunk marker (symbol 256 at end of input)
                    break;
                }
                else
                {
                    // Match reference: symbol encodes (length_header, distance_log2)
                    int matchSymbol = symbol - 256;
                    int lengthHeader = matchSymbol & 0x0F;
                    int distanceLog2 = matchSymbol >> 4;

                    int distance;
                    if (distanceLog2 == 0)
                    {
                        distance = 1;
                    }
                    else
                    {
                        // Read (distanceLog2 - 1) extra bits for distance
                        int extraBits = distanceLog2 - 1;
                        uint extraDistance = bitBuffer & ((1u << extraBits) - 1);
                        bitBuffer >>= extraBits;
                        bitsInBuffer -= extraBits;
                        EnsureBits(15);

                        distance = (1 << distanceLog2) | (int)extraDistance;
                    }

                    int length;
                    if (lengthHeader == 15)
                    {
                        // Read additional length byte
                        if (inputPos >= chunkInputEnd)
                            break;

                        // The extra length is encoded in the bit stream as an 8-bit value
                        uint extraLen = bitBuffer & 0xFF;
                        bitBuffer >>= 8;
                        bitsInBuffer -= 8;
                        EnsureBits(15);

                        if (extraLen == 255)
                        {
                            // 16-bit length override
                            uint len16 = bitBuffer & 0xFFFF;
                            bitBuffer >>= 16;
                            bitsInBuffer -= 16;
                            EnsureBits(15);

                            if (len16 == 0)
                            {
                                // 32-bit length
                                // Read as two 16-bit values
                                uint lo = bitBuffer & 0xFFFF;
                                bitBuffer >>= 16;
                                bitsInBuffer -= 16;
                                EnsureBits(16);
                                uint hi = bitBuffer & 0xFFFF;
                                bitBuffer >>= 16;
                                bitsInBuffer -= 16;
                                EnsureBits(15);
                                length = (int)((hi << 16) | lo);
                            }
                            else
                            {
                                length = (int)len16;
                            }
                        }
                        else
                        {
                            length = (int)(extraLen + 15);
                        }
                    }
                    else
                    {
                        length = lengthHeader;
                    }

                    // Actual length includes the minimum match of 3
                    length += 3;

                    // Copy match bytes from output
                    int srcPos = outputPos - distance;
                    if (srcPos < 0)
                        throw new InvalidDataException($"LZ77+Huffman: match distance {distance} exceeds output position {outputPos}.");

                    for (int i = 0; i < length && outputPos < uncompressedSize; i++)
                    {
                        output[outputPos++] = output[srcPos + i];
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Build a Huffman decode lookup table from symbol code lengths.
    /// Uses a 15-bit lookup (max code length for XPRESS Huffman is 15).
    /// Each entry stores (symbol, codeLength).
    /// </summary>
    private static (int Symbol, int CodeLen)[] BuildHuffmanDecodeTable(int[] symbolLengths)
    {
        const int tableBits = 15;
        const int tableSize = 1 << tableBits;
        var table = new (int Symbol, int CodeLen)[tableSize];

        // Count the number of codes at each length
        var blCount = new int[16];
        for (int i = 0; i < symbolLengths.Length; i++)
        {
            if (symbolLengths[i] > 0 && symbolLengths[i] <= 15)
                blCount[symbolLengths[i]]++;
        }

        // Compute the starting code value for each length
        var nextCode = new int[16];
        int code = 0;
        for (int bits = 1; bits <= 15; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Assign codes to symbols and fill the table
        for (int symbol = 0; symbol < symbolLengths.Length; symbol++)
        {
            int len = symbolLengths[symbol];
            if (len == 0)
                continue;

            int symbolCode = nextCode[len]++;

            // Fill all table entries that match this prefix
            // The code is stored MSB-first, but we read bits LSB-first,
            // so we need to reverse the bit order
            int reversedCode = ReverseBits(symbolCode, len);
            int fillCount = 1 << (tableBits - len);

            for (int fill = 0; fill < fillCount; fill++)
            {
                int index = reversedCode | (fill << len);
                if (index < tableSize)
                {
                    table[index] = (symbol, len);
                }
            }
        }

        return table;
    }

    /// <summary>
    /// Reverse the lowest 'bitCount' bits of a value.
    /// </summary>
    private static int ReverseBits(int value, int bitCount)
    {
        int result = 0;
        for (int i = 0; i < bitCount; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }

        return result;
    }
}
