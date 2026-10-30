using Fdp.Testing;
namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// MTB-P0-T2 SC: Verify the content layout — ⭐ CE-4089 (R-4097): the repository root holds <c>assets/behaviors/&lt;Kind&gt;</c> (final assets) and
/// <c>recipes/behaviors/Blueprints</c> (creation sources); the Behaviors PROJECT holds neither, and ships nothing of them into its output.
///
/// Finals (.bp.json/.hsm.json/.btree.json) are generator AdditionalFiles compiled into the assembly and are NOT copied to output.
/// </summary>
public sealed class FolderLayoutTests
{
    // ---- helpers ---------------------------------------------------------------

    /// <summary>
    /// Resolve the Behaviors source project directory (the .csproj location).
    /// </summary>
    private static string ResolveBehaviorsSourceDir()
    {
        var repoRoot = TestRepo.Root;
        var dir = Path.Combine(repoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                $"Hrot.AI.Behaviors source directory not found at: {dir}");
        return dir;
    }

    // ---- the layout --------------------------------------------------------------

    [Fact]
    public void TheRepositoryRootHoldsTheAssetsAndTheRecipes()
    {
        var repo = TestRepo.Root;

        // (a) recipes/behaviors/Blueprints exists with recipe templates (CountingDemo is the canonical one, committed since WHEN-M11-T4).
        var recipesDir = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorRecipes(repo, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder);
        Assert.True(Directory.Exists(recipesDir), $"Expected recipes/behaviors/Blueprints at the repository root: {recipesDir}");
        var recipeFiles = Directory.GetFiles(recipesDir, "*.bp.json");
        Assert.NotEmpty(recipeFiles);
        Assert.Contains(recipeFiles.Select(Path.GetFileName), f => f == "CountingDemo.bp.json");

        // (b) assets/behaviors/<Kind> directories exist with their files.
        var assetsBlueprintsDir = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(repo, Fdp.Toolkit.Orchestration.ContentLayout.BlueprintsFolder);
        Assert.True(Directory.Exists(assetsBlueprintsDir), $"Expected assets/behaviors/Blueprints: {assetsBlueprintsDir}");
        var assetBpFiles = Directory.GetFiles(assetsBlueprintsDir, "*.bp.json", SearchOption.AllDirectories);   // CE-3164: assets live in picker folders
        Assert.NotEmpty(assetBpFiles);
        Assert.Contains(assetBpFiles.Select(Path.GetFileName), f => f == "Count4.bp.json");

        var assetsHsmsDir = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(repo, Fdp.Toolkit.Orchestration.ContentLayout.HsmsFolder);
        Assert.True(Directory.Exists(assetsHsmsDir), $"Expected assets/behaviors/HSMs: {assetsHsmsDir}");
        var hsmFiles = Directory.GetFiles(assetsHsmsDir, "*.hsm.json", SearchOption.AllDirectories);
        Assert.NotEmpty(hsmFiles);
        Assert.Contains(hsmFiles.Select(Path.GetFileName), f => f == "SampleGuard.hsm.json");

        var assetsBTreesDir = Fdp.Toolkit.Orchestration.ContentLayout.BehaviorAssets(repo, Fdp.Toolkit.Orchestration.ContentLayout.BTreesFolder);
        Assert.True(Directory.Exists(assetsBTreesDir), $"Expected assets/behaviors/BTrees: {assetsBTreesDir}");
        var btreeFiles = Directory.GetFiles(assetsBTreesDir, "*.btree.json", SearchOption.AllDirectories);
        Assert.NotEmpty(btreeFiles);
        Assert.Contains(btreeFiles.Select(Path.GetFileName), f => f == "SampleScout.btree.json");
    }

    /// <summary>
    /// ⭐ CE-4089 — nothing of the content is left in (or shipped from) the Behaviors project: no <c>Assets/</c>, <c>Recipes/</c> or bare
    /// legacy folders, and the <c>.csproj</c> carries no copy-to-output rule (one pattern: a dev start seeds the NAS).
    /// </summary>
    [Fact]
    public void TheBehaviorsProjectHoldsNoContentAndCopiesNone()
    {
        var sourceDir = ResolveBehaviorsSourceDir();

        foreach (var legacy in new[] { "Assets", "Recipes", "Blueprints", "Machines", "Trees" })
        {
            var dir = Path.Combine(sourceDir, legacy);
            Assert.False(Directory.Exists(dir), $"{legacy}/ must not exist in the Behaviors project (content lives at the repository root): {dir}");
        }

        var csproj = File.ReadAllText(Path.Combine(sourceDir, "Hrot.AI.Behaviors.csproj"));
        Assert.DoesNotContain("<CopyToOutputDirectory>", csproj);   // the MSBuild item metadata, not a comment about it
    }
}
