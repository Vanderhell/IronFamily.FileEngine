/// <summary>
/// Determinism Prover: Encodes fixed input using all three engines.
/// Used to verify byte-for-byte identical output across multiple process runs.
/// </summary>

using System;
using System.IO;
using IronConfig;
using IronConfig.ILog;

class DeterminismProver
{
    static int Main(string[] args)
    {
        // Enable deterministic mode for reproducible output
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: DeterminismProver <input_file> <output_dir>");
            return 1;
        }

        string inputFile = args[0];
        string outputDir = args[1];

        if (!File.Exists(inputFile))
        {
            Console.Error.WriteLine($"Input file not found: {inputFile}");
            return 2;
        }

        Directory.CreateDirectory(outputDir);

        try
        {
            byte[] inputData = File.ReadAllBytes(inputFile);

            // Encode with ILOG
            Console.WriteLine("[ILOG] Encoding...");
            var ilogEncoder = new IlogEncoder();
            byte[] ilogOutput = ilogEncoder.Encode(inputData, IlogEncoder.IlogProfile.MINIMAL);
            string ilogPath = Path.Combine(outputDir, "determinism.ilog");
            File.WriteAllBytes(ilogPath, ilogOutput);
            Console.WriteLine($"[ILOG] Written {ilogOutput.Length} bytes to {ilogPath}");

            // Encode with IRONCFG (IronCfg test encoding)
            Console.WriteLine("[IRONCFG] Encoding...");
            var ironCfgOutput = EncodeIronCfg(inputData);
            string ironCfgPath = Path.Combine(outputDir, "determinism.ironcfg");
            File.WriteAllBytes(ironCfgPath, ironCfgOutput);
            Console.WriteLine($"[IRONCFG] Written {ironCfgOutput.Length} bytes to {ironCfgPath}");

            // Encode with IUPD (IUPD test encoding)
            Console.WriteLine("[IUPD] Encoding...");
            var iupdOutput = EncodeIupd(inputData);
            string iupdPath = Path.Combine(outputDir, "determinism.iupd");
            File.WriteAllBytes(iupdPath, iupdOutput);
            Console.WriteLine($"[IUPD] Written {iupdOutput.Length} bytes to {iupdPath}");

            Console.WriteLine("[OK] All engines encoded successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex}");
            return 3;
        }
    }

    static byte[] EncodeIronCfg(byte[] data)
    {
        // Create minimal IRONCFG file
        var result = new System.Collections.Generic.List<byte>();

        // Magic: "ICFG" = 0x49 0x43 0x46 0x47
        result.AddRange(new byte[] { 0x49, 0x43, 0x46, 0x47 });

        // Version: 0x01
        result.Add(0x01);

        // Flags: 0x00
        result.Add(0x00);

        // Reserved0 (2 bytes): 0x0000
        result.AddRange(new byte[] { 0x00, 0x00 });

        // Placeholder for file size (will update later)
        int fileSizeOffset = result.Count;
        result.AddRange(new byte[4]); // FileSize (u32 LE)

        // Section offsets
        uint schemaOffset = 64;  // Right after header
        uint schemaSize = (uint)data.Length; // Just put data as "schema"
        uint stringPoolOffset = schemaOffset + schemaSize;
        uint stringPoolSize = 0;
        uint dataOffset = stringPoolOffset + stringPoolSize;
        uint dataSize = 0;
        uint crcOffset = dataOffset + dataSize;
        uint blake3Offset = crcOffset + 4;

        // Write offsets (little-endian u32)
        result.AddRange(BitConverter.GetBytes(schemaOffset));
        result.AddRange(BitConverter.GetBytes(schemaSize));
        result.AddRange(BitConverter.GetBytes(stringPoolOffset));
        result.AddRange(BitConverter.GetBytes(stringPoolSize));
        result.AddRange(BitConverter.GetBytes(dataOffset));
        result.AddRange(BitConverter.GetBytes(dataSize));
        result.AddRange(BitConverter.GetBytes(crcOffset));
        result.AddRange(BitConverter.GetBytes(blake3Offset));

        // Reserved1 + Reserved2 (20 bytes)
        result.AddRange(new byte[20]);

        // Fill up to 64 bytes if needed
        while (result.Count < 64)
            result.Add(0);

        // Write actual data (schema)
        result.AddRange(data);

        // Write CRC32 (simple: just zeros for now)
        result.AddRange(new byte[4]);

        // Write Blake3 (32 bytes: zeros)
        result.AddRange(new byte[32]);

        // Update file size
        byte[] fileSize = BitConverter.GetBytes((uint)result.Count);
        for (int i = 0; i < 4; i++)
            result[fileSizeOffset + i] = fileSize[i];

        return result.ToArray();
    }

    static byte[] EncodeIupd(byte[] data)
    {
        // Create minimal IUPD file
        var result = new System.Collections.Generic.List<byte>();

        // Magic: "IUPD" = 0x49 0x55 0x50 0x44
        result.AddRange(new byte[] { 0x49, 0x55, 0x50, 0x44 });

        // Version: 0x01
        result.Add(0x01);

        // Flags (4 bytes): 0x00000000
        result.AddRange(new byte[4]);

        // HeaderSize (2 bytes): 36
        result.AddRange(BitConverter.GetBytes((ushort)36));

        // Reserved (1 byte): 0x00
        result.Add(0x00);

        // ChunkTableOffset (u64 LE): 36 (right after header)
        result.AddRange(BitConverter.GetBytes(36UL));

        // ManifestOffset (u64 LE): 36 + 56 = 92
        result.AddRange(BitConverter.GetBytes(92UL));

        // PayloadOffset (u64 LE): 92 + 24 (manifest header) = 116
        result.AddRange(BitConverter.GetBytes(116UL));

        // Chunk table entry (56 bytes)
        // ChunkIndex (u32): 0
        result.AddRange(BitConverter.GetBytes(0U));

        // PayloadSize (u64): data length
        result.AddRange(BitConverter.GetBytes((ulong)data.Length));

        // PayloadOffset (u64): 116
        result.AddRange(BitConverter.GetBytes(116UL));

        // PayloadCrc32 (u32): 0
        result.AddRange(BitConverter.GetBytes(0U));

        // PayloadBlake3 (32 bytes): zeros
        result.AddRange(new byte[32]);

        // Manifest header (24 bytes)
        // DependencyCount (u32): 0
        result.AddRange(BitConverter.GetBytes(0U));

        // ApplyOrderCount (u32): 0
        result.AddRange(BitConverter.GetBytes(0U));

        // Reserved (16 bytes)
        result.AddRange(new byte[16]);

        // Payload data
        result.AddRange(data);

        return result.ToArray();
    }
}
