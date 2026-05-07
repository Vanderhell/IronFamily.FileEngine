using System;
using System.Diagnostics;
using System.IO;
using IronConfig.ILog;

class Program
{
    static void Main()
    {
        var repoRoot = FindRepoRoot();
        var datasets = new[] { "small", "medium", "large", "mega" };

        Console.WriteLine("Dataset\tFileSize\tOpen(ms)\tValidateFast(MB/s)\tValidateStrict(MB/s)\tEventCount");

        foreach (var dataset in datasets)
        {
            var vectorPath = Path.Combine(repoRoot, "vectors/small", "ilog", $"golden_{dataset}", "expected", "ilog.ilog");
            if (!File.Exists(vectorPath))
            {
                Console.WriteLine($"SKIP {dataset}");
                continue;
            }

            var fileBytes = File.ReadAllBytes(vectorPath);
            long fileSize = fileBytes.Length;
            uint eventCount = 0;

            for (int i = 0; i < 5; i++)
            {
                IlogReader.Open(fileBytes, out var view);
                if (view != null) eventCount = view.EventCount;
            }

            // Benchmark Open
            var sw = Stopwatch.StartNew();
            int openLoops = fileSize < 1000 ? 5000 : 500;
            for (int i = 0; i < openLoops; i++)
                IlogReader.Open(fileBytes, out _);
            sw.Stop();
            double openMs = sw.Elapsed.TotalMilliseconds / openLoops;

            // Benchmark ValidateFast
            IlogReader.Open(fileBytes, out var fastView);
            sw = Stopwatch.StartNew();
            int fastLoops = fileSize < 1000 ? 50000 : 5000;
            for (int i = 0; i < fastLoops; i++)
                IlogReader.ValidateFast(fastView);
            sw.Stop();
            double fastMBps = (fileSize * fastLoops) / (sw.Elapsed.TotalMilliseconds * 1024 * 1024);

            // Benchmark ValidateStrict
            IlogReader.Open(fileBytes, out var strictView);
            sw = Stopwatch.StartNew();
            int strictLoops = fileSize < 1000 ? 5000 : 500;
            for (int i = 0; i < strictLoops; i++)
                IlogReader.ValidateStrict(strictView);
            sw.Stop();
            double strictMBps = (fileSize * strictLoops) / (sw.Elapsed.TotalMilliseconds * 1024 * 1024);

            Console.WriteLine($"{dataset}\t{fileSize}\t{openMs:F3}\t{fastMBps:F0}\t{strictMBps:F0}\t{eventCount}");
        }
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        for (int i = 0; i < 25; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "vectors/small", "ilog")))
                return dir.FullName;
            dir = dir.Parent;
            if (dir == null) break;
        }
        throw new Exception("Could not find repo root");
    }
}
