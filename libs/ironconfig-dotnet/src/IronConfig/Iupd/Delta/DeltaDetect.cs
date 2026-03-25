using System;
using System.IO;
using System.Text;

namespace IronConfig.Iupd.Delta;

/// <summary>
/// Delta format enumeration.
/// </summary>
public enum DeltaFormat
{
    /// <summary>Unknown or invalid delta format.</summary>
    Unknown = 0,

    /// <summary>IUPDDEL1 - Delta v1 format.</summary>
    V1_IUPDDEL1 = 1,

    /// <summary>IRONDEL2 - Delta v2 format (Content-Defined Chunking).</summary>
    V2_IRONDEL2 = 2
}

/// <summary>
/// Delta format detection by magic bytes.
/// Fail-closed: unknown formats result in error, not guessing.
/// </summary>
public static class DeltaDetect
{
    private const int MAGIC_SIZE = 8;
    private const string MAGIC_V1 = "IUPDDEL1";
    private const string MAGIC_V2 = "IRONDEL2";

    /// <summary>
    /// Detect delta format by reading magic bytes from file.
    /// Fail-closed: returns Unknown if magic is unrecognized.
    /// </summary>
    public static DeltaFormat DetectFile(string deltaFilePath)
    {
        try
        {
            if (!File.Exists(deltaFilePath))
                return DeltaFormat.Unknown;

            byte[] header = new byte[MAGIC_SIZE];
            using (var fs = new FileStream(deltaFilePath, FileMode.Open, FileAccess.Read))
            {
                if (fs.Read(header, 0, MAGIC_SIZE) < MAGIC_SIZE)
                    return DeltaFormat.Unknown;
            }

            return DetectBytes(header);
        }
        catch
        {
            return DeltaFormat.Unknown;
        }
    }

    /// <summary>
    /// Detect delta format from bytes (e.g., first 8 bytes of delta buffer).
    /// Fail-closed: returns Unknown if magic is unrecognized.
    /// </summary>
    public static DeltaFormat DetectBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < MAGIC_SIZE)
            return DeltaFormat.Unknown;

        string magic = Encoding.ASCII.GetString(data.Slice(0, MAGIC_SIZE).ToArray());

        return magic switch
        {
            MAGIC_V1 => DeltaFormat.V1_IUPDDEL1,
            MAGIC_V2 => DeltaFormat.V2_IRONDEL2,
            _ => DeltaFormat.Unknown
        };
    }

    /// <summary>
    /// Get human-readable format name.
    /// </summary>
    public static string FormatName(DeltaFormat format) => format switch
    {
        DeltaFormat.V1_IUPDDEL1 => "IUPDDEL1 (v1)",
        DeltaFormat.V2_IRONDEL2 => "IRONDEL2 (v2)",
        _ => "Unknown"
    };
}
