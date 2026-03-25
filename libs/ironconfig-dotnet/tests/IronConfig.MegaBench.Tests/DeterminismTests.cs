using System;
using System.IO;
using System.Security.Cryptography;
using IronFamily.MegaBench.Datasets.ILog;
using IronFamily.MegaBench.Datasets.IronCfg;
using IronFamily.MegaBench.Datasets.IUpd;
using IronConfig.Iupd;
using IronConfig.ILog;
using Xunit;

namespace IronConfig.MegaBench.Tests;

public class DeterminismTests
{
    [Fact]
    public void IronCfg_10KB_IsDeterministic()
    {
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        try
        {
            // Generate twice
            var binA = IronCfgDatasetGenerator.GenerateDataset("10KB", useCrc32: true);
            var binB = IronCfgDatasetGenerator.GenerateDataset("10KB", useCrc32: true);

            var hashA = SHA256.HashData(binA);
            var hashB = SHA256.HashData(binB);

            // Assert equality
            Assert.Equal(hashA, hashB);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", null);
        }
    }

    [Fact]
    public void ILog_Minimal_10KB_IsDeterministic()
    {
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        try
        {
            // Generate twice with MINIMAL profile
            var binA = ILogDatasetGenerator.GenerateDataset("10KB", IlogProfile.MINIMAL);
            var binB = ILogDatasetGenerator.GenerateDataset("10KB", IlogProfile.MINIMAL);

            var hashA = SHA256.HashData(binA);
            var hashB = SHA256.HashData(binB);

            // Assert equality
            Assert.Equal(hashA, hashB);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", null);
        }
    }

    [Fact]
    public void IUpd_Minimal_10KB_IsDeterministic()
    {
        Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", "1");
        try
        {
            // Generate twice with MINIMAL profile
            var binA = IUpdDatasetGenerator.GenerateDataset("10KB", IupdProfile.MINIMAL);
            var binB = IUpdDatasetGenerator.GenerateDataset("10KB", IupdProfile.MINIMAL);

            var hashA = SHA256.HashData(binA);
            var hashB = SHA256.HashData(binB);

            // Assert equality
            Assert.Equal(hashA, hashB);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONFAMILY_DETERMINISTIC", null);
        }
    }
}
