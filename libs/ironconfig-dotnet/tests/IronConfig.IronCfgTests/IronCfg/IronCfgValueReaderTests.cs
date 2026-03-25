using System;
using Xunit;
using Xunit.Abstractions;
using IronConfig.IronCfg;

namespace IronConfig.Tests.IronCfg;

public class IronCfgValueReaderTests
{
    private readonly ITestOutputHelper _output;

    public IronCfgValueReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }
    private static string GetTestVectorPath(string datasetName)
    {
        return TestVectorHelper.GetIronCfgTestVectorPath(datasetName);
    }

    private static void WriteUInt32LE(byte[] buf, int off, uint val)
    {
        buf[off] = (byte)(val & 0xFF);
        buf[off + 1] = (byte)((val >> 8) & 0xFF);
        buf[off + 2] = (byte)((val >> 16) & 0xFF);
        buf[off + 3] = (byte)((val >> 24) & 0xFF);
    }

    private static byte[] CreateStringPoolStringIdFile()
    {
        var fieldName = System.Text.Encoding.UTF8.GetBytes("value");
        var pooledValue = System.Text.Encoding.UTF8.GetBytes("pooled");

        byte[] schema = new byte[1 + 1 + 1 + 1 + fieldName.Length];
        int schemaOffset = 0;
        schema[schemaOffset++] = 0x01;
        schema[schemaOffset++] = 0x00;
        schema[schemaOffset++] = 0x20; // schema string type, payload may still use StringId
        schema[schemaOffset++] = (byte)fieldName.Length;
        Array.Copy(fieldName, 0, schema, schemaOffset, fieldName.Length);

        byte[] pool = new byte[1 + pooledValue.Length];
        pool[0] = (byte)pooledValue.Length;
        Array.Copy(pooledValue, 0, pool, 1, pooledValue.Length);

        byte[] data = new byte[] { 0x40, 0x01, 0x00, 0x21, 0x00 };

        uint headerSize = 64;
        uint schemaStart = headerSize;
        uint poolStart = schemaStart + (uint)schema.Length;
        uint dataStart = poolStart + (uint)pool.Length;
        uint fileSize = dataStart + (uint)data.Length;

        byte[] file = new byte[fileSize];
        Array.Clear(file);
        file[0] = 0x49; file[1] = 0x43; file[2] = 0x46; file[3] = 0x47;
        file[4] = 2;
        file[5] = 0;
        WriteUInt32LE(file, 8, fileSize);
        WriteUInt32LE(file, 12, schemaStart);
        WriteUInt32LE(file, 16, (uint)schema.Length);
        WriteUInt32LE(file, 20, poolStart);
        WriteUInt32LE(file, 24, (uint)pool.Length);
        WriteUInt32LE(file, 28, dataStart);
        WriteUInt32LE(file, 32, (uint)data.Length);

        Array.Copy(schema, 0, file, schemaStart, schema.Length);
        Array.Copy(pool, 0, file, poolStart, pool.Length);
        Array.Copy(data, 0, file, dataStart, data.Length);
        return file;
    }

    private static byte[] CreateVersion1ArrayFile()
    {
        var fieldName = System.Text.Encoding.UTF8.GetBytes("items");

        byte[] schema = new byte[1 + 1 + 1 + 1 + fieldName.Length];
        int schemaOffset = 0;
        schema[schemaOffset++] = 0x01;
        schema[schemaOffset++] = 0x00;
        schema[schemaOffset++] = 0x30;
        schema[schemaOffset++] = (byte)fieldName.Length;
        Array.Copy(fieldName, 0, schema, schemaOffset, fieldName.Length);

        byte[] data =
        [
            0x40, 0x01,
            0x00,
            0x30, 0x01,
            0x10, 0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        uint headerSize = 64;
        uint schemaStart = headerSize;
        uint dataStart = schemaStart + (uint)schema.Length;
        uint fileSize = dataStart + (uint)data.Length;

        byte[] file = new byte[fileSize];
        Array.Clear(file);
        file[0] = 0x49; file[1] = 0x43; file[2] = 0x46; file[3] = 0x47;
        file[4] = 1;
        file[5] = 0;
        WriteUInt32LE(file, 8, fileSize);
        WriteUInt32LE(file, 12, schemaStart);
        WriteUInt32LE(file, 16, (uint)schema.Length);
        WriteUInt32LE(file, 20, 0);
        WriteUInt32LE(file, 24, 0);
        WriteUInt32LE(file, 28, dataStart);
        WriteUInt32LE(file, 32, (uint)data.Length);

        Array.Copy(schema, 0, file, schemaStart, schema.Length);
        Array.Copy(data, 0, file, dataStart, data.Length);
        return file;
    }

    private static byte[] CreateArrayElementSchemaFile(byte version)
    {
        var itemsName = System.Text.Encoding.UTF8.GetBytes("items");
        var childName = System.Text.Encoding.UTF8.GetBytes("name");

        byte[] schema =
        [
            0x01,
            0x00,
            0x30,
            (byte)itemsName.Length,
            .. itemsName,
            0x01,
            0x00,
            0x20,
            (byte)childName.Length,
            .. childName
        ];

        byte[] data =
        [
            0x40, 0x01,
            0x00,
            0x30, 0x01,
            0x40, 0x01,
            0x00,
            0x20, 0x01, (byte)'x'
        ];

        uint headerSize = 64;
        uint schemaStart = headerSize;
        uint dataStart = schemaStart + (uint)schema.Length;
        uint fileSize = dataStart + (uint)data.Length;

        byte[] file = new byte[fileSize];
        Array.Clear(file);
        file[0] = 0x49; file[1] = 0x43; file[2] = 0x46; file[3] = 0x47;
        file[4] = version;
        file[5] = 0;
        WriteUInt32LE(file, 8, fileSize);
        WriteUInt32LE(file, 12, schemaStart);
        WriteUInt32LE(file, 16, (uint)schema.Length);
        WriteUInt32LE(file, 20, 0);
        WriteUInt32LE(file, 24, 0);
        WriteUInt32LE(file, 28, dataStart);
        WriteUInt32LE(file, 32, (uint)data.Length);

        Array.Copy(schema, 0, file, schemaStart, schema.Length);
        Array.Copy(data, 0, file, dataStart, data.Length);
        return file;
    }

    private void PrintObjectFieldKeys(ReadOnlyMemory<byte> buffer, IronCfgView view, string label)
    {
        // Helper to print object field keys and schema info
        var dataErr = view.GetRoot(out var dataBlock);
        if (!dataErr.IsOk)
        {
            _output.WriteLine($"{label}: Failed to get root");
            return;
        }

        if (dataBlock.Span.Length == 0)
        {
            _output.WriteLine($"{label}: Empty root block");
            return;
        }

        byte rootType = dataBlock.Span[0];
        if (rootType != 0x40)
        {
            _output.WriteLine($"{label}: Root is not object (type 0x{rootType:X2})");
            return;
        }

        // Print schema info
        var schemaErr = view.GetSchema(out var schemaBlock);
        if (schemaErr.IsOk && schemaBlock.Length > 0)
        {
            var schema = schemaBlock.Span;
            var countErr = DecodeVarUInt32(schema, 0, out var schemaFieldCount, out var countBytes);
            if (countErr.IsOk)
            {
                _output.WriteLine($"{label}: Schema has {schemaFieldCount} fields");
                uint schemaOffset = countBytes;
                for (uint sf = 0; sf < schemaFieldCount && sf < 10; sf++)
                {
                    var idErr = DecodeVarUInt32(schema, schemaOffset, out var schemaFieldId, out var idBytes);
                    if (!idErr.IsOk) break;
                    schemaOffset += idBytes;

                    if (schemaOffset >= schema.Length) break;
                    byte schemaTypeCode = schema[(int)schemaOffset];
                    schemaOffset++;

                    string schemaName = "";
                    if (schemaTypeCode >= 0x1C)
                    {
                        var nameErr = DecodeVarUInt32(schema, schemaOffset, out var nameLen, out var nameBytes);
                        if (nameErr.IsOk)
                        {
                            schemaOffset += nameBytes;
                            if (schemaOffset + nameLen <= schema.Length)
                            {
                                schemaName = System.Text.Encoding.UTF8.GetString(schema.Slice((int)schemaOffset, (int)nameLen));
                                schemaOffset += nameLen;
                            }
                        }
                    }
                    _output.WriteLine($"  Schema Field {sf}: ID={schemaFieldId}, Type=0x{schemaTypeCode:X2}, Name=\"{schemaName}\"");
                }
            }
        }

        // Print data field info
        uint offset = view.Header.DataOffset;
        uint countOffset = offset + 1;
        var dataCountErr = DecodeVarUInt32(buffer.Span, countOffset, out var fieldCount, out var dataCountBytes);
        if (!dataCountErr.IsOk)
        {
            _output.WriteLine($"{label}: Failed to decode field count");
            return;
        }

        uint fieldDataOffset = countOffset + dataCountBytes;
        _output.WriteLine($"{label}: Data object with {fieldCount} fields");

        for (uint f = 0; f < fieldCount && f < 10; f++)  // Limit to first 10 for diagnostics
        {
            if (fieldDataOffset >= buffer.Length) break;

            var idErr = DecodeVarUInt32(buffer.Span, fieldDataOffset, out var fieldId, out var idBytes);
            if (!idErr.IsOk) break;
            fieldDataOffset += idBytes;

            if (fieldDataOffset >= buffer.Length) break;
            byte fieldType = buffer.Span[(int)fieldDataOffset];
            fieldDataOffset++;

            _output.WriteLine($"  Data Field {f}: ID={fieldId}, Type=0x{fieldType:X2}");

            // Skip field value (simplified)
            if (fieldType != 0x40 && fieldType != 0x30)
            {
                var skipErr = SkipValueForDiag(buffer.Span, fieldDataOffset, fieldType, out var nextOffset);
                if (!skipErr.IsOk) break;
                fieldDataOffset = nextOffset;
            }
            else
            {
                break;  // Don't dig into nested structures
            }
        }
    }

    private void PrintRecordsFirstElementSchema(ReadOnlyMemory<byte> buffer, IronCfgView view, string label)
    {
        // Navigate to records[0] and print its field schema
        var dataErr = view.GetRoot(out var dataBlock);
        if (!dataErr.IsOk)
        {
            _output.WriteLine($"{label}: Failed to get root");
            return;
        }

        uint offset = view.Header.DataOffset;
        uint countOffset = offset + 1;
        var countErr = DecodeVarUInt32(buffer.Span, countOffset, out var fieldCount, out var countBytes);
        if (!countErr.IsOk)
        {
            _output.WriteLine($"{label}: Failed to decode root field count");
            return;
        }

        uint fieldDataOffset = countOffset + countBytes;

        // Find records field (ID=3)
        uint recordsOffset = 0;
        bool foundRecords = false;
        for (uint f = 0; f < fieldCount; f++)
        {
            if (fieldDataOffset >= buffer.Length) break;

            var idErr = DecodeVarUInt32(buffer.Span, fieldDataOffset, out var fieldId, out var idBytes);
            if (!idErr.IsOk) break;
            fieldDataOffset += idBytes;

            if (fieldDataOffset >= buffer.Length) break;
            byte fieldType = buffer.Span[(int)fieldDataOffset];
            fieldDataOffset++;

            if (fieldId == 3 && fieldType == 0x30)
            {
                recordsOffset = fieldDataOffset - 1;
                foundRecords = true;
                break;
            }

            var skipErr = SkipValueForDiag(buffer.Span, fieldDataOffset, fieldType, out var nextOffset);
            if (!skipErr.IsOk) break;
            fieldDataOffset = nextOffset;
        }

        if (!foundRecords)
        {
            _output.WriteLine($"{label}: records field not found");
            return;
        }

        // Navigate into records array
        uint arrayLenOffset = recordsOffset + 1;
        var lenErr = DecodeVarUInt32(buffer.Span, arrayLenOffset, out var arrayLen, out var lenBytes);
        if (!lenErr.IsOk)
        {
            _output.WriteLine($"{label}: Failed to decode array length");
            return;
        }

        if (arrayLen == 0)
        {
            _output.WriteLine($"{label}: records array is empty");
            return;
        }

        uint elemOffset = arrayLenOffset + lenBytes;

        // Read first element (type code + value)
        if (elemOffset >= buffer.Length)
        {
            _output.WriteLine($"{label}: Buffer bounds violation");
            return;
        }

        byte elemType = buffer.Span[(int)elemOffset];
        if (elemType != 0x40)
        {
            _output.WriteLine($"{label}: records[0] is not an object");
            return;
        }

        elemOffset++;

        // Now read the object fields
        if (elemOffset >= buffer.Length)
        {
            _output.WriteLine($"{label}: Buffer bounds violation");
            return;
        }

        var objCountErr = DecodeVarUInt32(buffer.Span, elemOffset, out var objFieldCount, out var objCountBytes);
        if (!objCountErr.IsOk)
        {
            _output.WriteLine($"{label}: Failed to decode object field count");
            return;
        }

        elemOffset += objCountBytes;
        _output.WriteLine($"{label}: Object with {objFieldCount} fields");

        for (uint f = 0; f < objFieldCount && f < 10; f++)
        {
            if (elemOffset >= buffer.Length) break;

            var idErr = DecodeVarUInt32(buffer.Span, elemOffset, out var fieldId, out var idBytes);
            if (!idErr.IsOk) break;
            elemOffset += idBytes;

            if (elemOffset >= buffer.Length) break;
            byte fieldType = buffer.Span[(int)elemOffset];
            elemOffset++;

            _output.WriteLine($"  Field {f}: ID={fieldId}, Type=0x{fieldType:X2}");
        }
    }

    private IronCfgError SkipValueForDiag(ReadOnlySpan<byte> buffer, uint offset, byte typeCode, out uint nextOffset)
    {
        nextOffset = offset;
        switch (typeCode)
        {
            case 0x00:
            case 0x01:
            case 0x02:
                nextOffset = offset;
                return IronCfgError.Ok;
            case 0x10:
            case 0x11:
            case 0x12:
                if (offset + 8 > buffer.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
                nextOffset = offset + 8;
                return IronCfgError.Ok;
            case 0x20:
            case 0x22:
            {
                var lenErr = DecodeVarUInt32(buffer, offset, out var len, out var lenBytes);
                if (!lenErr.IsOk) return lenErr;
                if (offset + lenBytes + len > buffer.Length)
                    return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
                nextOffset = offset + lenBytes + len;
                return IronCfgError.Ok;
            }
            default:
                return new IronCfgError(IronCfgErrorCode.InvalidTypeCode, offset);
        }
    }

    private static IronCfgError DecodeVarUInt32(ReadOnlySpan<byte> buffer, uint offset, out uint value, out uint bytes)
    {
        value = 0;
        bytes = 0;
        if (offset >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);

        byte b = buffer[(int)offset];
        if ((b & 0x80) == 0)
        {
            value = b;
            bytes = 1;
            return IronCfgError.Ok;
        }

        if (offset + 1 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b1 = buffer[(int)offset + 1];
        if ((b1 & 0x80) == 0)
        {
            value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7));
            bytes = 2;
            return IronCfgError.Ok;
        }

        if (offset + 2 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b2 = buffer[(int)offset + 2];
        if ((b2 & 0x80) == 0)
        {
            value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14));
            bytes = 3;
            return IronCfgError.Ok;
        }

        if (offset + 3 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b3 = buffer[(int)offset + 3];
        if ((b3 & 0x80) == 0)
        {
            value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21));
            bytes = 4;
            return IronCfgError.Ok;
        }

        if (offset + 4 >= buffer.Length) return new IronCfgError(IronCfgErrorCode.BoundsViolation, offset);
        byte b4 = buffer[(int)offset + 4];
        value = (uint)((b & 0x7F) | ((b1 & 0x7F) << 7) | ((b2 & 0x7F) << 14) | ((b3 & 0x7F) << 21) | ((b4 & 0x0F) << 28));
        bytes = 5;
        return IronCfgError.Ok;
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractBoolFromSmallVector()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk, $"Failed to open: {openErr.Code} at {openErr.Offset}");

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk, $"Failed strict validation: {strictErr.Code}");

        // Diagnostic: print root object fields
        PrintObjectFieldKeys(data, view, "Root object");

        // Diagnostic: print records[0] object schema
        PrintRecordsFirstElementSchema(data, view, "records[0] object");

        // Extract: records[0].enabled (true) - root is object with 'records' array
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgFieldIdPath(2)
        };

        var err = IronCfgValueReader.GetBool(data, view, path, out var enabled);
        Assert.True(err.IsOk, $"Failed to extract: {err.Code} at {err.Offset}");
        Assert.True(enabled);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractUInt64FromSmallVector()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk, $"ValidateStrict failed: {strictErr.Code} at offset {strictErr.Offset}");

        // Extract: records[0].id (0) - root is object with 'records' array
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgFieldIdPath(0)
        };

        var err = IronCfgValueReader.GetUInt64(data, view, path, out var id);
        Assert.True(err.IsOk);
        Assert.Equal(0UL, id);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractStringFromSmallVector()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Extract: records[0].name ("config_a") - root is object with 'records' array
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgKeyPath("name")
        };

        var err = IronCfgValueReader.GetString(data, view, path, out var nameBytes);
        Assert.True(err.IsOk);
        var name = System.Text.Encoding.UTF8.GetString(nameBytes.Span);
        Assert.Equal("config_a", name);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractObjectFieldCountFromSmallVector()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Extract root object field count (should have "records" field)
        var path = Array.Empty<IronCfgPath>();

        var err = IronCfgValueReader.GetObjectFieldCount(data, view, path, out var fieldCount);
        Assert.True(err.IsOk);
        Assert.True(fieldCount > 0);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractMissingFieldReturnsError()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Try to extract non-existent field
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgKeyPath("nonexistent_field")
        };

        var err = IronCfgValueReader.GetString(data, view, path, out _);
        Assert.False(err.IsOk);
        Assert.Equal(IronCfgErrorCode.UnknownField, err.Code);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractWrongTypeReturnsError()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Try to extract string as uint64
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgKeyPath("name")
        };

        var err = IronCfgValueReader.GetUInt64(data, view, path, out _);
        Assert.False(err.IsOk);
        Assert.Equal(IronCfgErrorCode.FieldTypeMismatch, err.Code);
    }

    [Fact]
    public void ExtractStringFromStringPoolReference()
    {
        var data = CreateStringPoolStringIdFile();
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk, $"ValidateStrict failed: {strictErr.Code} at offset {strictErr.Offset}");

        var path = new IronCfgPath[] { new IronCfgKeyPath("value") };
        var err = IronCfgValueReader.GetString(data, view, path, out var nameBytes);
        Assert.True(err.IsOk, $"GetString failed: {err.Code} at offset {err.Offset}");
        Assert.Equal("pooled", System.Text.Encoding.UTF8.GetString(nameBytes.Span));
    }

    [Fact]
    public void ExtractArrayLengthFromVersion1ArraySchema()
    {
        var data = CreateVersion1ArrayFile();
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk, $"ValidateStrict failed: {strictErr.Code} at offset {strictErr.Offset}");

        var path = new IronCfgPath[] { new IronCfgKeyPath("items") };
        var err = IronCfgValueReader.GetArrayLength(data, view, path, out var length);
        Assert.True(err.IsOk, $"GetArrayLength failed: {err.Code} at offset {err.Offset}");
        Assert.Equal(1u, length);
    }

    [Fact]
    public void ReaderCacheDoesNotReuseV2ElementSchemaForV1File()
    {
        IronCfgValueReader.ResetCaches();

        var v2Data = CreateArrayElementSchemaFile(version: 2);
        var v2OpenErr = IronCfgValidator.Open(v2Data, out var v2View);
        Assert.True(v2OpenErr.IsOk);

        var v2Path = new IronCfgPath[] { new IronCfgKeyPath("items"), new IronCfgIndexPath(0), new IronCfgKeyPath("name") };
        var v2Err = IronCfgValueReader.GetString(v2Data, v2View, v2Path, out var v2Value);
        Assert.True(v2Err.IsOk, $"V2 GetString failed: {v2Err.Code} at offset {v2Err.Offset}");
        Assert.Equal("x", System.Text.Encoding.UTF8.GetString(v2Value.Span));

        var v1Data = CreateArrayElementSchemaFile(version: 1);
        var v1OpenErr = IronCfgValidator.Open(v1Data, out var v1View);
        Assert.True(v1OpenErr.IsOk);

        var v1StrictErr = IronCfgValidator.ValidateStrict(v1Data, v1View);
        Assert.True(v1StrictErr.IsOk, $"V1 ValidateStrict failed: {v1StrictErr.Code} at offset {v1StrictErr.Offset}");

        var v1Err = IronCfgValueReader.GetString(v1Data, v1View, v2Path, out _);
        Assert.Equal(IronCfgErrorCode.UnknownField, v1Err.Code);
    }

    [Fact]
    public void ReaderSchemaParseErrorUsesAbsoluteSchemaOffset()
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

        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var path = new IronCfgPath[] { new IronCfgKeyPath("value") };
        var err = IronCfgValueReader.GetString(data, view, path, out _);
        Assert.Equal(IronCfgErrorCode.NonMinimalVarint, err.Code);
        Assert.Equal(schemaOffset, err.Offset);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractMediumVectorRecords()
    {
        // Load medium.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("medium"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Extract: records[0].user_id (0)
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgFieldIdPath(0)
        };

        var err = IronCfgValueReader.GetUInt64(data, view, path, out var userId);
        Assert.True(err.IsOk);
        Assert.Equal(0UL, userId);

        // Extract: records[9].user_id (9)
        path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(9),
            new IronCfgFieldIdPath(0)
        };

        err = IronCfgValueReader.GetUInt64(data, view, path, out userId);
        Assert.True(err.IsOk);
        Assert.Equal(9UL, userId);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void ExtractLargeVectorFloat()
    {
        // Load large.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("large"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Extract: records[0].price (9.99)
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgFieldIdPath(3)
        };

        var err = IronCfgValueReader.GetFloat64(data, view, path, out var price);
        Assert.True(err.IsOk);
        Assert.Equal(9.99, price, 2);

        // Extract: records[34].price (43.99 = 9.99 + 34)
        path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(34),
            new IronCfgFieldIdPath(3)
        };

        err = IronCfgValueReader.GetFloat64(data, view, path, out price);
        Assert.True(err.IsOk);
        Assert.Equal(43.99, price, 2);
    }

    [Fact]
    [Trait("Category", "Vectors")]
    public void BothNavigationModesWork()
    {
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("medium"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        var pathByName = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(5),
            new IronCfgKeyPath("name")
        };

        var pathById = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(5),
            new IronCfgFieldIdPath(0)
        };

        var errName = IronCfgValueReader.GetString(data, view, pathByName, out var usernameBytes);
        Assert.True(errName.IsOk, $"GetString failed");
        var username = System.Text.Encoding.UTF8.GetString(usernameBytes.Span);
        Assert.Equal("user_005", username);

        var errId = IronCfgValueReader.GetUInt64(data, view, pathById, out var userId);
        Assert.True(errId.IsOk);
        Assert.Equal(5UL, userId);
    }
}
