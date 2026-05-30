using System.IO;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Utility.Editor.Comparison;
using Xunit;

namespace Hrot.Utility.Editor.Tests.Comparison;

public sealed class UtilityComparisonSanitizerTests : IDisposable
{
    private const string SampleDecisionId = "3c6f9e42-5d10-6f3a-ac23-000000000001";
    private const string GeneratedMarker  = "// HROT_EDITOR_GENERATED";

    private readonly string _tmpDir;
    private readonly UtilityComparisonSanitizer _sut = new();

    public UtilityComparisonSanitizerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    // ---- Helpers ----

    private string WriteFile(string content)
    {
        string path = Path.Combine(_tmpDir, Path.GetRandomFileName() + ".cs");
        File.WriteAllText(path, content);
        return path;
    }

    private static string MakeSampleContent(bool includeLayout = true)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("// HROT_EDITOR_GENERATED - manual edits to this file will be overwritten by the AI editor on next save.\n");
        sb.Append($"// AssetId: {SampleDecisionId}\n");
        sb.Append("\n");
        sb.Append("using Fdp.Toolkit.Utility;\n");
        sb.Append("\n");
        sb.Append("[UtilityDecision(\n");
        sb.Append($"    assetId:     \"{SampleDecisionId}\",\n");
        sb.Append("    displayName: \"Combat Posture\")]\n");
        sb.Append("public sealed partial class CombatPosture : IUtilityDecisionDefinition\n");
        sb.Append("{\n");
        sb.Append("    public static void Build(IUtilityDecisionBuilder b) => b;\n");
        if (includeLayout)
        {
            sb.Append("\n");
            sb.Append("    [UtilityLayout]\n");
            sb.Append("    public static void Layout(IUtilityLayoutBuilder b)\n");
            sb.Append("    {\n");
            sb.Append("        // layout data\n");
            sb.Append("    }\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    private SanitizationResult RunOnText(string content)
    {
        string path    = WriteFile(content);
        var    request = new AssetExportRequest(path, null, AssetKind.Utility);
        return _sut.Sanitize(request);
    }

    // ---- Tests ----

    [Fact]
    public void Sanitize_FileNotFound_ReturnsEmptyTextWithWarning()
    {
        var request = new AssetExportRequest(
            Path.Combine(_tmpDir, "nonexistent.cs"), null, AssetKind.Utility);
        var result  = _sut.Sanitize(request);

        Assert.Equal(string.Empty, result.SanitizedText);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Sanitize_StripsSuffix_FromGeneratedMarkerLine()
    {
        var result = RunOnText(MakeSampleContent());

        var lines = result.SanitizedText.Split('\n');
        var markerLine = lines.First(l => l.TrimStart().StartsWith(GeneratedMarker, StringComparison.Ordinal));
        Assert.Equal(GeneratedMarker, markerLine.Trim());
    }

    [Fact]
    public void Sanitize_StripsLayoutBlock_WhenPresent()
    {
        var result = RunOnText(MakeSampleContent(includeLayout: true));

        Assert.DoesNotContain("[UtilityLayout]", result.SanitizedText);
        Assert.DoesNotContain("Layout(", result.SanitizedText);
    }

    [Fact]
    public void Sanitize_PreservesDecisionAttribute()
    {
        var result = RunOnText(MakeSampleContent());

        Assert.Contains("[UtilityDecision(", result.SanitizedText);
    }

    [Fact]
    public void Sanitize_PreservesBuildMethod()
    {
        var result = RunOnText(MakeSampleContent());

        Assert.Contains("void Build(", result.SanitizedText);
    }

    [Fact]
    public void Sanitize_ExtractsAssetId_FromHeader()
    {
        var result = RunOnText(MakeSampleContent());

        Assert.Equal(Guid.Parse(SampleDecisionId), result.Metadata.AssetId);
    }

    [Fact]
    public void Sanitize_ExtractsAssetName_FromClassDeclaration()
    {
        var result = RunOnText(MakeSampleContent());

        Assert.Equal("CombatPosture", result.Metadata.AssetName);
    }

    [Fact]
    public void Sanitize_Deterministic_SameInputTwice()
    {
        string content  = MakeSampleContent();
        var    result1  = RunOnText(content);
        var    result2  = RunOnText(content);

        Assert.Equal(result1.SanitizedText, result2.SanitizedText);
    }

    [Fact]
    public void Sanitize_NoLayoutBlock_NoWarning()
    {
        var result = RunOnText(MakeSampleContent(includeLayout: false));

        Assert.Empty(result.Warnings);
        Assert.Contains("void Build(", result.SanitizedText);
    }
}
