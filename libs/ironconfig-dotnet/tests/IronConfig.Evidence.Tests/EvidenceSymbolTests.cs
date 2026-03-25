using System;
using System.Reflection;
using Xunit;
using IronConfig;
using IronConfig.Iupd;
using IronConfig.IronCfg;
using IronConfig.ILog;

namespace IronConfig.Evidence.Tests;

/// <summary>
/// Symbol existence tests for EVIDENCE_MATRIX anchors.
/// These tests verify that every symbol referenced in the matrix actually exists.
/// </summary>
public class EvidenceSymbolTests
{
    [Fact(DisplayName = "iupd-prof-001: IupdProfile enum exists")]
    public void IupdProf001_IupdProfileEnumExists()
    {
        var type = typeof(IupdProfile);
        Assert.NotNull(type);
        Assert.True(type.IsEnum);
    }

    [Fact(DisplayName = "iupd-prof-007: RequiresBlake3 extension method exists")]
    public void IupdProf007_RequiresBlake3ExtensionExists()
    {
        // RequiresBlake3 is an extension method - verify it works at runtime
        Assert.True(IupdProfile.SECURE.RequiresBlake3());
    }

    [Fact(DisplayName = "iupd-prof-008: SupportsCompression extension method exists")]
    public void IupdProf008_SupportsCompressionExtensionExists()
    {
        // SupportsCompression is an extension method - verify it works at runtime
        Assert.True(IupdProfile.FAST.SupportsCompression());
    }

    [Fact(DisplayName = "iupd-prof-009: SupportsDependencies extension method exists")]
    public void IupdProf009_SupportsDependenciesExtensionExists()
    {
        // SupportsDependencies is an extension method - verify it works at runtime
        Assert.True(IupdProfile.SECURE.SupportsDependencies());
    }

    [Fact(DisplayName = "cfg-struct-002: HEADER_SIZE constant exists")]
    public void CfgStruct002_HeaderSizeConstantExists()
    {
        var field = typeof(IronCfgHeader).GetField("HEADER_SIZE", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
    }

    [Fact(DisplayName = "cfg-struct-003: MAGIC constant exists")]
    public void CfgStruct003_MagicConstantExists()
    {
        var field = typeof(IronCfgHeader).GetField("MAGIC", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
    }

    [Fact(DisplayName = "cfg-struct-004: VERSION constant exists")]
    public void CfgStruct004_VersionConstantExists()
    {
        var field = typeof(IronCfgHeader).GetField("VERSION", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
    }

    [Fact(DisplayName = "cfg-val-002: ValidateFast method exists")]
    public void CfgVal002_ValidateFastMethodExists()
    {
        var method = typeof(IronCfgValidator).GetMethod("ValidateFast", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact(DisplayName = "cfg-val-003: ValidateStrict method exists")]
    public void CfgVal003_ValidateStrictMethodExists()
    {
        var method = typeof(IronCfgValidator).GetMethod("ValidateStrict", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact(DisplayName = "cfg-val-005: IronCfgErrorCode enum exists")]
    public void CfgVal005_IronCfgErrorCodeEnumExists()
    {
        var type = typeof(IronCfgErrorCode);
        Assert.NotNull(type);
        Assert.True(type.IsEnum);
    }
}
