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
/// ⭐⭐⭐ <c>H4</c> — <b>the rail that proves the whole of stage <c>H</c>.</b> ⛔⛔ <c>R-4018</c> (<c>2026-10-15</c>): the seed
/// carries the TERRAIN name only — a scenario never names a TKB. Everything below about a TKB name is history.
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

    /// <summary>⭐ CE-4089 — the seeds live in the repository's <c>recipes/scenarios</c> (what a dev start copies into the NAS).</summary>
    private static string SeedRoot => Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipes(
        Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!);

    private static string SeedFile => Path.Combine(SeedRoot, SeedName, "scenario.json");

    /// <summary>
    /// ⭐ The seed EXISTS where the dev seed copies it from (<c>recipes/scenarios</c> at the repository root; CE-4089 — it used to ship as a csproj
    /// <c>Content</c> item). ⛔ Without this, every other rail here would pass vacuously on a file the product never sees.
    /// </summary>
    [Fact]
    public void TheSeedIsDeployedWhereTheRecipesRootLooks()
    {
        Assert.True(File.Exists(SeedFile),
            $"The scenario seed is missing at '{SeedFile}'. Seeds live in recipes/scenarios at the repository root, "
          + "and a dev start copies them into the NAS.");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>H4</c>'s claim, as amended by <c>R-4018</c>: the header carries the TERRAIN name, it survives a round trip
    /// through the very serializer options the product saves and loads with — and the seed names NO TKB (a scenario never
    /// does; the deployment's one TKB is synced by the orchestrator). ⛔ Was: "carries BOTH names".
    /// </summary>
    [Fact]
    public void TheSeedHeaderCarriesTheTerrainName_AndNoTkbName()
    {
        var raw = File.ReadAllText(SeedFile);
        Assert.DoesNotContain("tkbname", raw, System.StringComparison.OrdinalIgnoreCase);

        var header = JsonSerializer.Deserialize<ScenarioEnvelopeProbe>(raw, HrotSerializerOptions.HrotJsonOptions)?.Header;

        Assert.NotNull(header);
        Assert.False(string.IsNullOrWhiteSpace(header!.TerrainName),
            "The seed must NAME a terrain — B5 added the field and this is its first producer.");

        var reparsed = JsonSerializer.Deserialize<ScenarioHeaderDto>(
            JsonSerializer.Serialize(header, HrotSerializerOptions.HrotJsonOptions),
            HrotSerializerOptions.HrotJsonOptions);

        Assert.NotNull(reparsed);
        Assert.Equal(header.TerrainName, reparsed!.TerrainName);
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
        var discovered = Fdp.Toolkit.Orchestration.ContentSeed.CuratedRelPaths(
            SeedRoot);

        Assert.Contains(SeedName, discovered);

        var svc = new ScenarioNewAssetService(
            new FakeScenarioCreationSession(),
            () => Fdp.Toolkit.Orchestration.ContentSeed.CuratedRelPaths(
                      SeedRoot));

        Assert.Contains(svc.AvailableRecipes(), r => r.Name == SeedName);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>H2</c> against the real seed: creating from it loads <c>recipes/scenarios/basic-desert</c> — the
    /// name the cluster can resolve (L6: a recipe is named by its root) — and saves under the requested name.
    /// </summary>
    [Fact]
    public void CreatingFromTheSeed_LoadsItByItsStagedName()
    {
        var session = new FakeScenarioCreationSession();
        var svc = new ScenarioNewAssetService(
            session,
            () => Fdp.Toolkit.Orchestration.ContentSeed.CuratedRelPaths(
                      SeedRoot));

        var seed = svc.AvailableRecipes().First(r => r.Name == SeedName);
        svc.CreateNew(seed, "MyNewScenario", "Combat");

        Assert.Equal($"recipes/scenarios/{SeedName}", Assert.Single(session.LoadScenarioByNameCalls));
        Assert.Equal("Combat/MyNewScenario", Assert.Single(session.SaveScenarioAsCalls));
    }

    /// <summary>Minimal envelope shape — only the header matters to these claims.</summary>
    private sealed class ScenarioEnvelopeProbe
    {
        public ScenarioHeaderDto? Header { get; set; }
    }
}
