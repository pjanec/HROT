using System;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// CE-3340 (1) / AQ108 D7 - an OPENED copy of a recording (and later a never-saved autosave, A8) has ONE home, the NAS "opened, not saved" area
/// (<c>{nas}/opened/...</c>): neither <c>recipes/</c> nor <c>scenarios/</c>. So <i>New from a recipe</i> (lists <c>recipes/scenarios</c>) and <i>Open</i> (lists <c>scenarios/</c>)
/// can never offer it - by construction, not by a filter. The folder the master's prefetch stages to is <c>RecordedScenarioNames.DirectoryFor</c>
/// (pinned by <c>AssetInventoryProcessManagerTests.CE4091_OpeningARecordedCopy_*</c>), and the two lists are driven here over the very roots the editor
/// composes them from (<c>AssetRoots.ScenariosRecipesRoot</c> = <c>ContentLayout.ScenarioRecipes</c>, <c>EditorBootstrap.ScenariosRoot</c> = <c>ContentLayout.Scenarios</c>).
/// Red proof: <c>ContentLayout.Opened</c> returning <c>Scenarios(root)/opened</c> (or <c>Recipes(root)/opened</c>) lists the copy in Open (New) and fails.
/// </summary>
public sealed class TheOpenedCopiesAreNeverOfferedTests : IDisposable
{
    private readonly string _nas = Path.Combine(Path.GetTempPath(), "opened-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_nas, recursive: true); } catch { /* best effort */ } }

    private static void WriteScenario(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ContentSeed.MarkerFileName), "{}");
    }

    [Fact]
    public void AStagedRecordedCopy_IsOfferedByNeitherNewFromARecipeNorOpen()
    {
        var recording = Guid.NewGuid();
        WriteScenario(Path.Combine(ContentLayout.Scenarios(_nas), "saved-one"));              // a saved scenario: Open offers it
        WriteScenario(Path.Combine(ContentLayout.ScenarioRecipes(_nas), "a-recipe"));         // a recipe: New offers it
        WriteScenario(RecordedScenarioNames.DirectoryFor(_nas, recording));                   // the staged recorded copy

        var open = ScenarioEnumeration.EnumerateRelPaths(ContentLayout.Scenarios(_nas));
        var newFromRecipe = new ScenarioNewAssetService(new FakeScenarioCreationSession(),
                () => ContentSeed.CuratedRelPaths(ContentLayout.ScenarioRecipes(_nas)))
            .AvailableRecipes().Select(r => r.Name).ToArray();

        Assert.Equal(new[] { "saved-one" }, open);
        Assert.Equal(new[] { "a-recipe" }, newFromRecipe);
        Assert.True(Directory.Exists(RecordedScenarioNames.DirectoryFor(_nas, recording)));   // it IS there - just in neither list
    }

    /// <summary>The copy's load NAME resolves to the opened area, and the recipe name to the recipes root - two different homes, neither under scenarios/.</summary>
    [Fact]
    public void TheNamesOfARecordedCopyAndOfARecipe_ResolveOutsideTheScenariosRoot()
    {
        var recording = Guid.NewGuid();
        var copy = ContentLayout.ResolveScenarioDirectory(_nas, RecordedScenarioNames.ForExercise(recording));
        var recipe = ContentLayout.ResolveScenarioDirectory(_nas, ScenarioNewAssetService.SeedNameToScenarioName("a-recipe"));

        Assert.StartsWith(ContentLayout.Opened(_nas) + Path.DirectorySeparatorChar, copy);
        Assert.StartsWith(ContentLayout.ScenarioRecipes(_nas) + Path.DirectorySeparatorChar, recipe);
        Assert.DoesNotContain(ContentLayout.Scenarios(_nas) + Path.DirectorySeparatorChar, copy + Path.DirectorySeparatorChar);
        Assert.DoesNotContain(ContentLayout.Scenarios(_nas) + Path.DirectorySeparatorChar, recipe + Path.DirectorySeparatorChar);
    }
}
