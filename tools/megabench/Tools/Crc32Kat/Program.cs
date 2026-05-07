/*
 * CRC32 Known-Answer Test (KAT) - .NET Implementation
 * Compute CRC32 over deterministic header fixture for cross-runtime comparison
 */

using System;
using System.IO;
using System.Text;
using IronConfig.Iupd.Delta;

namespace CRC32_KAT;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("=== CRC32 Known-Answer Test (KAT) - .NET ===\n");

        // Create deterministic header fixture (100 bytes)
        byte[] header = CreateHeaderFixture();

        Console.WriteLine("Fixture created (100 bytes)");
        Console.WriteLine($"  Magic: {Encoding.ASCII.GetString(header, 0, 8)}");
        Console.WriteLine($"  BaseLen: 0x{BitConverter.ToUInt64(header, 12):X016}");
        Console.WriteLine($"  TargetLen: 0x{BitConverter.ToUInt64(header, 20):X016}");
        Console.WriteLine($"  OpCount: {BitConverter.ToUInt32(header, 92)}");
        Console.WriteLine($"  CrcField before compute: [{header[96]:X2} {header[97]:X2} {header[98]:X2} {header[99]:X2}]");

        // Compute CRC32 over header[0:96]
        byte[] headerForCrc = new byte[96];
        Array.Copy(header, 0, headerForCrc, 0, 96);
        uint crc32Computed = ComputeCrc32(headerForCrc);

        Console.WriteLine("\nCRC32 Computation");
        Console.WriteLine("  Input: First 96 bytes of header");
        Console.WriteLine($"  CRC32 Result: 0x{crc32Computed:X8}");
        Console.WriteLine($"  Hex string: {crc32Computed:x8}");

        // Write to file for comparison with native C
        string outputDir = "artifacts/_dev/exec_crc32_01";
        Directory.CreateDirectory(outputDir);

        string crc32File = Path.Combine(outputDir, "dotnet_crc32_kat.txt");
        File.WriteAllText(crc32File, $"{crc32Computed:x8}\n");
        Console.WriteLine($"\n✅ Result written to: {crc32File}");

        // Also write header fixture for reference
        string fixtureFile = Path.Combine(outputDir, "header_fixture.bin");
        File.WriteAllBytes(fixtureFile, header);
        Console.WriteLine($"✅ Header fixture written to: {fixtureFile}");

        return 0;
    }

    static byte[] CreateHeaderFixture()
    {
        byte[] header = new byte[100];

        // Magic: "IRONDEL2"
        Encoding.ASCII.GetBytes("IRONDEL2").CopyTo(header, 0);

        // Version (1 byte at offset 8)
        header[8] = 0x01;

        // Flags (1 byte at offset 9)
        header[9] = 0x00;

        // Reserved (2 bytes at offset 10-11, little-endian)
        BitConverter.GetBytes((ushort)0x0000).CopyTo(header, 10);

        // BaseLen (8 bytes at offset 12-19, little-endian = 524288)
        BitConverter.GetBytes((ulong)524288).CopyTo(header, 12);

        // TargetLen (8 bytes at offset 20-27, little-endian = 524288)
        BitConverter.GetBytes((ulong)524288).CopyTo(header, 20);

        // BaseHash (32 bytes at offset 28-59) - from golden vector case_01
        byte[] baseHash = new byte[] {
            0x18, 0x18, 0xDA, 0x79, 0xF1, 0xCC, 0x8E, 0x6B,
            0x66, 0x5D, 0x7E, 0x98, 0x08, 0xFD, 0xE4, 0x54,
            0x7B, 0x6B, 0xA7, 0x47, 0xE6, 0xD3, 0xF8, 0xD7,
            0x3B, 0x6F, 0x7A, 0x48, 0x08, 0xF7, 0x60, 0xA6
        };
        Array.Copy(baseHash, 0, header, 28, 32);

        // TargetHash (32 bytes at offset 60-91) - from golden vector case_01
        byte[] targetHash = new byte[] {
            0x4D, 0x2F, 0x21, 0x11, 0x8E, 0xBB, 0x0D, 0xC5,
            0x94, 0x01, 0x18, 0x5A, 0x13, 0x06, 0x72, 0xF9,
            0x19, 0xC8, 0x3F, 0x07, 0x9B, 0x3B, 0xFD, 0xC0,
            0x7E, 0xF3, 0xFD, 0xC5, 0x93, 0x99, 0x55, 0x1A
        };
        Array.Copy(targetHash, 0, header, 60, 32);

        // OpCount (4 bytes at offset 92-95, little-endian = 64)
        BitConverter.GetBytes((uint)64).CopyTo(header, 92);

        // CrcField (4 bytes at offset 96-99) - must be zero for computation
        // Already zero-initialized

        return header;
    }

    // Reflected CRC32 computation (matching IronDel2.ComputeCrc32)
    static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }
}
