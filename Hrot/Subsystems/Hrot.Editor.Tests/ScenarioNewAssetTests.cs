using System.Collections.Generic;
using System.Linq;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Xunit;

namespace Hrot.Editor.Tests;

public sealed class ScenarioNewAssetTests
{
    // ── H3 — THE "EMPTY" CLAIMS, RE-HOMED ─────────────────────────────────────
    //
    // 🔴 Two tests here used to assert that CreateNew(null, …) calls NewScenario() then
    //    SaveScenarioAs — i.e. that the Scenario kind mints a BLANK scenario. H3 retires that path, so
    //    those claims are re-homed rather than deleted (HN-037: every claim is re-homed or deleted with
    //    a stated reason). What survives is the part that was actually load-bearing:
    //      · "the full name is <relPath>/<name>, or <name> when relPath is empty"  -> asserted on the
    //        SEED path below, which is now the only creation path.
    //      · "a blank scenario is created"                                          -> INVERTED: it is
    //        refused, because a scenario with no terrain and no TKB cannot acquire either afterwards
    //        (design §2.1e ⑤c G3). The refusal names the seeds that would have worked.

    /// <summary>
    /// ⭐⭐⭐ <c>H3</c> — there is no blank template, and asking for one is REFUSED with the list of
    /// seeds. ⛔ Not a silent blank: that is the trap the whole stage exists to close.
    /// </summary>
    [Fact]
    public void Create_WithNoRecipe_IsRefused_AndNamesTheSeeds()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "basic-desert" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => svc.CreateNew(null, "MyScenario", "Combat"));

        Assert.Contains("basic-desert", ex.Message);
        Assert.Equal(0, session.NewScenarioCallCount);
        Assert.Empty(session.SaveScenarioAsCalls);
    }

    /// <summary>
    /// ⚠ The refusal is still useful when NOTHING is deployed — it says so rather than listing an
    /// empty set, which would read as a bug in the picker.
    /// </summary>
    [Fact]
    public void Create_WithNoRecipeAndNoSeeds_SaysNoSeedsAreDeployed()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        var ex = Assert.Throws<InvalidOperationException>(
            () => svc.CreateNew(null, "MyScenario", ""));

        Assert.Contains("Recipes/Scenarios", ex.Message);
    }

    /// <summary>
    /// ⭐ <c>H3</c> — <c>IsBlankTemplate</c> is ALWAYS false, which is what makes
    /// <c>RecipeByName.Resolve(service, null)</c> return a null recipe for <c>CreateNew</c> to refuse.
    /// </summary>
    [Fact]
    public void IsBlankTemplate_IsAlwaysFalse()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "basic-desert" });

        Assert.All(svc.AvailableRecipes(), r => Assert.False(svc.IsBlankTemplate(r)));
    }

    /// <summary>
    /// ⭐⭐⭐ <c>H2</c> / <c>U9</c> — the seed is loaded by the name the CLUSTER can resolve: the
    /// reserved <c>Recipes/</c> subfolder of the NAS scenarios root the seeds are staged into.
    ///
    /// <para>🔴 <b>This assertion is the whole of <c>U9</c>.</b> 📐 Measured: a scenario load is a CLUSTER
    /// transition keyed on a NAME every node resolves against its own NAS scenarios root
    /// (<c>EditorScenarioSession.OpenForEdit</c>) — nothing accepts a path ⇒ a seed left in the local
    /// <c>Recipes/Scenarios</c> output tree is unreachable however the seam is widened. ⭐ The name
    /// <c>Recipes/&lt;seed&gt;</c> works because <c>OpenForEdit</c>'s contract already allows a relative
    /// path. ⛔ Asserting the bare seed name here — as this test did before <c>H2</c> — pins the
    /// behaviour that cannot load.</para>
    /// </summary>
    [Fact]
    public void Create_FromSeed_LoadsTheSeedByItsStagedName_ThenSavesAs()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "SeedScenario" });

        var seedRecipe = svc.AvailableRecipes().First(r => r.Name == "SeedScenario");
        var result = svc.CreateNew(seedRecipe, "NewName", "Sub");

        var loadCall = Assert.Single(session.LoadScenarioByNameCalls);
        Assert.Equal("Recipes/SeedScenario", loadCall);
        Assert.Equal(ScenarioNewAssetService.SeedSubfolder + "/SeedScenario", loadCall);

        var saveCall3 = Assert.Single(session.SaveScenarioAsCalls);
        Assert.Equal("Sub/NewName", saveCall3);

        Assert.Equal("NewName", result.Name);
        Assert.Equal(AssetKind.Scenario, result.Kind);
        Assert.NotEqual(Guid.Empty, result.AssetId);
    }

    /// <summary>
    /// ⭐ Re-homed from the deleted <c>Create_Empty_NoRelPath_SavesWithNameOnly</c>: an empty
    /// <c>relPath</c> still saves under the bare name. The claim was never about "Empty" — it was about
    /// name composition, which now lives on the only creation path there is.
    /// </summary>
    [Fact]
    public void Create_FromSeed_NoRelPath_SavesWithNameOnly()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "SeedScenario" });

        svc.CreateNew(svc.AvailableRecipes().First(), "FlatScenario", "");

        Assert.Equal("FlatScenario", Assert.Single(session.SaveScenarioAsCalls));
    }

    /// <summary>
    /// ⭐ <c>H1</c> + <c>H3</c> — the recipes are the SEEDS, and nothing else. ⚠ This test previously
    /// asserted <c>3</c> and required an <c>"Empty"</c> member; that claim is retired by <c>H3</c>, and
    /// what remains of it — "every recipe is of Scenario kind" — is kept.
    /// </summary>
    [Fact]
    public void AvailableRecipes_AreTheSeeds_AndNothingElse()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session, new[] { "SeedA", "SeedB" });

        var recipes = svc.AvailableRecipes();
        Assert.Equal(2, recipes.Count);
        Assert.Contains(recipes, r => r.Name == "SeedA");
        Assert.Contains(recipes, r => r.Name == "SeedB");
        Assert.DoesNotContain(recipes, r => r.Name == "Empty");
        Assert.All(recipes, r => Assert.Equal(AssetKind.Scenario, r.Kind));
    }

    /// <summary>
    /// ⭐⭐⭐ <c>H1</c>'s stated success condition — <c>AvailableRecipes()</c> <b>re-reads the directory
    /// LIVE</b>, per open, and is not snapshotted at composition.
    ///
    /// <para>🔴 <b>This rail caught a real defect in this batch.</b> The first draft materialised the seed
    /// list in the constructor: the count was right on the first open and then stale forever — which is
    /// indistinguishable from working until someone adds a seed. ⭐ The service now holds a PROVIDER.</para>
    /// </summary>
    [Fact]
    public void AvailableRecipes_ReReadsTheSeedSource_Live()
    {
        var session = new FakeScenarioCreationSession();
        var live = new List<string> { "SeedA" };
        var svc = new ScenarioNewAssetService(session, () => live);

        Assert.Single(svc.AvailableRecipes());

        live.Add("SeedB");                       // a seed appears on disk between opens

        Assert.Equal(2, svc.AvailableRecipes().Count);
        Assert.Contains(svc.AvailableRecipes(), r => r.Name == "SeedB");
    }

    /// <summary>
    /// ⚠ The NAME check precedes the recipe check deliberately: a caller that got both wrong should
    /// hear about the blank name, which is the simpler mistake to correct.
    /// </summary>
    [Fact]
    public void CreateNew_NullName_Throws()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "", ""));
        Assert.Throws<ArgumentException>(() => svc.CreateNew(null, "  ", ""));
    }

    [Fact]
    public void Kind_IsScenario()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(session);

        Assert.Equal(AssetKind.Scenario, svc.Kind);
    }
}

/// <summary>
/// Fake implementation of <see cref="IScenarioCreationSession"/> for testing.
/// </summary>
public sealed class FakeScenarioCreationSession : IScenarioCreationSession
{
    public int NewScenarioCallCount { get; private set; }
    public readonly List<string> SaveScenarioAsCalls = new();
    public readonly List<string> LoadScenarioByNameCalls = new();

    public void NewScenario()
    {
        NewScenarioCallCount++;
    }

    public void SaveScenarioAs(string scenarioName)
    {
        SaveScenarioAsCalls.Add(scenarioName);
    }

    public void LoadScenarioByName(string scenarioName)
    {
        LoadScenarioByNameCalls.Add(scenarioName);
    }
}
