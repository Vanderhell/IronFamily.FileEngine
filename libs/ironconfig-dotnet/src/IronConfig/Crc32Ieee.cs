using System;
using System.IO.Hashing;

namespace IronConfig;

/// <summary>
/// IEEE CRC32 computation helper using System.IO.Hashing.Crc32
/// </summary>
public static class Crc32Ieee
{
    /// <summary>
    /// Compute CRC32 (IEEE) checksum for the given data
    /// </summary>
    /// <param name="data">Data to compute CRC for</param>
    /// <returns>CRC32 value as uint</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Crc32.HashToUInt32(data);
    }
}
