using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Loading;
using Xunit;

namespace Hrot.Utility.Editor.Tests.Loading;

public sealed class UtilityAssetLoaderTests
{
    // ---- Helper --------------------------------------------------------

    private static string MakeSampleFile(
        bool   addMarker       = true,
        string assetId         = "3c6f9e42-5d10-6f3a-ac23-000000000001",
        string displayName     = "Combat Posture",
        string kind            = "PostureSelect",
        string category        = "Tactical/Posture",
        float  hysteresisBonus = 0f)
    {
        var sb = new StringBuilder();
        if (addMarker)
        {
            sb.Append("// HROT_EDITOR_GENERATED\n");
            sb.Append($"// AssetId: {assetId}\n\n");
        }
        sb.Append("[UtilityDecision(\n");
        sb.Append($"    assetId:     \"{assetId}\",\n");
        sb.Append($"    displayName: \"{displayName}\",\n");
        sb.Append($"    kind:        DecisionKind.{kind},\n");
        if (hysteresisBonus != 0f)
            sb.Append($"    hysteresisBonus: {hysteresisBonus.ToString("R", CultureInfo.InvariantCulture)}f,\n");
        sb.Append($"    category:    \"{category}\")]\n");
        sb.Append($"public sealed partial class CombatPostureDecision : IUtilityDecisionDefinition\n");
        sb.Append("{\n    public static void Build(IUtilityDecisionBuilder b) => b;\n}\n");
        return sb.ToString();
    }

    // ---- Tests ---------------------------------------------------------

    [Fact]
    public void Load_FileNotFound_ReturnsReadOnlyWithWarning()
    {
        string path   = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_missing.cs");
        var    result = UtilityAssetLoader.Load(path);

        Assert.False(result.Asset.IsEditorOwned);
        Assert.Single(result.Warnings);
        Assert.Contains("File not found", result.Warnings[0]);
    }

    [Fact]
    public void Load_FileWithGeneratedMarker_IsEditorOwnedTrue()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(addMarker: true));
            var result = UtilityAssetLoader.Load(path);
            Assert.True(result.Asset.IsEditorOwned);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FileWithoutGeneratedMarker_IsEditorOwnedFalse()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(addMarker: false));
            var result = UtilityAssetLoader.Load(path);
            Assert.False(result.Asset.IsEditorOwned);
            Assert.Contains("read-only", result.Warnings[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtractsAssetId_FromAttribute()
    {
        string guid = "3c6f9e42-5d10-6f3a-ac23-000000000001";
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(assetId: guid));
            var result = UtilityAssetLoader.Load(path);
            Assert.Equal(new Guid(guid), result.Asset.AssetId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtractsDisplayName_FromAttribute()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(displayName: "Flank Posture"));
            var result = UtilityAssetLoader.Load(path);
            Assert.Equal("Flank Posture", result.Asset.DisplayName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtractsDecisionKind_FromAttribute()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(kind: "ThreatRanking"));
            var result = UtilityAssetLoader.Load(path);
            Assert.Equal(DecisionKind.ThreatRanking, result.Asset.DecisionKind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtractsCategory_FromAttribute()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(category: "Combat/Threat"));
            var result = UtilityAssetLoader.Load(path);
            Assert.Equal("Combat/Threat", result.Asset.Category);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ExtractsHysteresisBonus_WhenPresent()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, MakeSampleFile(hysteresisBonus: 0.08f));
            var result = UtilityAssetLoader.Load(path);
            Assert.Equal(0.08f, result.Asset.HysteresisBonus, precision: 5);
        }
        finally { File.Delete(path); }
    }
}
