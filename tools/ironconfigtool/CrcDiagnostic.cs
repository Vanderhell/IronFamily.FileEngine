using System;
using System.IO;
using IronConfig;

namespace IronConfigTool;

/// <summary>
/// Diagnostic tool to print CRC information from ICFX files
/// </summary>
public static class CrcDiagnostic
{
    public static void PrintCrcInfo(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);
        Console.WriteLine($"File: {filePath}");
        Console.WriteLine($"Size: {data.Length} bytes\n");

        // Parse header
        if (data.Length < 48)
        {
            Console.WriteLine("ERROR: File too small for ICFX header");
            return;
        }

        // Check magic
        string magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        Console.WriteLine($"Magic: {magic}");

        byte flags = data[4];
        Console.WriteLine($"Flags: 0x{flags:X2}");
        Console.WriteLine($"  Bit 0 (Little Endian): {(flags & 0x01)}");
        Console.WriteLine($"  Bit 1 (VSP): {(flags & 0x02) >> 1}");
        Console.WriteLine($"  Bit 2 (CRC): {(flags & 0x04) >> 2}");
        Console.WriteLine($"  Bit 3 (Index): {(flags & 0x08) >> 3}\n");

        // Read offsets
        uint crcOffset = ReadUInt32LE(data, 28);
        uint payloadSize = ReadUInt32LE(data, 32);
        uint dictSize = ReadUInt32LE(data, 36);
        uint vspSize = ReadUInt32LE(data, 40);

        Console.WriteLine($"CRC Offset: {crcOffset}");
        Console.WriteLine($"Payload Size: {payloadSize}");
        Console.WriteLine($"Dictionary Size: {dictSize}");
        Console.WriteLine($"VSP Size: {vspSize}\n");

        // If CRC present
        if (crcOffset > 0 && crcOffset + 4 <= data.Length)
        {
            uint storedCrc = ReadUInt32LE(data, crcOffset);
            Console.WriteLine($"CRC Information:");
            Console.WriteLine($"  Stored CRC: 0x{storedCrc:X8}");
            Console.WriteLine($"  CRC covers bytes [0 .. {crcOffset})");

            // Compute CRC over [0 .. crcOffset)
            byte[] dataToHash = new byte[crcOffset];
            Array.Copy(data, 0, dataToHash, 0, (int)crcOffset);
            uint computedCrc = Crc32Ieee.Compute(dataToHash);

            Console.WriteLine($"  Computed CRC: 0x{computedCrc:X8}");
            Console.WriteLine($"  Match: {(storedCrc == computedCrc ? "YES ✓" : "NO ✗")}");
        }
        else
        {
            Console.WriteLine("No CRC present (or invalid CRC offset)");
        }
    }

    private static uint ReadUInt32LE(byte[] data, uint offset)
    {
        if (offset + 4 > data.Length)
            throw new ArgumentException("Offset out of bounds");

        return (uint)(
            data[(int)offset] |
            (data[(int)(offset + 1)] << 8) |
            (data[(int)(offset + 2)] << 16) |
            (data[(int)(offset + 3)] << 24)
        );
    }
}
