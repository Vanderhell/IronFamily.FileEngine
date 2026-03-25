using System;
using System.IO;
using System.Text;
using Xunit;

namespace IronConfig.Iupd.Tests;

public class IupdApplyEngineTests : IDisposable
{
    private readonly string _testDir;

    public IupdApplyEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"iupd_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void StageUpdate_ValidPayload_CreatesChunks()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("test payload data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Act
        var error = engine.StageUpdate();

        // Assert
        Assert.True(error.IsOk);
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");
        Assert.True(Directory.Exists(stagingPath));
        var chunkPath = Path.Combine(stagingPath, "chunk_00000000");
        Assert.True(File.Exists(chunkPath));
    }

    [Fact]
    public void CreateCommitMarker_AfterStage_WritesDeterministicJson()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("test data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Act
        engine.StageUpdate();
        var error = engine.CreateCommitMarker(1);

        // Assert
        Assert.True(error.IsOk);
        var markerPath = Path.Combine(_testDir, ".ironupd_commit.json");
        Assert.True(File.Exists(markerPath));
        var json = File.ReadAllText(markerPath);

        // Verify deterministic field order
        Assert.Contains("\"manifest_hash\":", json);
        Assert.Contains("\"file_count\":", json);
        Assert.Contains("\"state\":", json);

        var hashIdx = json.IndexOf("\"manifest_hash\":");
        var countIdx = json.IndexOf("\"file_count\":");
        var stateIdx = json.IndexOf("\"state\":");
        Assert.True(hashIdx < countIdx && countIdx < stateIdx, "Fields must be in stable order");
    }

    [Fact]
    public void FinalizeSwap_AfterMarker_AtomicallySwapsDirectories()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("firmware binary");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Act - full 3-phase cycle
        engine.StageUpdate();
        engine.CreateCommitMarker(1);
        var error = engine.FinalizeSwap();

        // Assert
        Assert.True(error.IsOk);
        var currentPath = Path.Combine(_testDir, "current");
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");

        Assert.True(Directory.Exists(currentPath), "current directory should exist");
        Assert.False(Directory.Exists(stagingPath), "staging directory should be gone");

        var chunkPath = Path.Combine(currentPath, "chunk_00000000");
        Assert.True(File.Exists(chunkPath), "chunk should be in current");
    }

    [Fact]
    public void Recovery_CrashBeforeMarker_RollsBackStaging()
    {
        // Arrange - simulate crash during Phase 1
        var payload = Encoding.UTF8.GetBytes("test data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Simulate: StageUpdate succeeded, but CreateCommitMarker crashed
        engine.StageUpdate();
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");
        Assert.True(Directory.Exists(stagingPath), "Staging should exist before crash simulation");

        // Act - recovery on "restart"
        var recovery = new IupdApplyRecovery(_testDir);
        var error = recovery.Recover();

        // Assert - incomplete staging should be rolled back
        Assert.True(error.IsOk);
        Assert.False(Directory.Exists(stagingPath), "Staging should be deleted on recovery");
    }

    [Fact]
    public void Recovery_CrashAfterMarkerBeforeSwap_CompletesSwap()
    {
        // Arrange - simulate crash after commit marker but before swap
        var payload = Encoding.UTF8.GetBytes("firmware data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Simulate: full 3-phase started but swap crashed
        engine.StageUpdate();
        engine.CreateCommitMarker(1);
        var markerPath = Path.Combine(_testDir, ".ironupd_commit.json");
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");

        Assert.True(File.Exists(markerPath), "Marker should exist");
        Assert.True(Directory.Exists(stagingPath), "Staging should exist");

        // Act - recovery completes the swap
        var recovery = new IupdApplyRecovery(_testDir);
        var error = recovery.Recover();

        // Assert
        Assert.True(error.IsOk);
        var currentPath = Path.Combine(_testDir, "current");
        Assert.True(Directory.Exists(currentPath), "current should exist after recovery");
        Assert.False(Directory.Exists(stagingPath), "staging should be gone after recovery");
        Assert.False(File.Exists(markerPath), "marker should be deleted after recovery");
    }

    [Fact]
    public void Recovery_CrashDuringSwap_HandlesPartialState()
    {
        // Arrange - simulate crash during atomic swap
        var payload = Encoding.UTF8.GetBytes("new firmware");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Create old current directory with different content
        var currentPath = Path.Combine(_testDir, "current");
        Directory.CreateDirectory(currentPath);
        File.WriteAllText(Path.Combine(currentPath, "old_chunk"), "old content");

        // Simulate: full 3-phase but crash during swap
        engine.StageUpdate();
        engine.CreateCommitMarker(1);
        // (don't call FinalizeSwap - simulate crash before it completes)

        var markerPath = Path.Combine(_testDir, ".ironupd_commit.json");
        Assert.True(File.Exists(markerPath));

        // Act - recovery handles the partial state
        var recovery = new IupdApplyRecovery(_testDir);
        var error = recovery.Recover();

        // Assert - new content should be in current
        Assert.True(error.IsOk);
        Assert.True(Directory.Exists(currentPath));
        var newChunk = Path.Combine(currentPath, "chunk_00000000");
        Assert.True(File.Exists(newChunk), "New chunk should be in current after recovery");

        // Old file should be backed up or gone
        Assert.False(File.Exists(Path.Combine(currentPath, "old_chunk")),
            "Old content should not remain in current after recovery");
    }

    [Fact]
    public void Recovery_Idempotent_MultipleRecoveryCalls()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        engine.StageUpdate();
        engine.CreateCommitMarker(1);

        // Act - call recovery multiple times (simulating repeated restarts)
        var recovery = new IupdApplyRecovery(_testDir);
        var error1 = recovery.Recover();
        var error2 = recovery.Recover();
        var error3 = recovery.Recover();

        // Assert - all should succeed and final state should be stable
        Assert.True(error1.IsOk);
        Assert.True(error2.IsOk);
        Assert.True(error3.IsOk);

        var currentPath = Path.Combine(_testDir, "current");
        Assert.True(Directory.Exists(currentPath));

        // Verify state is unchanged after second recovery
        var chunkPath = Path.Combine(currentPath, "chunk_00000000");
        var content1 = File.ReadAllBytes(chunkPath);
        var error4 = recovery.Recover();
        var content2 = File.ReadAllBytes(chunkPath);
        Assert.True(error4.IsOk);
        Assert.Equal(content1, content2);
    }

    [Fact]
    public void Recovery_NormalState_ReturnsOk()
    {
        // Arrange - clean directory, no crash state
        var recovery = new IupdApplyRecovery(_testDir);

        // Act
        var error = recovery.Recover();

        // Assert
        Assert.True(error.IsOk);
        Assert.False(Directory.Exists(Path.Combine(_testDir, ".ironupd_stage")));
        Assert.False(Directory.Exists(Path.Combine(_testDir, ".ironupd_backup")));
        Assert.False(File.Exists(Path.Combine(_testDir, ".ironupd_commit.json")));
    }

    [Fact]
    public void FullCycle_StageCommitSwap_DeterministicOutput()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("deterministic test data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .AddChunk(1, payload)
            .WithApplyOrder(0, 1)
            .Build();
        var manifestHash = new byte[32];

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Act - full cycle
        var err1 = engine.StageUpdate();
        var err2 = engine.CreateCommitMarker(2);
        var err3 = engine.FinalizeSwap();

        // Assert
        Assert.True(err1.IsOk);
        Assert.True(err2.IsOk);
        Assert.True(err3.IsOk);

        var currentPath = Path.Combine(_testDir, "current");
        Assert.True(Directory.Exists(currentPath));
        Assert.True(File.Exists(Path.Combine(currentPath, "chunk_00000000")));
        Assert.True(File.Exists(Path.Combine(currentPath, "chunk_00000001")));
    }

    [Fact]
    public void StageUpdate_AfterValidation_CreatesValidStructure()
    {
        // Arrange - verify that a successful stage/commit/finalize sequence produces valid structure
        var payload = Encoding.UTF8.GetBytes("firmware data");
        var data = new IupdBuilder()
            .AddChunk(0, payload)
            .WithApplyOrder(0)
            .Build();

        var reader = IupdReader.Open(data, out var err);
        Assert.NotNull(reader);
        var manifestHash = new byte[32];
        var engine = new IupdApplyEngine(reader, manifestHash, _testDir);

        // Act
        var error = engine.StageUpdate();

        // Assert
        Assert.True(error.IsOk, "Staging should succeed with valid payload");
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");
        Assert.True(Directory.Exists(stagingPath), "Staging directory should exist");
        Assert.True(File.Exists(Path.Combine(stagingPath, "chunk_00000000")), "Chunk should be staged");
    }
}

public class IupdApplyRecoveryTests : IDisposable
{
    private readonly string _testDir;

    public IupdApplyRecoveryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"iupd_recovery_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void Recover_StagingWithoutMarker_DeletesStaging()
    {
        // Arrange - incomplete staging from Phase 1 crash
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");
        Directory.CreateDirectory(stagingPath);
        File.WriteAllText(Path.Combine(stagingPath, "chunk_00000000"), "partial data");

        var recovery = new IupdApplyRecovery(_testDir);

        // Act
        var error = recovery.Recover();

        // Assert
        Assert.True(error.IsOk);
        Assert.False(Directory.Exists(stagingPath));
    }

    [Fact]
    public void Recover_MarkerWithStaging_CompletesSwap()
    {
        // Arrange - crash after marker but before swap
        var stagingPath = Path.Combine(_testDir, ".ironupd_stage");
        var markerPath = Path.Combine(_testDir, ".ironupd_commit.json");

        Directory.CreateDirectory(stagingPath);
        File.WriteAllText(Path.Combine(stagingPath, "chunk_00000000"), "new firmware");
        File.WriteAllText(markerPath, "{ \"manifest_hash\": \"abc\", \"file_count\": 1, \"state\": \"ready\" }");

        var recovery = new IupdApplyRecovery(_testDir);

        // Act
        var error = recovery.Recover();

        // Assert
        Assert.True(error.IsOk);
        var currentPath = Path.Combine(_testDir, "current");
        Assert.True(Directory.Exists(currentPath));
        Assert.False(Directory.Exists(stagingPath));
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Recover_MarkerWithoutStaging_CleansupMarker()
    {
        // Arrange - swap already completed, just marker remains
        var markerPath = Path.Combine(_testDir, ".ironupd_commit.json");
        File.WriteAllText(markerPath, "{ \"state\": \"ready\" }");

        var recovery = new IupdApplyRecovery(_testDir);

        // Act
        var error = recovery.Recover();

        // Assert
        Assert.True(error.IsOk);
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void Recover_BackupWithMarker_CleansupBoth()
    {
        // Arrange - swap completed, backup and marker remain
        var markerPath = Path.Combine(_testDir, ".ironupd_commit.json");
        var backupPath = Path.Combine(_testDir, ".ironupd_backup");
        var currentPath = Path.Combine(_testDir, "current");

        Directory.CreateDirectory(currentPath);
        File.WriteAllText(Path.Combine(currentPath, "new_chunk"), "new");

        Directory.CreateDirectory(backupPath);
        File.WriteAllText(Path.Combine(backupPath, "old_chunk"), "old");

        File.WriteAllText(markerPath, "{ \"state\": \"ready\" }");

        var recovery = new IupdApplyRecovery(_testDir);

        // Act
        var error = recovery.Recover();

        // Assert
        Assert.True(error.IsOk);
        Assert.False(File.Exists(markerPath));
        Assert.False(Directory.Exists(backupPath), "Backup should be cleaned up");
        Assert.True(Directory.Exists(currentPath), "Current should remain");
        Assert.True(File.Exists(Path.Combine(currentPath, "new_chunk")));
    }
}
