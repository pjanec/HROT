using Hrot.Blueprints.Editor.Catalog;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// MTB-P0-T3 SC2: Verify final-asset scans (Assets/<Kind>) exclude recipes
/// (Recipes/<Kind>), so recipes never appear in the Asset Browser.
/// </summary>
public sealed class AssetScanTests : IDisposable
{
    private readonly string _tempDir;

    public AssetScanTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AssetScan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteBpJson(string directory, string fileName, Guid assetId, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var json = $"{{\"AssetId\":\"{assetId:D}\",\"Name\":\"{name}\",\"Dispatch\":\"Library\",\"Graphs\":[]}}";
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// A recipe placed under Recipes/Blueprints must NOT be returned by a
    /// final-asset contributor whose root is Assets/Blueprints.
    /// </summary>
    [Fact]
    public void RecipesExcludedFromFinalScan()
    {
        var assetsDir  = Path.Combine(_tempDir, "Assets", "Blueprints");
        var recipesDir = Path.Combine(_tempDir, "Recipes", "Blueprints");

        var finalId   = new Guid("f1000000-0001-0001-0001-000000000001");
        var recipeId  = new Guid("a2000000-0002-0002-0002-000000000002");

        WriteBpJson(assetsDir,  "FinalBlueprint.bp.json",  finalId,  "FinalBlueprint");
        WriteBpJson(recipesDir, "RecipeTemplate.bp.json",  recipeId, "RecipeTemplate");

        // Contributor scans ONLY the Assets root (as per the new layout).
        var contributor = new BlueprintAssetContributor(assetsDir);
        contributor.Refresh();

        var assets = contributor.Enumerate();

        // The final asset must be present.
        Assert.Contains(assets, a => a.AssetId == finalId);

        // The recipe must NOT be present.
        Assert.DoesNotContain(assets, a => a.AssetId == recipeId);

        // And the recipe directory should not be empty (sanity — the file exists).
        Assert.True(Directory.Exists(recipesDir));
        Assert.NotEmpty(Directory.GetFiles(recipesDir, "*.bp.json"));
    }

    /// <summary>
    /// A final asset under Assets/Blueprints IS returned — confirming the
    /// contributor correctly scans the new Assets root.
    /// </summary>
    [Fact]
    public void FinalAssetUnderAssetsRoot_IsReturned()
    {
        var assetsDir = Path.Combine(_tempDir, "Assets", "Blueprints");

        var assetId = new Guid("f3000000-0003-0003-0003-000000000003");
        WriteBpJson(assetsDir, "MyBlueprint.bp.json", assetId, "MyBlueprint");

        var contributor = new BlueprintAssetContributor(assetsDir);
        contributor.Refresh();

        var assets = contributor.Enumerate();

        Assert.Single(assets);
        Assert.Equal(assetId, assets[0].AssetId);
        Assert.Equal("MyBlueprint", assets[0].Name);
    }
}
