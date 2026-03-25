using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace IronConfig.ILog;

/// <summary>
/// LZ4 + LZ77 hybrid compressor for ILOG L3 ARCHIVE layer.
/// Implements lossless compression without external dependencies.
/// </summary>
public static class IlogCompressor
{
    private const int HashTableBits = 16;
    private const int HashTableSize = 1 << HashTableBits;
    private const int MinMatchLength = 4;
    private const int MaxMatchOffset = 65535;
    private const int Lz77Lookahead = 512;
    private const int CompressorVersion = 0x01;

    private struct CompressToken
    {
        public int LiteralOffset { get; set; }
        public int LiteralLength { get; set; }
        public int MatchOffset { get; set; }
        public int MatchLength { get; set; }
    }

    /// <summary>
    /// Compress data using LZ4 + LZ77 hybrid strategy.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        if (input.Length == 0)
            return Array.Empty<byte>();

        // Determine compression strategy based on input size
        var tokens = new List<CompressToken>();
        var hashTable = new int[HashTableSize];
        Array.Fill(hashTable, -1);

        // Phase 1: LZ4 pass
        EncodeLz4Pass(input, tokens, hashTable, MinMatchLength);

        // Phase 2: LZ77 optimization for larger data
        if (input.Length >= 1024)
        {
            EncodeLz77OptimizePass(input, tokens, Lz77Lookahead);
        }

        // Encode tokens into compressed stream
        var compressedData = EncodeTokenStream(input, tokens);

        // Build final payload with headers
        return BuildPayload(input, compressedData);
    }

    /// <summary>
    /// Attempt to decompress L3 payload.
    /// </summary>
    public static bool TryDecompress(ReadOnlySpan<byte> compressed, out byte[] output, out string? error)
    {
        output = Array.Empty<byte>();
        error = null;

        if (compressed.Length < 6)
        {
            error = "Compressed data too short for header";
            return false;
        }

        try
        {
            int offset = 0;

            // Read ArchiveVersion (u8)
            byte archiveVersion = compressed[offset++];
            if (archiveVersion != 0x01)
            {
                error = $"Unsupported archive version: {archiveVersion}";
                return false;
            }

            // Read CompressionType (u8)
            byte compressionType = compressed[offset++];
            if (compressionType != 0x02 && compressionType != 0x00)
            {
                error = $"Unsupported compression type: {compressionType}";
                return false;
            }

            // Read CompressedSize (u32 LE)
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(compressed.Slice(offset, 4));
            offset += 4;

            // If not compressed, return original data
            if (compressionType == 0x00)
            {
                // Payload is just the raw data
                if (offset + compressedSize > compressed.Length)
                {
                    error = "Compressed data truncated";
                    return false;
                }
                output = compressed.Slice(offset, (int)compressedSize).ToArray();
                return true;
            }

            // Read CompressorVersion (u8)
            if (offset >= compressed.Length)
            {
                error = "Unexpected end of data at compressor version";
                return false;
            }
            byte compressorVersion = compressed[offset++];

            // Read StreamFlags (u8)
            if (offset >= compressed.Length)
            {
                error = "Unexpected end of data at stream flags";
                return false;
            }
            byte streamFlags = compressed[offset++];

            // Read OriginalSize (u32 LE)
            if (offset + 4 > compressed.Length)
            {
                error = "Unexpected end of data at original size";
                return false;
            }
            uint originalSize = BinaryPrimitives.ReadUInt32LittleEndian(compressed.Slice(offset, 4));
            offset += 4;

            // Decompress token stream
            output = DecodeTokenStream(compressed.Slice(offset), (int)originalSize);

            if (output.Length != originalSize)
            {
                error = $"Decompressed size mismatch: expected {originalSize}, got {output.Length}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Decompression error: {ex.Message}";
            return false;
        }
    }

    private static void EncodeLz4Pass(ReadOnlySpan<byte> input, List<CompressToken> tokens,
        int[] hashTable, int minMatch)
    {
        int pos = 0;
        int matchStart = -1;
        int literalStart = 0;

        while (pos < input.Length)
        {
            // Look for a match
            int bestMatchLen = 0;
            int bestMatchOffset = 0;

            if (pos + minMatch <= input.Length)
            {
                var matchPos = FindMatch(input, pos, hashTable, minMatch, MaxMatchOffset);
                if (matchPos.matchLength >= minMatch)
                {
                    bestMatchLen = matchPos.matchLength;
                    bestMatchOffset = matchPos.offset;
                }
            }

            if (bestMatchLen >= minMatch)
            {
                // Emit one LZ4-style sequence: literals + optional match in the same token.
                if (matchStart >= 0)
                {
                    tokens.Add(new CompressToken
                    {
                        LiteralOffset = matchStart,
                        LiteralLength = pos - matchStart,
                        MatchOffset = bestMatchOffset,
                        MatchLength = bestMatchLen
                    });
                }
                else
                {
                    tokens.Add(new CompressToken
                    {
                        LiteralOffset = 0,
                        LiteralLength = 0,
                        MatchOffset = bestMatchOffset,
                        MatchLength = bestMatchLen
                    });
                }

                // Update hash table for matched region
                for (int i = 0; i < bestMatchLen && pos + i < input.Length - minMatch; i++)
                {
                    int hash = Hash4(input, pos + i);
                    hashTable[hash] = pos + i;
                }

                pos += bestMatchLen;
                matchStart = -1;
                literalStart = pos;
            }
            else
            {
                // Update hash table
                if (pos < input.Length - minMatch)
                {
                    int hash = Hash4(input, pos);
                    hashTable[hash] = pos;
                }

                if (matchStart < 0)
                    matchStart = pos;

                pos++;
            }
        }

        // Emit final literal run
        if (matchStart >= 0 && matchStart < input.Length)
        {
            tokens.Add(new CompressToken
            {
                LiteralOffset = matchStart,
                LiteralLength = input.Length - matchStart,
                MatchOffset = 0,
                MatchLength = 0
            });
        }
    }

    private static void EncodeLz77OptimizePass(ReadOnlySpan<byte> input, List<CompressToken> tokens, int lookahead)
    {
        // LZ77 optimization: lazy matching to find better matches at lookahead positions
        // FIX: Replaced O(n * 512) nested loop with O(n) lazy matching (2 positions only)
        // Strategy: for each token, check current position vs +1 position (lazy matching)
        // only if current match is short (<12 bytes). This avoids 512 comparisons per position.

        var hashTable = new int[HashTableSize];
        Array.Fill(hashTable, -1);

        // Build complete hash table from all positions (not just first 1000)
        for (int pos = 0; pos < input.Length - MinMatchLength; pos++)
        {
            int hash = Hash4(input, pos);
            hashTable[hash] = pos;
        }

        for (int tokenIdx = 0; tokenIdx < tokens.Count; tokenIdx++)
        {
            var token = tokens[tokenIdx];

            // Only optimize tokens with literals followed by matches (or pure literals)
            if (token.LiteralLength == 0)
                continue;

            int literalEnd = token.LiteralOffset + token.LiteralLength;

            // Lazy matching: only check current position and +1 (not all 512)
            if (literalEnd < input.Length - MinMatchLength)
            {
                // Check CURRENT position (offset=0)
                int currentMatch = FindMatch(input, literalEnd, hashTable, MinMatchLength, MaxMatchOffset).matchLength;

                // Lazy matching: only check +1 if current is short
                if (currentMatch > 0 && currentMatch < MinMatchLength + 8 &&
                    literalEnd + 1 < input.Length - MinMatchLength)
                {
                    int nextMatch = FindMatch(input, literalEnd + 1, hashTable, MinMatchLength, MaxMatchOffset).matchLength;

                    // If next position is significantly better, skip current and take next
                    if (nextMatch > currentMatch + 4)
                    {
                        currentMatch = 0;  // Skip current, the next position will be processed later
                    }
                }

                // If we found a good match at current position, extract it
                if (currentMatch >= MinMatchLength)
                {
                    var match = FindMatch(input, literalEnd, hashTable, MinMatchLength, MaxMatchOffset);

                    // Extract this match
                    if (token.MatchLength > 0)
                    {
                        // Have existing match - create new match token after literals
                        tokens[tokenIdx] = token;
                        tokens.Insert(tokenIdx + 1, new CompressToken
                        {
                            MatchOffset = match.offset,
                            MatchLength = match.matchLength,
                            LiteralOffset = 0,
                            LiteralLength = 0
                        });
                    }
                    else
                    {
                        // Pure literal - extend it and add match
                        token.LiteralLength = 0;
                        tokens[tokenIdx] = token;
                        tokens.Insert(tokenIdx + 1, new CompressToken
                        {
                            MatchOffset = match.offset,
                            MatchLength = match.matchLength,
                            LiteralOffset = 0,
                            LiteralLength = 0
                        });
                    }
                }
            }
        }
    }

    private static (int matchLength, int offset) FindMatch(ReadOnlySpan<byte> input, int pos,
        int[] hashTable, int minMatch, int maxOffset)
    {
        if (pos + minMatch > input.Length)
            return (0, 0);

        int hash = Hash4(input, pos);
        int matchPos = hashTable[hash];

        int bestLen = 0;
        int bestOffset = 0;

        if (matchPos >= 0 && pos - matchPos > 0 && pos - matchPos <= maxOffset)
        {
            int maxLen = Math.Min(258, input.Length - pos); // LZ4 style max literal/match length
            int len = 0;

            while (len < maxLen && input[matchPos + len] == input[pos + len])
            {
                len++;
            }

            if (len >= minMatch)
            {
                bestLen = len;
                bestOffset = pos - matchPos;
            }
        }

        return (bestLen, bestOffset);
    }

    private static int Hash4(ReadOnlySpan<byte> data, int pos)
    {
        if (pos + 3 >= data.Length)
            return 0;

        // Multiply-shift hash for 4-byte sequences
        uint val = (uint)data[pos] | ((uint)data[pos + 1] << 8) |
                   ((uint)data[pos + 2] << 16) | ((uint)data[pos + 3] << 24);

        // Knuth's multiplicative hash
        const uint Prime = 2654435761u;
        return (int)((val * Prime) >> (32 - HashTableBits));
    }

    private static byte[] EncodeTokenStream(ReadOnlySpan<byte> input, List<CompressToken> tokens)
    {
        var output = new List<byte>(Math.Max(64, input.Length));

        foreach (var token in tokens)
        {
            if (token.LiteralLength > 0)
            {
                // Encode literal
                byte literalLen = (byte)Math.Min(15, token.LiteralLength);
                byte matchLen = token.MatchLength > 0 ? (byte)Math.Min(15, token.MatchLength - MinMatchLength) : (byte)0;

                output.Add((byte)((literalLen << 4) | matchLen));

                // Extended literal length
                if (token.LiteralLength >= 15)
                {
                    int extLen = token.LiteralLength - 15;
                    while (extLen >= 255)
                    {
                        output.Add(0xFF);
                        extLen -= 255;
                    }
                    output.Add((byte)extLen);
                }

                // Literal bytes
                output.AddRange(input.Slice(token.LiteralOffset, token.LiteralLength).ToArray());

                // Match offset (if there's a match)
                if (token.MatchLength > 0)
                {
                    output.Add((byte)(token.MatchOffset & 0xFF));
                    output.Add((byte)((token.MatchOffset >> 8) & 0xFF));

                    // Extended match length
                    if (token.MatchLength - MinMatchLength >= 15)
                    {
                        int extLen = token.MatchLength - MinMatchLength - 15;
                        while (extLen >= 255)
                        {
                            output.Add(0xFF);
                            extLen -= 255;
                        }
                        output.Add((byte)extLen);
                    }
                }
            }
            else if (token.MatchLength > 0)
            {
                // Pure match (no preceding literal)
                byte matchLen = (byte)Math.Min(15, token.MatchLength - MinMatchLength);
                output.Add(matchLen);

                output.Add((byte)(token.MatchOffset & 0xFF));
                output.Add((byte)((token.MatchOffset >> 8) & 0xFF));

                // Extended match length
                if (token.MatchLength - MinMatchLength >= 15)
                {
                    int extLen = token.MatchLength - MinMatchLength - 15;
                    while (extLen >= 255)
                    {
                        output.Add(0xFF);
                        extLen -= 255;
                    }
                    output.Add((byte)extLen);
                }
            }
        }

        return output.ToArray();
    }

    private static byte[] DecodeTokenStream(ReadOnlySpan<byte> tokenStream, int originalSize)
    {
        if (originalSize <= 0)
            return Array.Empty<byte>();

        byte[] output = new byte[originalSize];
        int outPos = 0;
        int offset = 0;

        while (offset < tokenStream.Length && outPos < originalSize)
        {
            byte token = tokenStream[offset++];

            // Decode literal length
            int literalLen = (token >> 4) & 0x0F;
            if (literalLen == 15)
            {
                while (offset < tokenStream.Length && tokenStream[offset] == 0xFF)
                {
                    literalLen += 255;
                    offset++;
                }
                if (offset < tokenStream.Length)
                    literalLen += tokenStream[offset++];
            }

            // Copy literals
            if (literalLen > 0)
            {
                if (offset + literalLen > tokenStream.Length)
                    break;

                int copyLen = Math.Min(literalLen, originalSize - outPos);
                tokenStream.Slice(offset, copyLen).CopyTo(output.AsSpan(outPos, copyLen));
                outPos += copyLen;
                offset += literalLen;
            }

            // End-of-stream literal run. In our encoder only final sequence may omit match.
            if (offset >= tokenStream.Length || outPos >= originalSize)
                break;

            // Read match offset
            if (offset + 2 > tokenStream.Length)
                break;

            int matchOffset = tokenStream[offset] | (tokenStream[offset + 1] << 8);
            offset += 2;
            if (matchOffset <= 0 || matchOffset > outPos)
                break;

            int matchLen = (token & 0x0F) + MinMatchLength;
            if ((token & 0x0F) == 15)
            {
                while (offset < tokenStream.Length && tokenStream[offset] == 0xFF)
                {
                    matchLen += 255;
                    offset++;
                }
                if (offset < tokenStream.Length)
                    matchLen += tokenStream[offset++];
            }

            int copyLenMatch = Math.Min(matchLen, originalSize - outPos);
            int matchPos = outPos - matchOffset;

            // Fast path for non-overlapping copies, fallback for overlapping LZ copies.
            if (matchOffset >= copyLenMatch)
            {
                output.AsSpan(matchPos, copyLenMatch).CopyTo(output.AsSpan(outPos, copyLenMatch));
            }
            else
            {
                for (int i = 0; i < copyLenMatch; i++)
                    output[outPos + i] = output[matchPos + i];
            }

            outPos += copyLenMatch;
        }

        if (outPos == originalSize)
            return output;

        var truncated = new byte[outPos];
        Buffer.BlockCopy(output, 0, truncated, 0, outPos);
        return truncated;
    }

    private static byte[] BuildPayload(ReadOnlySpan<byte> input, byte[] compressedData)
    {
        // Check if compression is worth it
        if (compressedData.Length + 12 >= input.Length)
        {
            // Fallback to uncompressed (CompressionType = 0x00)
            var result = new byte[6 + input.Length];
            result[0] = 0x01; // ArchiveVersion
            result[1] = 0x00; // CompressionType = no compression
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2, 4), (uint)input.Length);
            input.CopyTo(result.AsSpan(6));
            return result;
        }

        // Build compressed payload
        var output = new List<byte>(compressedData.Length + 12);

        // ArchiveVersion (u8)
        output.Add(0x01);

        // CompressionType (u8) = 0x02 (hybrid LZ4+LZ77)
        output.Add(0x02);

        // CompressedSize (u32 LE) - size of compressed token stream
        output.Add((byte)((compressedData.Length) & 0xFF));
        output.Add((byte)((compressedData.Length >> 8) & 0xFF));
        output.Add((byte)((compressedData.Length >> 16) & 0xFF));
        output.Add((byte)((compressedData.Length >> 24) & 0xFF));

        // CompressorVersion (u8)
        output.Add(CompressorVersion);

        // StreamFlags (u8) - bit 0: LZ77 pass used (not used in initial version)
        output.Add(0x00);

        // OriginalSize (u32 LE)
        output.Add((byte)((input.Length) & 0xFF));
        output.Add((byte)((input.Length >> 8) & 0xFF));
        output.Add((byte)((input.Length >> 16) & 0xFF));
        output.Add((byte)((input.Length >> 24) & 0xFF));

        // Token stream
        output.AddRange(compressedData);

        return output.ToArray();
    }
}
