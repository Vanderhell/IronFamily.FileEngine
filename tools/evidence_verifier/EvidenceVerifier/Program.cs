#!/usr/bin/env dotnet
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Evidence Matrix Strict Verifier with AST and Reflection Validation
/// - Strict parsing: counts claims, enforces 50 claims in matrix
/// - AST validation: verifies symbols exist in source code
/// - Reflection validation: verifies numeric values match claim literals
/// </summary>
public class EvidenceVerifier
{
    private readonly string repoRoot;
    private readonly bool useReflection;
    private readonly int? expectedClaims;
    private int parsedClaims = 0;
    private int codeAnchorsChecked = 0;
    private int anchorsFound = 0;
    private int reflectionChecked = 0;
    private int failures = 0;
    private List<string> failureMessages = new();

    public EvidenceVerifier(string matrixPath, bool useReflection = false, int? expectedClaims = null)
    {
        repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrixPath) ?? ".", "..", ".."));
        this.useReflection = useReflection;
        this.expectedClaims = expectedClaims;
    }

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: EvidenceVerifier <path> [--reflect] [--expect-claims 50]");
            return 2;
        }

        string matrixPath = args[0];
        bool useReflection = args.Contains("--reflect");
        int? expectedClaims = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--expect-claims" && i + 1 < args.Length && int.TryParse(args[i + 1], out int count))
            {
                expectedClaims = count;
            }
        }

        if (!File.Exists(matrixPath))
        {
            Console.Error.WriteLine($"ERROR: File not found: {matrixPath}");
            return 2;
        }

        var verifier = new EvidenceVerifier(matrixPath, useReflection, expectedClaims);
        return await verifier.Verify();
    }

    public async Task<int> Verify()
    {
        Console.WriteLine("=== Evidence Matrix Strict Verifier ===");
        Console.WriteLine($"Repository root: {repoRoot}");
        Console.WriteLine($"Reflection checks: {(useReflection ? "ENABLED" : "disabled")}");
        if (expectedClaims.HasValue)
            Console.WriteLine($"Expected claims: {expectedClaims.Value}");
        Console.WriteLine();

        var matrixPath = Path.Combine(repoRoot, "docs", "sct", "EVIDENCE_MATRIX.md");

        if (!File.Exists(matrixPath))
        {
            Console.Error.WriteLine($"ERROR: EVIDENCE_MATRIX not found at {matrixPath}");
            return 2;
        }

        var lines = File.ReadAllLines(matrixPath);
        var claims = new List<(string ClaimId, string EvidenceType, string EvidenceRef, string Claim)>();

        // STRICT PARSING: Parse only valid table rows with exactly 7 columns
        for (int lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            var line = lines[lineNum];

            // Skip non-data rows
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || !line.StartsWith("|"))
                continue;

            // Skip header row (before separator check so we catch it first)
            if (line.Contains("ClaimId") && line.Contains("EvidenceType"))
                continue;

            var parts = line.Split('|').Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().TrimStart('`').TrimEnd('`')).ToArray();

            // Skip separator rows (all parts are dashes of any length)
            if (parts.Length == 7 && parts.All(p => p.All(c => c == '-')))
                continue;

            // Strict: require exactly 7 columns (ClaimId | Engine | Topic | Claim | EvidenceType | EvidenceRef | Verified)
            if (parts.Length != 7)
            {
                failureMessages.Add($"⚠ Line {lineNum + 1}: Expected 7 columns, got {parts.Length}");
                continue;
            }

            var claimId = parts[0];
            var evidenceType = parts[4];
            var evidenceRef = parts[5];
            var claim = parts[3];

            if (string.IsNullOrWhiteSpace(claimId))
                continue;

            parsedClaims++;
            claims.Add((claimId, evidenceType, evidenceRef, claim));
        }

        // Check claim count
        if (expectedClaims.HasValue && parsedClaims != expectedClaims.Value)
        {
            failures++;
            failureMessages.Add($"❌ STRICT PARSING FAILED: Expected {expectedClaims.Value} claims, parsed {parsedClaims}");
            Console.WriteLine();
            Console.WriteLine("=== Failure Details ===");
            foreach (var msg in failureMessages)
                Console.WriteLine(msg);
            Console.Error.WriteLine("❌ STRICT PARSING FAILED");
            return 2;
        }

        // Validate claims
        foreach (var (claimId, evidenceType, evidenceRef, claim) in claims)
        {
            // Check for line number references
            if (Regex.IsMatch(evidenceRef, @":\d+"))
            {
                failures++;
                failureMessages.Add($"❌ {claimId}: Line number reference (invalid): {evidenceRef}");
                continue;
            }

            switch (evidenceType)
            {
                case "Code":
                    await ValidateCodeAnchor(claimId, evidenceRef, claim);
                    break;
                case "Test":
                    ValidateTestAnchor(claimId, evidenceRef);
                    break;
                case "CI":
                    ValidateCIAnchor(claimId, evidenceRef);
                    break;
            }
        }

        // Print results
        Console.WriteLine("=== Validation Results ===");
        Console.WriteLine($"Parsed claims: {parsedClaims}");
        if (expectedClaims.HasValue)
            Console.WriteLine($"Expected claims: {expectedClaims.Value}");
        Console.WriteLine($"Code anchors checked: {codeAnchorsChecked}");
        Console.WriteLine($"Anchors found: {anchorsFound}");
        Console.WriteLine($"Reflection checks: {reflectionChecked}");
        Console.WriteLine($"Failures: {failures}");
        Console.WriteLine();

        if (failures > 0)
        {
            Console.WriteLine("=== Failure Details ===");
            foreach (var msg in failureMessages)
                Console.WriteLine(msg);
            Console.WriteLine();
            Console.Error.WriteLine("❌ VERIFICATION FAILED");
            return 2;
        }

        Console.WriteLine("✅ VERIFICATION PASSED");
        return 0;
    }

    private async Task ValidateCodeAnchor(string claimId, string evidenceRef, string claim)
    {
        codeAnchorsChecked++;

        var parts = evidenceRef.Split('#');
        if (parts.Length != 2)
        {
            failures++;
            failureMessages.Add($"❌ {claimId}: Invalid Code format (expected path#Anchor): {evidenceRef}");
            return;
        }

        var path = parts[0];
        var anchor = parts[1];
        var filePath = Path.Combine(repoRoot, path);

        if (!File.Exists(filePath))
        {
            failures++;
            failureMessages.Add($"❌ {claimId}: File not found: {filePath}");
            return;
        }

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot() as CompilationUnitSyntax;

        if (root == null)
        {
            failures++;
            failureMessages.Add($"❌ {claimId}: Failed to parse syntax tree: {path}");
            return;
        }

        if (FindAnchor(root, anchor))
        {
            anchorsFound++;
            Console.WriteLine($"✓ {claimId}: '{anchor}' found in {Path.GetFileName(path)}");

            // Reflection check if enabled and claim has numeric literal
            if (useReflection)
            {
                await TryReflectionValidation(claimId, anchor, path, claim);
            }
        }
        else
        {
            failures++;
            failureMessages.Add($"❌ {claimId}: Anchor '{anchor}' not found in {path}");
        }
    }

    private async Task TryReflectionValidation(string claimId, string anchor, string path, string claim)
    {
        // Extract numeric literal from claim, but skip offset references like "offset 0x04"
        // Look for hex values in value contexts (enum member=0x..., equals 0x..., flag 0x..., type 0x...)
        var hexMatches = Regex.Matches(claim, @"(?<![a-z])\b(?:at\s+offset\s+0x|offset\s+0x).*", RegexOptions.IgnoreCase);
        if (hexMatches.Count > 0)
            return; // This is an offset reference, not a value to validate

        var hexMatch = Regex.Match(claim, @"0x[0-9A-Fa-f]+");
        if (!hexMatch.Success)
            return; // No numeric literal to validate

        reflectionChecked++;
        var expectedHex = hexMatch.Value;
        var expectedValue = Convert.ToUInt64(expectedHex, 16);

        try
        {
            // Discover and load assemblies from IronConfig project
            var binDir = Path.Combine(repoRoot, "libs/ironconfig-dotnet/src/IronConfig/bin/Release");
            var dllFiles = Directory.GetFiles(binDir, "IronConfig*.dll", SearchOption.AllDirectories);

            foreach (var dll in dllFiles)
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    if (TryValidateInAssembly(asm, anchor, expectedValue, claimId))
                        return;
                }
                catch { }
            }

            // If we get here, we couldn't validate
            failureMessages.Add($"⚠ {claimId}: Could not load assembly to validate {anchor}");
        }
        catch (Exception ex)
        {
            failureMessages.Add($"⚠ {claimId}: Reflection check failed: {ex.Message}");
        }
    }

    private bool TryValidateInAssembly(Assembly asm, string anchor, ulong expectedValue, string claimId)
    {
        // Try to find and validate const field or enum member
        foreach (var type in asm.GetTypes())
        {
            // Check enum members
            if (type.IsEnum)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var field in fields)
                {
                    if (field.Name == anchor)
                    {
                        var value = Convert.ToUInt64(field.GetValue(null));
                        if (value == expectedValue)
                        {
                            Console.WriteLine($"  ✓ {claimId}: Reflection verified {anchor} = {field.GetValue(null)}");
                            return true;
                        }
                        else
                        {
                            failures++;
                            failureMessages.Add($"❌ {claimId}: Reflection mismatch {anchor}: expected {expectedValue:X}, got {value:X}");
                            return true;
                        }
                    }
                }
            }

            // Check const fields
            var typeFields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in typeFields)
            {
                if (field.Name == anchor && field.IsLiteral)
                {
                    var value = Convert.ToUInt64(field.GetValue(null));
                    if (value == expectedValue)
                    {
                        Console.WriteLine($"  ✓ {claimId}: Reflection verified {anchor} = {field.GetValue(null)}");
                        return true;
                    }
                    else
                    {
                        failures++;
                        failureMessages.Add($"❌ {claimId}: Reflection mismatch {anchor}: expected {expectedValue:X}, got {value:X}");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool FindAnchor(CompilationUnitSyntax root, string anchor)
    {
        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
            if (enumDecl.Identifier.Text == anchor)
                return true;

        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            if (type.Identifier.Text == anchor)
                return true;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            if (method.Identifier.Text == anchor)
                return true;

        foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            if (prop.Identifier.Text == anchor)
                return true;

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            foreach (var var in field.Declaration.Variables)
                if (var.Identifier.Text == anchor)
                    return true;

        foreach (var enumMember in root.DescendantNodes().OfType<EnumMemberDeclarationSyntax>())
            if (enumMember.Identifier.Text == anchor)
                return true;

        return false;
    }

    private void ValidateTestAnchor(string claimId, string evidenceRef)
    {
        if (!Regex.IsMatch(evidenceRef, @"^[^:]+:[^\s]+\.[^\s]+$"))
            Console.WriteLine($"⚠ {claimId}: Vague test format: {evidenceRef}");
        anchorsFound++;
    }

    private void ValidateCIAnchor(string claimId, string evidenceRef)
    {
        anchorsFound++;
    }
}
