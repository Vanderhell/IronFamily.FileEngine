using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronConfig.Iupd.Trust;

/// <summary>
/// Trust store v1: per-target key management with atomic writes.
/// File: .ironupd_trust.json (deterministic canonical JSON).
/// </summary>
public class IupdTrustStoreV1
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("keys")]
    public List<TrustKey> Keys { get; set; } = new();

    [JsonPropertyName("revoked")]
    public List<string> Revoked { get; set; } = new();

    public class TrustKey
    {
        [JsonPropertyName("key_id")]
        public string KeyId { get; set; } = "";

        [JsonPropertyName("pub")]
        public string Pub { get; set; } = "";

        [JsonPropertyName("comment")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Comment { get; set; }
    }

    /// <summary>
    /// Compute key ID from 32-byte public key (first16(BLAKE3(pub32))).
    /// </summary>
    public static string ComputeKeyId(byte[] pub32)
    {
        if (pub32?.Length != 32)
            throw new ArgumentException("Public key must be 32 bytes", nameof(pub32));

        byte[] hash = Blake3Ieee.Compute(pub32);
        byte[] keyIdBytes = new byte[16];
        Array.Copy(hash, 0, keyIdBytes, 0, 16);

        return Convert.ToHexString(keyIdBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Load trust store from file. Returns error if validation fails.
    /// </summary>
    public static (IupdTrustStoreV1? store, IronEdgeError? error) TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return (null, new IronEdgeError(
                    IronEdgeErrorCategory.Io,
                    0x70,
                    IronEdgeEngine.Iupd,
                    $"Trust store not found: {path}"));

            string json = File.ReadAllText(path);
            var store = JsonSerializer.Deserialize<IupdTrustStoreV1>(json);

            if (store == null)
                return (null, new IronEdgeError(
                    IronEdgeErrorCategory.CorruptData,
                    0x71,
                    IronEdgeEngine.Iupd,
                    "Failed to deserialize trust store"));

            // Validate
            if (store.Version != 1)
                return (null, new IronEdgeError(
                    IronEdgeErrorCategory.UnsupportedVersion,
                    0x72,
                    IronEdgeEngine.Iupd,
                    $"Unsupported trust store version: {store.Version}"));

            // Validate hex format and length
            foreach (var key in store.Keys ?? new())
            {
                if (!IsValidHex(key.KeyId, 32))
                    return (null, new IronEdgeError(
                        IronEdgeErrorCategory.CorruptData,
                        0x73,
                        IronEdgeEngine.Iupd,
                        "Invalid key_id hex format"));

                if (!IsValidHex(key.Pub, 64))
                    return (null, new IronEdgeError(
                        IronEdgeErrorCategory.CorruptData,
                        0x74,
                        IronEdgeEngine.Iupd,
                        "Invalid pub hex format"));
            }

            foreach (var revoked in store.Revoked ?? new())
            {
                if (!IsValidHex(revoked, 32))
                    return (null, new IronEdgeError(
                        IronEdgeErrorCategory.CorruptData,
                        0x75,
                        IronEdgeEngine.Iupd,
                        "Invalid revoked key_id hex format"));
            }

            // Check for duplicates
            var keyIds = store.Keys?.Select(k => k.KeyId).ToHashSet();
            if (keyIds?.Count != store.Keys?.Count)
                return (null, new IronEdgeError(
                    IronEdgeErrorCategory.CorruptData,
                    0x76,
                    IronEdgeEngine.Iupd,
                    "Duplicate key_id in trust store"));

            var revokedUnique = new HashSet<string>(store.Revoked ?? new());
            if (revokedUnique.Count != (store.Revoked?.Count ?? 0))
                return (null, new IronEdgeError(
                    IronEdgeErrorCategory.CorruptData,
                    0x77,
                    IronEdgeEngine.Iupd,
                    "Duplicate key_id in revoked list"));

            return (store, null);
        }
        catch (Exception ex)
        {
            return (null, new IronEdgeError(
                IronEdgeErrorCategory.Io,
                0x78,
                IronEdgeEngine.Iupd,
                $"Failed to load trust store: {ex.Message}",
                innerException: ex));
        }
    }

    /// <summary>
    /// Save trust store atomically with canonical JSON ordering.
    /// </summary>
    public IronEdgeError? SaveAtomic(string path)
    {
        try
        {
            // Canonicalize: sort keys and revoked
            Keys = Keys?.OrderBy(k => k.KeyId).ToList() ?? new();
            Revoked = Revoked?.OrderBy(r => r).ToList() ?? new();

            // Canonical JSON: deterministic serialization
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            // Custom ordering: version, keys, revoked (no PropertyOrder in .NET 8 by default)
            // We'll use JsonDocument for precise control
            var json = ToCanonicalJson();

            // Atomic write: temp file + rename
            string dir = Path.GetDirectoryName(path) ?? ".";
            string tempPath = Path.Combine(dir, ".tmp_" + Path.GetRandomFileName());

            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }

            return null;
        }
        catch (Exception ex)
        {
            return new IronEdgeError(
                IronEdgeErrorCategory.Io,
                0x79,
                IronEdgeEngine.Iupd,
                $"Failed to save trust store: {ex.Message}",
                innerException: ex);
        }
    }

    /// <summary>
    /// Generate canonical JSON with stable field order: version, keys, revoked.
    /// </summary>
    private string ToCanonicalJson()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{");

        // version
        sb.Append("\"version\":");
        sb.Append(Version);

        // keys (sorted)
        sb.Append(",\"keys\":[");
        var sortedKeys = (Keys ?? new()).OrderBy(k => k.KeyId).ToList();
        for (int i = 0; i < sortedKeys.Count; i++)
        {
            if (i > 0) sb.Append(",");
            var key = sortedKeys[i];
            sb.Append("{");
            sb.Append($"\"key_id\":\"{key.KeyId}\"");
            sb.Append($",\"pub\":\"{key.Pub}\"");
            if (!string.IsNullOrEmpty(key.Comment))
            {
                // Escape quotes in comment
                string escapedComment = key.Comment
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");
                sb.Append($",\"comment\":\"{escapedComment}\"");
            }
            sb.Append("}");
        }
        sb.Append("]");

        // revoked (sorted)
        sb.Append(",\"revoked\":[");
        var sortedRevoked = (Revoked ?? new()).OrderBy(r => r).ToList();
        for (int i = 0; i < sortedRevoked.Count; i++)
        {
            if (i > 0) sb.Append(",");
            sb.Append($"\"{sortedRevoked[i]}\"");
        }
        sb.Append("]");

        sb.Append("}");
        return sb.ToString();
    }

    private static bool IsValidHex(string? value, int expectedLen)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length != expectedLen) return false;
        return value.All(c => "0123456789abcdef".Contains(c));
    }

    /// <summary>
    /// Check if a key is trusted (exists in keys and not revoked).
    /// </summary>
    public bool IsTrusted(string keyId)
    {
        if (Keys?.Any(k => k.KeyId == keyId) != true)
            return false;
        if (Revoked?.Contains(keyId) == true)
            return false;
        return true;
    }

    /// <summary>
    /// Add a key to the trust store (idempotent).
    /// </summary>
    public void AddKey(string keyId, string pub, string? comment = null)
    {
        Keys ??= new();
        if (Keys.Any(k => k.KeyId == keyId))
            return; // Idempotent

        Keys.Add(new TrustKey { KeyId = keyId, Pub = pub, Comment = comment });
    }

    /// <summary>
    /// Revoke a key (idempotent).
    /// </summary>
    public void RevokeKey(string keyId)
    {
        Revoked ??= new();
        if (Revoked.Contains(keyId))
            return; // Idempotent

        Revoked.Add(keyId);
    }
}
