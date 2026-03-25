using System;
using System.IO;
using System.IO.Hashing;

class VerifyBothLocations
{
    static void Main()
    {
        Console.WriteLine("=== Verifying golden files in BOTH locations ===\n");

        Check(@"C:\Users\vande\Desktop\Bjv\bjv\vectors\icf2\golden_small.icf2", 242);
        Check(@"C:\Users\vande\Desktop\Bjv\bjv\libs\bjv-dotnet\vectors\icf2\golden_small.icf2", 242);
    }

    static void Check(string path, uint crcOffset)
    {
        byte[] data = File.ReadAllBytes(path);
        uint stored = BitConverter.ToUInt32(data, (int)crcOffset);

        byte[] range = new byte[crcOffset];
        Array.Copy(data, 0, range, 0, (int)crcOffset);

        Span<byte> hash = stackalloc byte[4];
        Crc32.Hash(range, hash);
        uint computed = (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));

        Console.WriteLine($"File: {Path.GetFileName(path)}");
        Console.WriteLine($"  Stored:   0x{stored:X8}");
        Console.WriteLine($"  Computed: 0x{computed:X8}");
        Console.WriteLine($"  Match: {(stored == computed ? "YES âś“" : "NO âś—")}\n");
    }
}
