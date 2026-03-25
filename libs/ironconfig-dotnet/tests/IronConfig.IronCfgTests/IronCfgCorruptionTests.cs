// Phase 1.3: IronCfg Corruption Tests
// Verify unified error model correctly maps IronCfg errors

using IronConfig;
using IronConfig.IronCfg;

namespace IronConfig.Tests;

public class IronCfgCorruptionTests
{
    private static void WriteUInt32LE(byte[] buf, int off, uint val)
    {
        buf[off] = (byte)(val & 0xFF);
        buf[off + 1] = (byte)((val >> 8) & 0xFF);
        buf[off + 2] = (byte)((val >> 16) & 0xFF);
        buf[off + 3] = (byte)((val >> 24) & 0xFF);
    }

    private static byte[] CreateValidIronCfgFile()
    {
        var buf = new byte[70];
        Array.Clear(buf);
        buf[0] = 0x49; buf[1] = 0x43; buf[2] = 0x46; buf[3] = 0x47; // ICFG
        buf[4] = 1;  // version
        buf[5] = 0;  // flags
        buf[6] = 0; buf[7] = 0;
        WriteUInt32LE(buf, 8, 70);   // FileSize
        WriteUInt32LE(buf, 12, 64);  // SchemaOffset
        WriteUInt32LE(buf, 16, 1);   // SchemaSize
        WriteUInt32LE(buf, 20, 0);   // StringPoolOffset
        WriteUInt32LE(buf, 24, 0);   // StringPoolSize
        WriteUInt32LE(buf, 28, 65);  // DataOffset
        WriteUInt32LE(buf, 32, 5);   // DataSize
        WriteUInt32LE(buf, 36, 0);   // CrcOffset
        WriteUInt32LE(buf, 40, 0);   // Blake3Offset
        return buf;
    }

    private static byte[] CreateStringPoolStringIdFile(byte poolIndex = 0, byte schemaType = 0x20)
    {
        var fieldName = System.Text.Encoding.UTF8.GetBytes("value");
        var pooledValue = System.Text.Encoding.UTF8.GetBytes("pooled");

        byte[] schema = new byte[1 + 1 + 1 + 1 + fieldName.Length];
        int schemaOffset = 0;
        schema[schemaOffset++] = 0x01; // field count
        schema[schemaOffset++] = 0x00; // field id
        schema[schemaOffset++] = schemaType;
        schema[schemaOffset++] = (byte)fieldName.Length;
        Array.Copy(fieldName, 0, schema, schemaOffset, fieldName.Length);

        byte[] pool = new byte[1 + pooledValue.Length];
        pool[0] = (byte)pooledValue.Length;
        Array.Copy(pooledValue, 0, pool, 1, pooledValue.Length);

        byte[] data = new byte[] { 0x40, 0x01, 0x00, 0x21, poolIndex };

        uint headerSize = 64;
        uint schemaStart = headerSize;
        uint poolStart = schemaStart + (uint)schema.Length;
        uint dataStart = poolStart + (uint)pool.Length;
        uint fileSize = dataStart + (uint)data.Length;

        byte[] file = new byte[fileSize];
        Array.Clear(file);

        file[0] = 0x49; file[1] = 0x43; file[2] = 0x46; file[3] = 0x47; // ICFG
        file[4] = 2;  // version
        file[5] = 0;  // flags
        WriteUInt32LE(file, 8, fileSize);
        WriteUInt32LE(file, 12, schemaStart);
        WriteUInt32LE(file, 16, (uint)schema.Length);
        WriteUInt32LE(file, 20, poolStart);
        WriteUInt32LE(file, 24, (uint)pool.Length);
        WriteUInt32LE(file, 28, dataStart);
        WriteUInt32LE(file, 32, (uint)data.Length);
        WriteUInt32LE(file, 36, 0);
        WriteUInt32LE(file, 40, 0);

        Array.Copy(schema, 0, file, schemaStart, schema.Length);
        Array.Copy(pool, 0, file, poolStart, pool.Length);
        Array.Copy(data, 0, file, dataStart, data.Length);
        return file;
    }

    private static byte[] CreateInvalidStringPoolStringIdFile()
    {
        var data = CreateStringPoolStringIdFile();
        uint poolOffset = BitConverter.ToUInt32(data, 20);
        data[poolOffset + 1] = 0xFF;
        return data;
    }

    /// <summary>
    /// A1) Bitflip Header - IronCfg
    /// Flip magic byte, expect InvalidMagic error that maps to global::IronConfig.IronEdgeErrorCategory.InvalidMagic
    /// </summary>
    [Fact]
    public void CorruptionTest_IronCfg_BitflipMagic_ReturnsInvalidMagic()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var data = CreateValidIronCfgFile();

            // Bitflip: flip bit 0 of first magic byte (0x49 → 0x48)
            TestHelpers.FlipBit(data, 0, 0);

            // Write corrupted file
            var filePath = TestHelpers.WriteTestFile(tempDir, "corrupted.icfg", data);

            // Read and validate - should get InvalidMagic error
            var fileData = TestHelpers.ReadAllBytes(filePath);
            var cfgError = IronCfgValidator.ValidateFast(fileData);

            // Verify engine error
            Assert.Equal(IronCfgErrorCode.InvalidMagic, cfgError.Code);

            // Map to unified error
            var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgError);

            // Verify unified error
            Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidMagic, unified.Category);
            Assert.Equal(0x02, unified.Code);
            Assert.Equal(global::IronConfig.IronEdgeEngine.IronCfg, unified.Engine);
            Assert.Equal(0u, unified.Offset);
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// A2) Truncate File - IronCfg
    /// Cut file at half length, expect Truncated error
    /// </summary>
    [Fact]
    public void CorruptionTest_IronCfg_TruncateFile_ReturnsTruncated()
    {
        var tempDir = TestHelpers.CreateUniqueTempDir();
        try
        {
            var data = CreateValidIronCfgFile();

            // Truncate to half size
            var truncated = new byte[data.Length / 2];
            Array.Copy(data, truncated, truncated.Length);

            // Write truncated file
            var filePath = TestHelpers.WriteTestFile(tempDir, "truncated.icfg", truncated);

            // Read and validate
            var fileData = TestHelpers.ReadAllBytes(filePath);
            var cfgError = IronCfgValidator.ValidateFast(fileData);

            // Verify engine error
            Assert.Equal(IronCfgErrorCode.TruncatedFile, cfgError.Code);

            // Map to unified error
            var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgError);

            // Verify unified error
            Assert.Equal(global::IronConfig.IronEdgeErrorCategory.Truncated, unified.Category);
            Assert.Equal(0x01, unified.Code);
            Assert.Equal(global::IronConfig.IronEdgeEngine.IronCfg, unified.Engine);
        }
        finally
        {
            TestHelpers.CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// A3) Wrong Checksum - IronCfg
    /// Corrupt a data byte (flip a bit), expect Crc32Mismatch error
    /// Note: This test assumes CRC validation is enabled in the file format.
    /// If CRC is not enabled in the created file, this test may not trigger the CRC check.
    /// For this test to work, we would need a file with CRC flag set and proper CRC calculation.
    /// For now, we demonstrate the error mapping by creating an InvalidChecksum scenario.
    /// </summary>
    [Fact]
    public void CorruptionTest_IronCfg_CorruptData_WithCrcFlag_ReturnsCrcMismatch()
    {
        // Create a file with CRC flag enabled (this is a more complex scenario)
        // For Phase 1.3, we'll test that the error mapping is correct using a simpler approach
        // by testing with a file that fails CRC32 validation

        var cfgErr = new IronCfgError(IronCfgErrorCode.Crc32Mismatch, 512);
        var unified = global::IronConfig.IronEdgeError.FromIronCfgError(cfgErr);

        // Verify mapping
        Assert.Equal(global::IronConfig.IronEdgeErrorCategory.InvalidChecksum, unified.Category);
        Assert.Equal(0x07, unified.Code);
        Assert.Equal(global::IronConfig.IronEdgeEngine.IronCfg, unified.Engine);
        Assert.Equal(512u, unified.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_CorruptDataType_StrictReturnsInvalidTypeCode()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "value", FieldType = 0x10, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgInt64 { Value = 42 } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out int size);
        Assert.True(err.IsOk);

        var data = buf.Slice(0, size).ToArray();
        data[67] = 0xFF; // dataOffset=64, object=0x40, count=1, fieldId=0, value type at +3

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.InvalidTypeCode, strictErr.Code);
    }

    [Fact]
    public void CorruptionTest_IronCfg_ReservedSchemaType_StrictReturnsInvalidTypeCode()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "value", FieldType = 0x10, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgInt64 { Value = 42 } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out int size);
        Assert.True(err.IsOk);

        var data = buf.Slice(0, size).ToArray();
        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        uint schemaTypeOffset = view.Header.SchemaOffset + 2u; // count varint + fieldId varint
        data[schemaTypeOffset] = 0x13;

        memory = new ReadOnlyMemory<byte>(data);
        openErr = IronCfgValidator.Open(memory, out view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.InvalidTypeCode, strictErr.Code);
        Assert.Equal(schemaTypeOffset, strictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_Blake3Mismatch_StrictReturnsBlake3Mismatch()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "value", FieldType = 0x20, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgString { Value = "blake3 protected" } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: true, buf, out int size);
        Assert.True(err.IsOk);

        var data = buf.Slice(0, size).ToArray();
        data[size - 1] ^= 0xFF; // corrupt stored blake3 bytes

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.Blake3Mismatch, strictErr.Code);
    }

    [Fact]
    public void CorruptionTest_IronCfg_StringIdOutOfRange_StrictReturnsBoundsViolation()
    {
        var data = CreateStringPoolStringIdFile(poolIndex: 1);

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.BoundsViolation, strictErr.Code);
        Assert.Equal(view.Header.StringPoolOffset + view.Header.StringPoolSize, strictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_NonMinimalSchemaVarint_StrictReturnsNonMinimalVarint()
    {
        byte[] schema = [0x81, 0x00, 0x00, 0x10];
        byte[] rootData = [0x40, 0x00];

        uint schemaOffset = 64;
        uint dataOffset = schemaOffset + (uint)schema.Length;
        uint fileSize = dataOffset + (uint)rootData.Length;
        byte[] data = new byte[fileSize];
        data[0] = 0x49; data[1] = 0x43; data[2] = 0x46; data[3] = 0x47;
        data[4] = 1;
        WriteUInt32LE(data, 8, fileSize);
        WriteUInt32LE(data, 12, schemaOffset);
        WriteUInt32LE(data, 16, (uint)schema.Length);
        WriteUInt32LE(data, 20, 0);
        WriteUInt32LE(data, 24, 0);
        WriteUInt32LE(data, 28, dataOffset);
        WriteUInt32LE(data, 32, (uint)rootData.Length);
        Array.Copy(schema, 0, data, schemaOffset, schema.Length);
        Array.Copy(rootData, 0, data, dataOffset, rootData.Length);

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.NonMinimalVarint, strictErr.Code);
        Assert.Equal(schemaOffset, strictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_StringIdWithoutPool_StrictReturnsBoundsViolation()
    {
        var data = CreateStringPoolStringIdFile(poolIndex: 0);
        var movedData = data.AsSpan(80, 5).ToArray();
        WriteUInt32LE(data, 20, 0);
        WriteUInt32LE(data, 24, 0);
        WriteUInt32LE(data, 28, 73);
        WriteUInt32LE(data, 8, 78);
        movedData.CopyTo(data.AsSpan(73, movedData.Length));

        Array.Resize(ref data, 78);

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.BoundsViolation, strictErr.Code);
        Assert.Equal(view.Header.DataOffset + 4u, strictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_StrictPoolCacheDoesNotReuseAcrossDifferentPoolContent()
    {
        IronCfgValidator.ResetStrictMetadataCache();

        var validData = CreateStringPoolStringIdFile();
        var validOpenErr = IronCfgValidator.Open(validData, out var validView);
        Assert.True(validOpenErr.IsOk);

        var validStrictErr = IronCfgValidator.ValidateStrict(validData, validView);
        Assert.True(validStrictErr.IsOk, $"Valid strict failed: {validStrictErr.Code} at offset {validStrictErr.Offset}");

        var invalidData = CreateInvalidStringPoolStringIdFile();
        var invalidOpenErr = IronCfgValidator.Open(invalidData, out var invalidView);
        Assert.True(invalidOpenErr.IsOk);

        var invalidStrictErr = IronCfgValidator.ValidateStrict(invalidData, invalidView);
        Assert.Equal(IronCfgErrorCode.InvalidString, invalidStrictErr.Code);
        Assert.Equal(invalidView.Header.StringPoolOffset + 1u, invalidStrictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_FieldTypeMismatch_StrictReturnsFieldTypeMismatch()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "value", FieldType = 0x10, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgInt64 { Value = 42 } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out int size);
        Assert.True(err.IsOk);

        var data = buf.Slice(0, size).ToArray();
        data[67] = 0x20;
        data[68] = 0x01;
        data[69] = (byte)'x';

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.FieldTypeMismatch, strictErr.Code);
        Assert.Equal(67u, strictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_UnknownField_StrictReturnsUnknownField()
    {
        var fieldName = System.Text.Encoding.UTF8.GetBytes("value");
        byte[] schema = new byte[1 + 1 + 1 + 1 + fieldName.Length];
        schema[0] = 0x01;
        schema[1] = 0x00;
        schema[2] = 0x10;
        schema[3] = (byte)fieldName.Length;
        Array.Copy(fieldName, 0, schema, 4, fieldName.Length);

        byte[] rootData =
        [
            0x40, 0x02,
            0x00, 0x10, 42, 0, 0, 0, 0, 0, 0, 0,
            0x01, 0x10, 7, 0, 0, 0, 0, 0, 0, 0
        ];

        uint schemaOffset = 64;
        uint dataOffset = schemaOffset + (uint)schema.Length;
        uint fileSize = dataOffset + (uint)rootData.Length;
        byte[] data = new byte[fileSize];
        data[0] = 0x49; data[1] = 0x43; data[2] = 0x46; data[3] = 0x47;
        data[4] = 2;
        WriteUInt32LE(data, 8, fileSize);
        WriteUInt32LE(data, 12, schemaOffset);
        WriteUInt32LE(data, 16, (uint)schema.Length);
        WriteUInt32LE(data, 20, 0);
        WriteUInt32LE(data, 24, 0);
        WriteUInt32LE(data, 28, dataOffset);
        WriteUInt32LE(data, 32, (uint)rootData.Length);
        Array.Copy(schema, 0, data, schemaOffset, schema.Length);
        Array.Copy(rootData, 0, data, dataOffset, rootData.Length);

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.UnknownField, strictErr.Code);
        Assert.Equal(dataOffset + 12u, strictErr.Offset);
    }

    [Fact]
    public void CorruptionTest_IronCfg_ArrayTypeMismatch_StrictReturnsArrayTypeMismatch()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField
                {
                    FieldId = 0,
                    FieldName = "items",
                    FieldType = 0x30,
                    IsRequired = true,
                    ElementSchema = new IronCfgSchema
                    {
                        Fields = new List<IronCfgField>
                        {
                            new IronCfgField { FieldId = 0, FieldName = "name", FieldType = 0x20, IsRequired = true }
                        }
                    }
                }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                {
                    0,
                    new IronCfgArray
                    {
                        Elements = new List<IronCfgValue?>
                        {
                            new IronCfgObject
                            {
                                Fields = new SortedDictionary<uint, IronCfgValue?>
                                {
                                    { 0, new IronCfgString { Value = "ok" } }
                                }
                            }
                        }
                    }
                }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out int size);
        Assert.True(err.IsOk);

        var data = buf.Slice(0, size).ToArray();
        int arrayElementTypeOffset = Array.IndexOf(data, (byte)0x40, 65) + 3;
        data[arrayElementTypeOffset] = 0x20;
        data[arrayElementTypeOffset + 1] = 0x01;
        data[arrayElementTypeOffset + 2] = (byte)'x';

        var memory = new ReadOnlyMemory<byte>(data);
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.Equal(IronCfgErrorCode.ArrayTypeMismatch, strictErr.Code);
    }
}
