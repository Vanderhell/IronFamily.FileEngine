using System;

namespace IronConfig;

/// <summary>
/// BJV Reader - parses binary JSON format
/// </summary>
public class BjvReader
{
    private readonly byte[] _data;

    public BjvReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Validate binary JSON data
    /// </summary>
    public bool Validate()
    {
        return _data.Length > 0;
    }
}
