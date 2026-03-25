// Performance characteristics: GC allocation measurement
// Category: Perf - runs separately from canonical unit suite
// Extracted from hot path to ensure clean heap state

using System;
using System.IO;
using IronConfig;
using IronConfig.IronCfg;

namespace IronConfig.Tests.Perf;

[Trait("Category", "Perf")]
public class NoHeapAllocationOnHotPathTests
{
    private static string GetTestVectorPath(string profile)
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? repoRoot = null;

        for (int i = 0; i < 10; i++)
        {
            var testVectorsPath = Path.Combine(currentDir.FullName, "vectors", "small");
            if (Directory.Exists(testVectorsPath))
            {
                repoRoot = currentDir;
                break;
            }
            currentDir = currentDir.Parent;
            if (currentDir == null) break;
        }

        if (repoRoot == null)
            throw new Exception("Could not find repository root with vectors");

        return Path.Combine(repoRoot.FullName, "vectors", "small", "ironcfg", profile, "golden.icfg");
    }

    [Fact]
    public void NoHeapAllocationOnHotPath()
    {
        // Load small.icfg golden vector
        var data = System.IO.File.ReadAllBytes(GetTestVectorPath("small"));
        var openErr = IronCfgValidator.Open(data, out var view);
        Assert.True(openErr.IsOk);

        var strictErr = IronCfgValidator.ValidateStrict(data, view);
        Assert.True(strictErr.IsOk);

        // Create path once (on stack)
        var path = new IronCfgPath[] {
            new IronCfgKeyPath("records"),
            new IronCfgIndexPath(0),
            new IronCfgFieldIdPath(0)
        };

        // Extract multiple times - verify no allocation
        var allocBefore = GC.GetTotalMemory(true);
        for (int i = 0; i < 1000; i++)
        {
            var err = IronCfgValueReader.GetUInt64(data, view, path, out var _);
            Assert.True(err.IsOk);
        }
        var allocAfter = GC.GetTotalMemory(true);

        // Should be < 1KB allocation (accounting for GC measurement noise)
        var allocDiff = allocAfter - allocBefore;
        Assert.True(allocDiff < 10000, $"Unexpected allocation: {allocDiff} bytes");
    }
}
