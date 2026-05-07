using System;

/// <summary>
/// Legacy ICF2 Golden Vector Generator (removed).
/// This repo cleanup removed legacy ICF2 encoder APIs and the associated tool implementation.
/// </summary>
internal static class Icf2VectorGenerator
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
            Console.WriteLine("Icf2VectorGenerator - legacy generator removed");
            return 0;
        }

        Console.Error.WriteLine("This tool has been retired.");
        Console.Error.WriteLine("Legacy ICF2 generator code was removed as dead/legacy code during repo cleanup.");
        Console.Error.WriteLine("Restore from git history on a dedicated branch if you still need it.");
        Console.Error.WriteLine();
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Icf2VectorGenerator (retired)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/Icf2VectorGenerator.csproj help");
        Console.WriteLine("  dotnet run --project tools/Icf2VectorGenerator.csproj version");
    }
}
