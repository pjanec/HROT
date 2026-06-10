using System.Reflection;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// MTB-P0-T2 SC: Verify the §16 folder layout — recipe templates ship to
/// output under Recipes/Blueprints, source assets live under Assets/<Kind>,
/// and no legacy bare directories remain.
///
/// Finals (.bp.json/.hsm.json/.btree.json) are generator AdditionalFiles
/// compiled into the assembly and are NOT copied to output; this test does
/// not assert their presence in the output tree (see instruction note).
/// </summary>
public sealed class FolderLayoutTests
{
    // ---- helpers ---------------------------------------------------------------

    /// <summary>
    /// Resolve the repository root by walking up from the test assembly
    /// output directory until IOS-IG-SimHost.sln is found.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Could not find repo root (looked for IOS-IG-SimHost.sln upward from " +
            AppContext.BaseDirectory + ")");
    }

    /// <summary>
    /// Resolve the Behaviors source project directory (the .csproj location).
    /// </summary>
    private static string ResolveBehaviorsSourceDir()
    {
        var repoRoot = ResolveRepoRoot();
        var dir = Path.Combine(repoRoot, "Hrot", "Subsystems", "Hrot.AI.Behaviors");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                $"Hrot.AI.Behaviors source directory not found at: {dir}");
        return dir;
    }

    /// <summary>
    /// Resolve the Behaviors build-output directory from the loaded assembly.
    /// </summary>
    private static string ResolveBehaviorsOutputDir()
    {
        // Force-load Hrot.AI.Behaviors so we can resolve its output directory.
        var asm = Assembly.Load("Hrot.AI.Behaviors");
        var dir = Path.GetDirectoryName(asm.Location);
        if (dir == null || !Directory.Exists(dir))
            throw new DirectoryNotFoundException(
                "Cannot determine Hrot.AI.Behaviors assembly output directory.");
        return dir;
    }

    // ---- (a) Output — Recipes/Blueprints ships to build output ------------------

    /// <summary>
    /// (a) Output: the build-output Recipes/Blueprints dir exists and contains
    /// the recipe templates (e.g. CountingDemo.bp.json).
    /// Finals are NOT asserted — they are compiled in, not copied.
    /// </summary>
    [Fact]
    public void Output_HasAssetsAndRecipesRoots()
    {
        var outputDir = ResolveBehaviorsOutputDir();

        // (a) Output Recipes/Blueprints exists with recipe templates.
        var recipesDir = Path.Combine(outputDir, "Recipes", "Blueprints");
        Assert.True(Directory.Exists(recipesDir),
            $"Expected Recipes/Blueprints directory in output: {recipesDir}");

        var recipeFiles = Directory.GetFiles(recipesDir, "*.bp.json");
        Assert.NotEmpty(recipeFiles);

        // CountingDemo.bp.json is the canonical recipe committed since WHEN-M11-T4.
        var recipeNames = recipeFiles.Select(Path.GetFileName).ToHashSet();
        Assert.Contains("CountingDemo.bp.json", recipeNames);

        // (b) Source project: Assets/<Kind> directories exist with their files.
        var sourceDir = ResolveBehaviorsSourceDir();

        var assetsBlueprintsDir = Path.Combine(sourceDir, "Assets", "Blueprints");
        Assert.True(Directory.Exists(assetsBlueprintsDir),
            $"Expected Assets/Blueprints directory in source: {assetsBlueprintsDir}");
        var assetBpFiles = Directory.GetFiles(assetsBlueprintsDir, "*.bp.json");
        Assert.NotEmpty(assetBpFiles);
        Assert.Contains(assetBpFiles.Select(Path.GetFileName),
            f => f == "Count4.bp.json");

        var assetsHsmsDir = Path.Combine(sourceDir, "Assets", "HSMs");
        Assert.True(Directory.Exists(assetsHsmsDir),
            $"Expected Assets/HSMs directory in source: {assetsHsmsDir}");
        var hsmFiles = Directory.GetFiles(assetsHsmsDir, "*.hsm.json");
        Assert.NotEmpty(hsmFiles);
        Assert.Contains(hsmFiles.Select(Path.GetFileName),
            f => f == "SampleGuard.hsm.json");

        var assetsBTreesDir = Path.Combine(sourceDir, "Assets", "BTrees");
        Assert.True(Directory.Exists(assetsBTreesDir),
            $"Expected Assets/BTrees directory in source: {assetsBTreesDir}");
        var btreeFiles = Directory.GetFiles(assetsBTreesDir, "*.btree.json");
        Assert.NotEmpty(btreeFiles);
        Assert.Contains(btreeFiles.Select(Path.GetFileName),
            f => f == "SampleScout.btree.json");

        // Source Recipes/Blueprints exists with recipe templates.
        var sourceRecipesDir = Path.Combine(sourceDir, "Recipes", "Blueprints");
        Assert.True(Directory.Exists(sourceRecipesDir),
            $"Expected Recipes/Blueprints directory in source: {sourceRecipesDir}");
        var sourceRecipeFiles = Directory.GetFiles(sourceRecipesDir, "*.bp.json");
        Assert.NotEmpty(sourceRecipeFiles);
        Assert.Contains(sourceRecipeFiles.Select(Path.GetFileName),
            f => f == "CountingDemo.bp.json");

        // (c) No leftovers: no bare Blueprints/, Machines/, or Trees/ dirs remain.
        var bareBlueprintsDir = Path.Combine(sourceDir, "Blueprints");
        Assert.False(Directory.Exists(bareBlueprintsDir),
            $"Bare Blueprints/ directory must not exist in source: {bareBlueprintsDir}");

        var machinesDir = Path.Combine(sourceDir, "Machines");
        Assert.False(Directory.Exists(machinesDir),
            $"Machines/ directory must not exist in source: {machinesDir}");

        var treesDir = Path.Combine(sourceDir, "Trees");
        Assert.False(Directory.Exists(treesDir),
            $"Trees/ directory must not exist in source: {treesDir}");
    }
}
