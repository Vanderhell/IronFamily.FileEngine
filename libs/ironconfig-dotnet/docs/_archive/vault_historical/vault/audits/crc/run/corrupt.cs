using System;
using System.IO;

class Program
{
    static void Main()
    {
        string file = "./_crc_impl/run/min_corrupt.icfg";
        byte[] data = File.ReadAllBytes(file);
        
        // Corrupt a byte in the middle of the file
        int midpoint = data.Length / 2;
        data[midpoint] ^= 0x01;  // Flip one bit
        
        File.WriteAllBytes(file, data);
        Console.WriteLine($"Corrupted byte at offset {midpoint} (value: 0x{data[midpoint]:X2})");
    }
}
