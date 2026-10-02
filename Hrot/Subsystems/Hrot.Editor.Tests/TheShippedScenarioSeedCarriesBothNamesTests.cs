using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Map.Common;
using Hrot.Map.Common.Scenario;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <c>H4</c> — <b>the rail that proves the whole of stage <c>H</c>.</b>
///
/// <para>🔒 §2.1e ⑤d's point is that the terrain and TKB names are <b>CARRIED</b>, not chosen: a scenario
/// recipe is an ordinary scenario whose header already holds both, and <c>FromSeed</c> loads it and saves
/// it under the new name <b>header and all</b>. ⇒ ⛔ no new asset format, no new registry, and no
/// terrain/TKB fields on the kind-agnostic <c>NewAssetDialog</c>.</para>
///
/// <para>⭐⭐ <b>Why this is the load-bearing rail.</b> 📐 Measured before this batch: <b>no scenario in
/// the repository names a TKB at all</b> — all four curated scenarios carry
/// <c>{subsystemType, schemaVersion}</c> and nothing else — so the <c>TkbName</c> path had never been
/// exercised from a file, and <c>B5</c>'s <c>TerrainName</c> beside it had no producer either. ⇒ ⭐ this
/// is the first shipped artifact that fills either field.</para>
///
/// <para>⚠⚠ <b>The stated limit, so nobody reads a green here as "the seed loads".</b> 📐 A named TKB
/// resolves to <c>{node staging}/{TkbName}.zip</c> (<c>TkbLoadClusterStateHandler.cs:78</c>) and a named
/// terrain to <c>{node staging}/Terrain/{TerrainName}.json</c>
/// (<c>TerrainLoadClusterStateHandler.cs:138</c>); <b>each throws when its artifact is absent</b>, and
/// nothing in this repository stages either. ⇒ ⛔ creating from this seed on a real cluster FAILS LOUDLY
/// until those artifacts are staged. ⭐ That is the designed behaviour, not an accident — §8.3 N4 rules
/// that a host missing the named terrain must fail loudly *"instead of silently passing"* — but it means
/// these rails assert the CARRYING, which is <c>H4</c>'s claim, and deliberately not an end-to-end load.
/// 📄 filed as a defect in the tracker; design §10.8.</para>
/// </summary>
public sealed class TheShippedScenarioSeedCarriesBothNamesTests
{
    private const string SeedName = "basic-desert";

    private static string SeedFile =>
        Path.Combine(AssetRoots.ScenariosRecipesRoot, SeedName, "scenario.json");

    /// <summary>
    /// ⭐ The seed is DEPLOYED — the csproj <c>Content</c> item reaches the output tree at the path
    /// <see cref="AssetRoots.ScenariosRecipesRoot"/> resolves. ⛔ Without this, every other rail here
    /// would pass vacuously on a file the product never sees.
    /// </summary>
    [Fact]
    public void TheSeedIsDeployedWhereTheRecipesRootLooks()
    {
        Assert.True(File.Exists(SeedFile),
            $"The shipped scenario seed is missing at '{SeedFile}'. It ships as a csproj Content item "
          + "from Hrot.AI.Behaviors/Recipes/Scenarios/, mirroring the blueprint recipes.");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>H4</c>'s claim: the header carries <b>BOTH</b> names, and they survive a round trip
    /// through the very serializer options the product saves and loads with.
    /// </summary>
    [Fact]
    public void TheSeedHeaderCarriesBothNames_AndTheyRoundTrip()
    {
        var header = JsonSerializer.Deserialize<ScenarioEnvelopeProbe>(
            File.ReadAllText(SeedFile), HrotSerializerOptions.HrotJsonOptions)?.Header;

        Assert.NotNull(header);
        Assert.False(string.IsNullOrWhiteSpace(header!.TkbName),
            "The seed must NAME a TKB — carrying it is the entire point of the seed (§2.1e ⑤d).");
        Assert.False(string.IsNullOrWhiteSpace(header.TerrainName),
            "The seed must NAME a terrain — B5 added the field and this is its first producer.");

        // ⭐ The round trip, through the product's own options: write it back out and read it again.
        //   ⚠ This is what "nothing re-authors them" means concretely — a save of a scenario created
        //     from this seed must not drop or rewrite either name.
        var reparsed = JsonSerializer.Deserialize<ScenarioHeaderDto>(
            JsonSerializer.Serialize(header, HrotSerializerOptions.HrotJsonOptions),
            HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(reparsed);
        Assert.Equal(header.TkbName, reparsed!.TkbName);
        Assert.Equal(header.TerrainName, reparsed.TerrainName);
        Assert.Equal(header.SubsystemType, reparsed.SubsystemType);
    }

    /// <summary>
    /// ⭐⭐ <c>H1</c> end to end against the REAL deployed tree: the discoverer
    /// <c>EditorSubsystem</c> passes finds the shipped seed, so the picker offers it.
    /// ⚠ Asserting through <c>CuratedRelPaths</c> rather than a bespoke scan is deliberate — that is the
    /// enumerator production uses, so a green here is a statement about production.
    /// </summary>
    [Fact]
    public void TheSeedIsDiscoveredAsARecipe_ByTheEnumeratorProductionUses()
    {
        var discovered = Hrot.ScenarioEditor.Services.CuratedScenarios.CuratedRelPaths(
            AssetRoots.ScenariosRecipesRoot);

        Assert.Contains(SeedName, discovered);

        var svc = new ScenarioNewAssetService(
            new FakeScenarioCreationSession(),
            () => Hrot.ScenarioEditor.Services.CuratedScenarios.CuratedRelPaths(
                      AssetRoots.ScenariosRecipesRoot));

        Assert.Contains(svc.AvailableRecipes(), r => r.Name == SeedName);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>H2</c> against the real seed: creating from it loads <c>Recipes/basic-desert</c> — the
    /// name the cluster can resolve — and saves under the requested name. 📄 See
    /// <c>ScenarioNewAssetService.SeedSubfolder</c> for why the prefix and not a path.
    /// </summary>
    [Fact]
    public void CreatingFromTheSeed_LoadsItByItsStagedName()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(
            session,
            () => Hrot.ScenarioEditor.Services.CuratedScenarios.CuratedRelPaths(
                      AssetRoots.ScenariosRecipesRoot));

        var seed = svc.AvailableRecipes().First(r => r.Name == SeedName);
        svc.CreateNew(seed, "MyNewScenario", "Combat");

        Assert.Equal($"Recipes/{SeedName}", Assert.Single(session.LoadScenarioByNameCalls));
        Assert.Equal("Combat/MyNewScenario", Assert.Single(session.SaveScenarioAsCalls));
    }

    /// <summary>Minimal envelope shape — only the header matters to these claims.</summary>
    private sealed class ScenarioEnvelopeProbe
    {
        public ScenarioHeaderDto? Header { get; set; }
    }
}
