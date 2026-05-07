using System;

namespace IronConfigTool;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        if (command is "version" or "--version")
        {
            Console.WriteLine("ironcfg (ironconfigtool) - legacy commands removed");
            return 0;
        }

        Console.Error.WriteLine($"Command '{command}' is no longer supported in this repo.");
        Console.Error.WriteLine("Legacy format tooling for BJV/ICF2/ICFX/ICXS (and the legacy IronConfig.Crypto project) was removed as dead code.");
        Console.Error.WriteLine("If you still need these commands for historical reasons, restore them from git history in a dedicated branch.");
        Console.Error.WriteLine();
        PrintUsage();

        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("ironcfg (ironconfigtool)");
        Console.WriteLine();
        Console.WriteLine("This tool previously hosted legacy CLI commands for older/experimental formats.");
        Console.WriteLine("Those commands were intentionally removed during cleanup.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ironcfg help");
        Console.WriteLine("  ironcfg version");
    }
}
