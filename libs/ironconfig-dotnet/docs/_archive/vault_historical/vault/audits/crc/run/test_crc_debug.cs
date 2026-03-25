using System;
using System.IO.Hashing;
using System.Text;

// Test the endianness of CRC32
byte[] data = Encoding.ASCII.GetBytes("123456789");

// Test 1: System.IO.Hashing.Crc32 output
Span<byte> hash = stackalloc byte[4];
Crc32.Hash(data, hash);

Console.WriteLine($"Hash bytes: {string.Join(", ", hash.ToArray().Select(b => $"0x{b:X2}"))}");

// Expected: 0xCBF43926
// If hash returns big-endian: [0xCB, 0xF4, 0x39, 0x26]
// If hash returns little-endian: [0x26, 0x39, 0xF4, 0xCB]

uint littleEndian = (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));
uint bigEndian = (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]);

Console.WriteLine($"Interpreting as little-endian: 0x{littleEndian:X8}");
Console.WriteLine($"Interpreting as big-endian: 0x{bigEndian:X8}");
Console.WriteLine($"Expected: 0xCBF43926");
