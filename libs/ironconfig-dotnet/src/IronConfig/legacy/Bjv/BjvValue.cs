using System;
using System.Collections.Generic;

namespace IronConfig;

/// <summary>
/// Represents a BJV value (for encoding)
/// </summary>
public abstract class BjvValueNode
{
    public abstract void Accept(IBjvValueVisitor visitor);
}

public class BjvNullValue : BjvValueNode
{
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitNull(this);
}

public class BjvBoolValue : BjvValueNode
{
    public bool Value { get; set; }
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitBool(this);
}

public class BjvInt64Value : BjvValueNode
{
    public long Value { get; set; }
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitInt64(this);
}

public class BjvUInt64Value : BjvValueNode
{
    public ulong Value { get; set; }
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitUInt64(this);
}

public class BjvFloat64Value : BjvValueNode
{
    public double Value { get; set; }
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitFloat64(this);
}

public class BjvStringValue : BjvValueNode
{
    public string Value { get; set; } = string.Empty;
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitString(this);
}

public class BjvBytesValue : BjvValueNode
{
    public byte[] Value { get; set; } = Array.Empty<byte>();
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitBytes(this);
}

public class BjvArrayValue : BjvValueNode
{
    public List<BjvValueNode> Elements { get; } = new();
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitArray(this);
}

public class BjvObjectValue : BjvValueNode
{
    // Key -> Value (key is actual string, not keyId)
    public Dictionary<string, BjvValueNode> Fields { get; } = new(StringComparer.Ordinal);
    public override void Accept(IBjvValueVisitor visitor) => visitor.VisitObject(this);
}

/// <summary>
/// Visitor pattern for encoding BJV values
/// </summary>
public interface IBjvValueVisitor
{
    void VisitNull(BjvNullValue value);
    void VisitBool(BjvBoolValue value);
    void VisitInt64(BjvInt64Value value);
    void VisitUInt64(BjvUInt64Value value);
    void VisitFloat64(BjvFloat64Value value);
    void VisitString(BjvStringValue value);
    void VisitBytes(BjvBytesValue value);
    void VisitArray(BjvArrayValue value);
    void VisitObject(BjvObjectValue value);
}
