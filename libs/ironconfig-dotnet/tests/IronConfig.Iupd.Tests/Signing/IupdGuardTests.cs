using System;
using System.IO;
using System.Text;
using Xunit;
using IronConfig.Iupd.Signing;
using IronConfig.Iupd.Trust;

namespace IronConfig.Iupd.Tests.Signing;

/// <summary>
/// Guard tests: strict validation of format, prevent security bypasses.
/// </summary>
public class IupdGuardTests
{
    [Fact]
    public void SigFile_TrailingBytes_Rejected()
    {
        byte[] validSig = new byte[125];
        System.Text.Encoding.ASCII.GetBytes("IUPDSIG1").CopyTo(validSig, 0);
        validSig[8] = 0x01; // AlgId

        byte[] withTrailing = new byte[126];
        Array.Copy(validSig, withTrailing, 125);
        withTrailing[125] = 0xFF; // Trailing byte

        var (_, err) = IupdSigFile.TryRead(withTrailing);
        Assert.NotNull(err);
        // Size check happens first, so we get 0x41 (wrong size)
        Assert.Equal(0x41, err.Value.Code); // CorruptData (wrong size)
    }

    [Fact]
    public void SigFile_BadMagic_Rejected()
    {
        byte[] bad = new byte[125];
        System.Text.Encoding.ASCII.GetBytes("BADMAGIC").CopyTo(bad, 0);

        var (_, err) = IupdSigFile.TryRead(bad);
        Assert.NotNull(err);
        Assert.Equal(0x42, err.Value.Code); // BadMagic
    }

    [Fact]
    public void SigFile_WrongSize_Rejected()
    {
        var (_, err) = IupdSigFile.TryRead(new byte[124]);
        Assert.NotNull(err);
        Assert.Equal(0x41, err.Value.Code); // CorruptData (wrong size)

        var (_, err2) = IupdSigFile.TryRead(new byte[126]);
        Assert.NotNull(err2);
        Assert.Equal(0x41, err2.Value.Code);
    }

    [Fact]
    public void TrustStore_InvalidHex_KeyId_Rejected()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bad_trust_{Guid.NewGuid()}.json");
        try
        {
            // Invalid hex in key_id (Z is not hex)
            string json = @"{""version"":1,""keys"":[{""key_id"":""ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ"",""pub"":""d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a""}],""revoked"":[]}";
            File.WriteAllText(tempFile, json);

            var (_, err) = IupdTrustStoreV1.TryLoad(tempFile);
            Assert.NotNull(err);
            Assert.Equal(0x73, err.Value.Code); // Invalid key_id hex
        }
        finally { try { File.Delete(tempFile); } catch { } }
    }

    [Fact]
    public void TrustStore_InvalidHex_Pub_Rejected()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bad_trust_{Guid.NewGuid()}.json");
        try
        {
            // Invalid hex in pub (too short)
            string json = @"{""version"":1,""keys"":[{""key_id"":""6c31041268f471609c79f5f2dbcc38e4"",""pub"":""d75a980182b10ab7d54bfed3c""}],""revoked"":[]}";
            File.WriteAllText(tempFile, json);

            var (_, err) = IupdTrustStoreV1.TryLoad(tempFile);
            Assert.NotNull(err);
            Assert.Equal(0x74, err.Value.Code); // Invalid pub hex
        }
        finally { try { File.Delete(tempFile); } catch { } }
    }

    [Fact]
    public void TrustStore_DuplicateKeyId_Rejected()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bad_trust_{Guid.NewGuid()}.json");
        try
        {
            // Same key_id twice
            string json = @"{""version"":1,""keys"":[{""key_id"":""6c31041268f471609c79f5f2dbcc38e4"",""pub"":""d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a""},{""key_id"":""6c31041268f471609c79f5f2dbcc38e4"",""pub"":""d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a""}],""revoked"":[]}";
            File.WriteAllText(tempFile, json);

            var (_, err) = IupdTrustStoreV1.TryLoad(tempFile);
            Assert.NotNull(err);
            Assert.Equal(0x76, err.Value.Code); // Duplicate key_id
        }
        finally { try { File.Delete(tempFile); } catch { } }
    }

    [Fact]
    public void TrustStore_DuplicateRevoked_Rejected()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bad_trust_{Guid.NewGuid()}.json");
        try
        {
            // Same key_id in revoked twice
            string json = @"{""version"":1,""keys"":[],""revoked"":[""6c31041268f471609c79f5f2dbcc38e4"",""6c31041268f471609c79f5f2dbcc38e4""]}";
            File.WriteAllText(tempFile, json);

            var (_, err) = IupdTrustStoreV1.TryLoad(tempFile);
            Assert.NotNull(err);
            Assert.Equal(0x77, err.Value.Code); // Duplicate in revoked
        }
        finally { try { File.Delete(tempFile); } catch { } }
    }

    [Fact]
    public void TrustStore_UnsupportedVersion_Rejected()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bad_trust_{Guid.NewGuid()}.json");
        try
        {
            // Version 2 not supported yet
            string json = @"{""version"":2,""keys"":[],""revoked"":[]}";
            File.WriteAllText(tempFile, json);

            var (_, err) = IupdTrustStoreV1.TryLoad(tempFile);
            Assert.NotNull(err);
            Assert.Equal(0x72, err.Value.Code); // UnsupportedVersion
        }
        finally { try { File.Delete(tempFile); } catch { } }
    }

    [Fact]
    public void Verify_SigFile_BadMagic_FailsCorruptData()
    {
        byte[] pkg = System.Text.Encoding.ASCII.GetBytes("test");
        byte[] badSig = new byte[125];
        System.Text.Encoding.ASCII.GetBytes("BADMAGIC").CopyTo(badSig, 0);

        var err = IupdSigner.VerifyPackageBytes(pkg, new byte[32], badSig);
        Assert.NotNull(err);
        Assert.Equal(0x42, err.Value.Code); // BadMagic
    }

    [Fact]
    public void Verify_SigFile_TrailingBytes_FailsCorruptData()
    {
        byte[] pkg = System.Text.Encoding.ASCII.GetBytes("test");
        byte[] badSig = new byte[126]; // Wrong size

        var err = IupdSigner.VerifyPackageBytes(pkg, new byte[32], badSig);
        Assert.NotNull(err);
        Assert.Equal(0x41, err.Value.Code); // CorruptData (size)
    }

    [Fact]
    public void ComputeKeyId_LowercaseHex()
    {
        byte[] pub32 = new byte[32];
        string keyId = IupdTrustStoreV1.ComputeKeyId(pub32);

        // Must be lowercase
        Assert.Equal(keyId, keyId.ToLowerInvariant());
        // Must be hex only
        Assert.True(keyId.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void TrustStore_InvalidHex_Revoked_Rejected()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bad_trust_{Guid.NewGuid()}.json");
        try
        {
            // Invalid hex in revoked
            string json = @"{""version"":1,""keys"":[],""revoked"":[""ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ""]}";
            File.WriteAllText(tempFile, json);

            var (_, err) = IupdTrustStoreV1.TryLoad(tempFile);
            Assert.NotNull(err);
            Assert.Equal(0x75, err.Value.Code); // Invalid revoked hex
        }
        finally { try { File.Delete(tempFile); } catch { } }
    }
}
