using System;
using System.IO;
using System.Text;
using Xunit;
using IronConfig.Iupd.Trust;
using IronConfig.Crypto;

namespace IronConfig.Iupd.Tests.Signing;

public class IupdTrustStoreTests : IDisposable
{
    private readonly string _tempDir;

    public IupdTrustStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trust_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Trust_Init_CreatesCanonicalJson()
    {
        string path = Path.Combine(_tempDir, "trust.json");
        var store = new IupdTrustStoreV1();
        var err = store.SaveAtomic(path);

        Assert.Null(err);
        Assert.True(File.Exists(path));

        string json = File.ReadAllText(path);
        // Canonical JSON: {"version":1,"keys":[],"revoked":[]}
        Assert.Contains("\"version\":", json);
        Assert.Contains("\"keys\":", json);
        Assert.Contains("\"revoked\":", json);
    }

    [Fact]
    public void Trust_Add_Idempotent_SameFileBytes()
    {
        string path = Path.Combine(_tempDir, "trust.json");
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        var store1 = new IupdTrustStoreV1();
        string keyId = IupdTrustStoreV1.ComputeKeyId(pub32);
        string pubHex = Convert.ToHexString(pub32).ToLowerInvariant();
        store1.AddKey(keyId, pubHex, "test");
        var err1 = store1.SaveAtomic(path);
        Assert.Null(err1);

        byte[] bytes1 = File.ReadAllBytes(path);

        // Add again (idempotent)
        store1.AddKey(keyId, pubHex, "test");
        var err2 = store1.SaveAtomic(path);
        Assert.Null(err2);

        byte[] bytes2 = File.ReadAllBytes(path);
        Assert.Equal(bytes1, bytes2); // Identical bytes
    }

    [Fact]
    public void Trust_Revoke_Idempotent_Sorted()
    {
        string path = Path.Combine(_tempDir, "trust.json");
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        var store = new IupdTrustStoreV1();
        string keyId = IupdTrustStoreV1.ComputeKeyId(pub32);
        string pubHex = Convert.ToHexString(pub32).ToLowerInvariant();
        store.AddKey(keyId, pubHex);

        store.RevokeKey(keyId);
        var err1 = store.SaveAtomic(path);
        Assert.Null(err1);

        byte[] bytes1 = File.ReadAllBytes(path);

        // Revoke again (idempotent)
        store.RevokeKey(keyId);
        var err2 = store.SaveAtomic(path);
        Assert.Null(err2);

        byte[] bytes2 = File.ReadAllBytes(path);
        Assert.Equal(bytes1, bytes2); // Identical after idempotent revoke
    }

    [Fact]
    public void Trust_LoadSave_Deterministic_ByteIdentical()
    {
        string path = Path.Combine(_tempDir, "trust.json");
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        // Create initial store
        var store1 = new IupdTrustStoreV1();
        string keyId = IupdTrustStoreV1.ComputeKeyId(pub32);
        string pubHex = Convert.ToHexString(pub32).ToLowerInvariant();
        store1.AddKey(keyId, pubHex, "test");
        store1.RevokeKey(keyId);
        var err1 = store1.SaveAtomic(path);
        Assert.Null(err1);

        byte[] bytes1 = File.ReadAllBytes(path);

        // Load and re-save
        var (store2, loadErr) = IupdTrustStoreV1.TryLoad(path);
        Assert.Null(loadErr);
        Assert.NotNull(store2);

        var err2 = store2.SaveAtomic(path);
        Assert.Null(err2);

        byte[] bytes2 = File.ReadAllBytes(path);
        Assert.Equal(bytes1, bytes2); // Load-save deterministic
    }

    [Fact]
    public void Trust_IsTrusted_ChecksKeyPresenceAndRevocation()
    {
        byte[] seed32 = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] pub32 = new byte[32];
        Ed25519.CreatePublicKey(seed32, pub32);

        var store = new IupdTrustStoreV1();
        string keyId = IupdTrustStoreV1.ComputeKeyId(pub32);
        string pubHex = Convert.ToHexString(pub32).ToLowerInvariant();

        // Not present: not trusted
        Assert.False(store.IsTrusted(keyId));

        // Add key: trusted
        store.AddKey(keyId, pubHex);
        Assert.True(store.IsTrusted(keyId));

        // Revoke key: not trusted
        store.RevokeKey(keyId);
        Assert.False(store.IsTrusted(keyId));
    }

    [Fact]
    public void Trust_InvalidHex_Rejected()
    {
        string path = Path.Combine(_tempDir, "trust.json");
        string json = @"{""version"":1,""keys"":[{""key_id"":""ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ"",""pub"":""d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a""}],""revoked"":[]}";
        File.WriteAllText(path, json);

        var (store, err) = IupdTrustStoreV1.TryLoad(path);
        Assert.NotNull(err);
        Assert.Equal(0x73, err.Value.Code); // Invalid key_id hex
    }

    [Fact]
    public void Trust_DuplicateKeyId_Rejected()
    {
        string path = Path.Combine(_tempDir, "trust.json");
        string json = @"{""version"":1,""keys"":[{""key_id"":""6c31041268f471609c79f5f2dbcc38e4"",""pub"":""d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a""},{""key_id"":""6c31041268f471609c79f5f2dbcc38e4"",""pub"":""d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a""}],""revoked"":[]}";
        File.WriteAllText(path, json);

        var (store, err) = IupdTrustStoreV1.TryLoad(path);
        Assert.NotNull(err);
        Assert.Equal(0x76, err.Value.Code); // Duplicate key_id
    }

    [Fact]
    public void Trust_ComputeKeyId_Deterministic()
    {
        byte[] pub32 = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");

        string keyId1 = IupdTrustStoreV1.ComputeKeyId(pub32);
        string keyId2 = IupdTrustStoreV1.ComputeKeyId(pub32);

        Assert.Equal(keyId1, keyId2);
        Assert.Equal(32, keyId1.Length); // 16 bytes = 32 hex chars
    }
}
