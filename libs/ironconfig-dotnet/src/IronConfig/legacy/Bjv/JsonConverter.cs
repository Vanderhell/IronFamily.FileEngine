using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace IronConfig;

/// <summary>
/// Convert between JSON and BJV value nodes
/// </summary>
public static class JsonConverter
{
    /// <summary>
    /// Convert JsonElement to BJV value node
    /// </summary>
    public static BjvValueNode FromJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => new BjvNullValue(),
            JsonValueKind.True => new BjvBoolValue { Value = true },
            JsonValueKind.False => new BjvBoolValue { Value = false },
            JsonValueKind.Number => ConvertNumber(element),
            JsonValueKind.String => new BjvStringValue { Value = element.GetString() ?? string.Empty },
            JsonValueKind.Array => ConvertArray(element),
            JsonValueKind.Object => ConvertObject(element),
            _ => throw new InvalidOperationException($"Unknown JSON kind: {element.ValueKind}")
        };
    }

    private static BjvValueNode ConvertNumber(JsonElement element)
    {
        string rawText = element.GetRawText();

        // Try to parse as integer first
        if (long.TryParse(rawText, NumberStyles.Any, CultureInfo.InvariantCulture, out long intVal))
        {
            if (intVal >= 0)
                return new BjvUInt64Value { Value = (ulong)intVal };
            else
                return new BjvInt64Value { Value = intVal };
        }

        // Parse as double
        double doubleVal = element.GetDouble();
        return new BjvFloat64Value { Value = doubleVal };
    }

    private static BjvArrayValue ConvertArray(JsonElement element)
    {
        var arr = new BjvArrayValue();
        foreach (var item in element.EnumerateArray())
        {
            arr.Elements.Add(FromJson(item));
        }
        return arr;
    }

    private static BjvObjectValue ConvertObject(JsonElement element)
    {
        var obj = new BjvObjectValue();
        foreach (var prop in element.EnumerateObject())
        {
            obj.Fields[prop.Name] = FromJson(prop.Value);
        }
        return obj;
    }

    /// <summary>
    /// Convert BJV value node to JSON string
    /// </summary>
    public static string ToJson(BjvValueNode root)
    {
        var converter = new BjvToJsonConverter();
        root.Accept(converter);
        return converter.GetJson();
    }

    private class BjvToJsonConverter : IBjvValueVisitor
    {
        private string _result = string.Empty;

        public string GetJson() => _result;

        public void VisitNull(BjvNullValue value)
        {
            _result = "null";
        }

        public void VisitBool(BjvBoolValue value)
        {
            _result = value.Value ? "true" : "false";
        }

        public void VisitInt64(BjvInt64Value value)
        {
            _result = value.Value.ToString();
        }

        public void VisitUInt64(BjvUInt64Value value)
        {
            _result = value.Value.ToString();
        }

        public void VisitFloat64(BjvFloat64Value value)
        {
            _result = value.Value.ToString("G17", CultureInfo.InvariantCulture);
        }

        public void VisitString(BjvStringValue value)
        {
            _result = JsonSerializer.Serialize(value.Value);
        }

        public void VisitBytes(BjvBytesValue value)
        {
            _result = JsonSerializer.Serialize(Convert.ToBase64String(value.Value));
        }

        public void VisitArray(BjvArrayValue value)
        {
            var items = new List<string>();
            foreach (var element in value.Elements)
            {
                var converter = new BjvToJsonConverter();
                element.Accept(converter);
                items.Add(converter.GetJson());
            }
            _result = "[" + string.Join(",", items) + "]";
        }

        public void VisitObject(BjvObjectValue value)
        {
            var items = new List<string>();
            var sortedKeys = new List<string>(value.Fields.Keys);
            sortedKeys.Sort();

            foreach (var key in sortedKeys)
            {
                var converter = new BjvToJsonConverter();
                value.Fields[key].Accept(converter);
                items.Add($"\"{key}\":{converter.GetJson()}");
            }
            _result = "{" + string.Join(",", items) + "}";
        }
    }
}
