using System;
using System.Collections.Generic;
using Xunit;
using IronConfig.IronCfg;

namespace IronConfig.Tests;

public class IronCfgEncoderTests
{
    [Fact]
    public void TestEncodeDeterminism3x()
    {
        Span<byte> buf1 = new byte[1024];
        Span<byte> buf2 = new byte[1024];
        Span<byte> buf3 = new byte[1024];

        // Create schema
        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "count", FieldType = 0x11, IsRequired = true }
            }
        };

        // Create value
        var fieldVal = new IronCfgUInt64 { Value = 42 };
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, fieldVal }
            }
        };

        // Encode three times
        var err1 = IronCfgEncoder.Encode(root, schema, true, false, buf1, out int size1);
        var err2 = IronCfgEncoder.Encode(root, schema, true, false, buf2, out int size2);
        var err3 = IronCfgEncoder.Encode(root, schema, true, false, buf3, out int size3);

        Assert.True(err1.IsOk);
        Assert.True(err2.IsOk);
        Assert.True(err3.IsOk);
        Assert.Equal(size1, size2);
        Assert.Equal(size2, size3);

        Assert.Equal(buf1.Slice(0, size1).ToArray(), buf2.Slice(0, size2).ToArray());
        Assert.Equal(buf2.Slice(0, size2).ToArray(), buf3.Slice(0, size3).ToArray());
    }

    [Fact]
    public void TestEncodeCrc32Valid()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "value", FieldType = 0x10, IsRequired = true }
            }
        };

        var fieldVal = new IronCfgInt64 { Value = -12345 };
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, fieldVal }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
        Assert.True(err.IsOk);

        // Validate the file
        var memory = new ReadOnlyMemory<byte>(buf.Slice(0, size).ToArray());
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);
        Assert.True(view.HasCrc32());
    }

    [Fact]
    public void TestEncoderWritesCurrentHeaderVersion()
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
                { 0, new IronCfgInt64 { Value = 7 } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
        Assert.True(err.IsOk);
        Assert.True(size >= 64);
        Assert.Equal(IronCfgHeader.VERSION, buf[4]);
    }

    [Fact]
    public void TestFloatNormalization()
    {
        Span<byte> buf1 = new byte[1024];
        Span<byte> buf2 = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "temp", FieldType = 0x12, IsRequired = true }
            }
        };

        // Encode with +0.0
        var root1 = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgFloat64 { Value = 0.0 } }
            }
        };

        // Encode with -0.0
        var root2 = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgFloat64 { Value = -0.0 } }
            }
        };

        IronCfgEncoder.Encode(root1, schema, true, false, buf1, out int size1);
        IronCfgEncoder.Encode(root2, schema, true, false, buf2, out int size2);

        Assert.Equal(size1, size2);
        Assert.Equal(buf1.Slice(0, size1).ToArray(), buf2.Slice(0, size2).ToArray());
    }

    [Fact]
    public void TestNanRejection()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "data", FieldType = 0x12, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgFloat64 { Value = double.NaN } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
        Assert.Equal(IronCfgErrorCode.InvalidFloat, err.Code);
    }

    [Fact]
    public void TestFieldOrderingDeterminism()
    {
        Span<byte> buf1 = new byte[2048];
        Span<byte> buf2 = new byte[2048];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "alpha", FieldType = 0x11, IsRequired = true },
                new IronCfgField { FieldId = 1, FieldName = "beta", FieldType = 0x11, IsRequired = true },
                new IronCfgField { FieldId = 2, FieldName = "gamma", FieldType = 0x11, IsRequired = true }
            }
        };

        // Create two root objects with same values
        var root1 = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = 1 } },
                { 1, new IronCfgUInt64 { Value = 2 } },
                { 2, new IronCfgUInt64 { Value = 3 } }
            }
        };

        var root2 = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = 1 } },
                { 1, new IronCfgUInt64 { Value = 2 } },
                { 2, new IronCfgUInt64 { Value = 3 } }
            }
        };

        IronCfgEncoder.Encode(root1, schema, true, false, buf1, out int size1);
        IronCfgEncoder.Encode(root2, schema, true, false, buf2, out int size2);

        Assert.Equal(size1, size2);
        Assert.Equal(buf1.Slice(0, size1).ToArray(), buf2.Slice(0, size2).ToArray());
    }

    [Fact]
    public void TestMultipleFieldTypes()
    {
        Span<byte> buf1 = new byte[4096];
        Span<byte> buf2 = new byte[4096];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "b", FieldType = 0x01, IsRequired = true },
                new IronCfgField { FieldId = 1, FieldName = "f", FieldType = 0x12, IsRequired = true },
                new IronCfgField { FieldId = 2, FieldName = "i", FieldType = 0x10, IsRequired = true },
                new IronCfgField { FieldId = 3, FieldName = "s", FieldType = 0x20, IsRequired = true },
                new IronCfgField { FieldId = 4, FieldName = "u", FieldType = 0x11, IsRequired = true }
            }
        };

        // Create roots with mixed types
        var root1 = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgBool { Value = true } },
                { 1, new IronCfgFloat64 { Value = 3.14 } },
                { 2, new IronCfgInt64 { Value = -999 } },
                { 3, new IronCfgString { Value = "hello" } },
                { 4, new IronCfgUInt64 { Value = 123456 } }
            }
        };

        var root2 = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgBool { Value = true } },
                { 1, new IronCfgFloat64 { Value = 3.14 } },
                { 2, new IronCfgInt64 { Value = -999 } },
                { 3, new IronCfgString { Value = "hello" } },
                { 4, new IronCfgUInt64 { Value = 123456 } }
            }
        };

        var err1 = IronCfgEncoder.Encode(root1, schema, true, false, buf1, out int size1);
        var err2 = IronCfgEncoder.Encode(root2, schema, true, false, buf2, out int size2);

        Assert.True(err1.IsOk);
        Assert.True(err2.IsOk);
        Assert.Equal(size1, size2);
        Assert.Equal(buf1.Slice(0, size1).ToArray(), buf2.Slice(0, size2).ToArray());
    }

    [Fact]
    public void TestEncodingWithoutCrc32()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "x", FieldType = 0x11, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgUInt64 { Value = 999 } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, false, false, buf, out int size);
        Assert.True(err.IsOk);

        // Verify file header
        var memory = new ReadOnlyMemory<byte>(buf.Slice(0, size).ToArray());
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);
        Assert.False(view.HasCrc32());
    }

    [Fact]
    public void TestEncodingWithBlake3_StrictValidationPasses()
    {
        Span<byte> buf = new byte[2048];

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
                { 0, new IronCfgString { Value = "hello blake3" } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: true, computeBlake3: true, buf, out int size);
        Assert.True(err.IsOk);

        var memory = new ReadOnlyMemory<byte>(buf.Slice(0, size).ToArray());
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);
        Assert.True(view.HasBlake3());

        var strictErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.True(strictErr.IsOk, $"ValidateStrict failed: {strictErr.Code} at offset {strictErr.Offset}");
    }

    [Fact]
    public void TestEncoderRejectsReservedSchemaType()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "value", FieldType = 0x13, IsRequired = true }
            }
        };

        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgInt64 { Value = 42 } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out _);
        Assert.Equal(IronCfgErrorCode.InvalidTypeCode, err.Code);
    }

    [Fact]
    public void TestEncoderRejectsOversizedString()
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
                { 0, new IronCfgString { Value = new string('x', 16 * 1024 * 1024 + 1) } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out _);
        Assert.Equal(IronCfgErrorCode.LimitExceeded, err.Code);
    }

    [Fact]
    public void TestEncoderRejectsOversizedBytes()
    {
        Span<byte> buf = new byte[1024];

        var schema = new IronCfgSchema
        {
            Fields = new List<IronCfgField>
            {
                new IronCfgField { FieldId = 0, FieldName = "blob", FieldType = 0x22, IsRequired = true }
            }
        };

        var oversized = new byte[16 * 1024 * 1024 + 1];
        var root = new IronCfgObject
        {
            Fields = new SortedDictionary<uint, IronCfgValue?>
            {
                { 0, new IronCfgBytes { Data = oversized } }
            }
        };

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out _);
        Assert.Equal(IronCfgErrorCode.LimitExceeded, err.Code);
    }

    [Fact]
    public void TestEncoderRejectsTooManySchemaFields()
    {
        Span<byte> buf = new byte[1024];

        var fields = new List<IronCfgField>(65537);
        for (uint i = 0; i <= 65536; i++)
        {
            fields.Add(new IronCfgField { FieldId = i, FieldName = $"f{i}", FieldType = 0x10, IsRequired = false });
        }

        var schema = new IronCfgSchema { Fields = fields };
        var root = new IronCfgObject();

        var err = IronCfgEncoder.Encode(root, schema, computeCrc32: false, computeBlake3: false, buf, out _);
        Assert.Equal(IronCfgErrorCode.LimitExceeded, err.Code);
    }

    [Fact]
    public void TestLargeConfiguration()
    {
        // Test with ~1 MB configuration (multiple large strings)
        Span<byte> buf = new byte[1_500_000];

        var fields = new List<IronCfgField>();
        var rootFields = new SortedDictionary<uint, IronCfgValue?>();

        // Create 100 large string fields (~10 KB each)
        for (uint i = 0; i < 100; i++)
        {
            fields.Add(new IronCfgField { FieldId = i, FieldName = $"field_{i}", FieldType = 0x20, IsRequired = true });

            // Create a 10 KB string for each field
            var largString = new string('X', 10240);
            rootFields.Add(i, new IronCfgString { Value = largString });
        }

        var schema = new IronCfgSchema { Fields = fields };
        var root = new IronCfgObject { Fields = rootFields };

        // Encode large config
        var err = IronCfgEncoder.Encode(root, schema, true, false, buf, out int size);
        Assert.True(err.IsOk);
        Assert.True(size > 1_000_000, $"Expected large config >1MB, got {size} bytes");
        Assert.True(size < buf.Length, "Encoded size must fit in buffer");

        // Verify it can be read back
        var memory = new ReadOnlyMemory<byte>(buf.Slice(0, size).ToArray());
        var openErr = IronCfgValidator.Open(memory, out var view);
        Assert.True(openErr.IsOk);

        // Verify strict validation passes on large config
        var validateErr = IronCfgValidator.ValidateStrict(memory, view);
        Assert.True(validateErr.IsOk);
    }
}
