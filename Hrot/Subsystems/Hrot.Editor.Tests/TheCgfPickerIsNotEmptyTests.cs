using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b>The CGF windowed corrective — rails for symptoms 3–6 of the user's <c>--mode cgf</c> visual
/// check *(`2026-08-26`)*.</b>
/// 📄 `HANDOFF_Cgf_Windowed_Corrective.md` §1. IDs `CE-053` *(the scenario contributor)* and
/// `CE-054` *(the perspective toolbar section)*.
///
/// <para>🔴🔴 <b>WHY THE HARNESS WAS BLIND, which is the whole point of this file.</b> The conformance
/// rails compare panel MODELS across hosts, and `CE-049`'s equality rail asserted the two hosts register
/// the same menu ITEMS with the same enablement. 📐 All of that was TRUE and GREEN while
/// `File/Edit/Open Scenario` opened an **empty** picker on CGF: the item existed, was enabled, and its
/// handler ran — the *catalog behind it* held no scenarios. ⇒ ⭐⭐ <b>"the control is present and enabled"
/// is a strictly weaker claim than "the control has something to offer"</b>, and nothing asserted the
/// second one.</para>
///
/// <para>⭐ These rails assert the CONTENT chain — contributor → catalog → picker source → entries — which
/// is the layer the model-level comparison cannot see.</para>
/// </summary>
public sealed class TheCgfPickerIsNotEmptyTests : IDisposable
{
    private readonly string _root;

    public TheCgfPickerIsNotEmptyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cgf-picker-rails-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(_root, "alpha", "scenario.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_root, "Combat", "Ambush"));
        File.WriteAllText(Path.Combine(_root, "Combat", "Ambush", "scenario.json"), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    // ══ ① THE CONTENT CHAIN — contributor → catalog → picker ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A catalog carrying the scenario contributor offers scenarios to the picker.</b>
    /// ⚠ This is the rail that would have caught symptoms 4–6: it goes all the way to the
    /// <see cref="AssetPickerSource"/> entries the picker actually lists, ⛔ not merely to "a contributor
    /// is registered".
    /// </summary>
    [Fact]
    public void AScenarioContributorMakesTheScenarioPickerNonEmpty()
    {
        var catalog = new AssetCatalog();
        catalog.AddContributor(new ScenarioCatalogContributor(
            () => ScenarioEnumeration.EnumerateRelPaths(_root)));

        var scenarios = catalog.All.Where(a => a.Kind == AssetKind.Scenario).ToList();
        Assert.Equal(2, scenarios.Count);

        // ⭐ …and through the picker source the launcher actually builds.
        var source  = new AssetPickerSource(catalog, AssetKindFilter.Scenario);
        var entries = source.BuildEntries("", null);
        Assert.NotEmpty(entries);
    }

    /// <summary>
    /// ⭐⭐ <b>The inverse, stated so the rail above cannot pass vacuously:</b> WITHOUT the contributor the
    /// scenario picker is empty — which is exactly the state CGF shipped in.
    /// </summary>
    [Fact]
    public void WithoutTheContributorTheScenarioPickerIsEmpty()
    {
        var catalog = new AssetCatalog();

        Assert.Empty(catalog.All.Where(a => a.Kind == AssetKind.Scenario));
        Assert.Empty(new AssetPickerSource(catalog, AssetKindFilter.Scenario).BuildEntries("", null));
    }

    /// <summary>
    /// ⭐⭐ <b>Nested scenario paths survive to the picker</b> — `Combat/Ambush`, not just `alpha`.
    /// ⚠ Worth pinning: the contributor uses the relative path verbatim as the asset NAME, and the
    /// scenario session then loads by that name, so a flattened name would load the wrong scenario.
    /// </summary>
    [Fact]
    public void NestedScenarioPathsReachThePickerVerbatim()
    {
        var catalog = new AssetCatalog();
        catalog.AddContributor(new ScenarioCatalogContributor(
            () => ScenarioEnumeration.EnumerateRelPaths(_root)));

        var names = catalog.All.Where(a => a.Kind == AssetKind.Scenario)
                               .Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Combat/Ambush", "alpha" }, names);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-064</c> — every catalogued scenario carries a REAL <c>SourceFilePath</c>.</b>
    ///
    /// <para>🔴🔴 <b>The rail this file should have had, and the reason it did not exist is worth stating.</b>
    /// The T3 conformance rail <c>The_cluster_can_discover_open_and_switch_graph_tabs</c> already asserts
    /// this for EVERY catalogued asset — and it was GREEN for months while
    /// <c>ScenarioEditableAsset.SourceFilePath</c> was hard-coded to <c>""</c>. ⇒ ⭐⭐ because on
    /// <c>--mode all</c> the catalog held <b>zero scenarios</b>: first the contributor was editor-only
    /// (<c>CE-053</c>), then it was aimed at an empty root (<c>CE-057</c>). ⛔ <b>A loop over an empty
    /// collection asserts nothing</b> — so fixing the emptiness is what finally reddened it.</para>
    ///
    /// <para>⚠ That is the same weaker/stronger trap as <c>CE-049</c>'s and <c>CE-053</c>'s rails, in a
    /// third disguise: not a supplied input this time, but an <b>unreachable assertion</b>. ⭐ Kept here
    /// at T0 so it fails in milliseconds instead of after an eleven-minute cluster boot.</para>
    /// </summary>
    [Fact]
    public void EveryCataloguedScenarioCarriesItsFilePath()
    {
        var catalog = new AssetCatalog();
        catalog.AddContributor(new ScenarioCatalogContributor(
            () => ScenarioEnumeration.EnumerateRelPaths(_root),
            scenariosRoot: () => _root));

        var scenarios = catalog.All.Where(a => a.Kind == AssetKind.Scenario).ToList();
        Assert.NotEmpty(scenarios);                       // ⛔ else the loop below asserts nothing

        foreach (var a in scenarios)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.SourceFilePath),
                $"scenario '{a.Name}' has no SourceFilePath — open_asset_by_path cannot reach it.");
            Assert.True(File.Exists(a.SourceFilePath),
                $"'{a.Name}' advertises {a.SourceFilePath}, which does not exist — the address is a lie.");
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Both production hosts PASS the root.</b> ⚠ The parameter is optional so the many
    /// single-argument test constructions keep compiling — ⛔ which makes a host that omits it a SILENT
    /// DEFAULT rather than a compile error. This rail is the control for that.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF",    "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostThatComposesTheContributorPassesTheRoot(string project, string file)
    {
        var text = ReadHostSource(project, file);
        if (!text.Contains("ScenarioCatalogContributor(", StringComparison.Ordinal)) return;

        Assert.Contains("scenariosRoot:", text);
    }

    // ══ ② THE COMPOSITION GUARDS — source scans, per host ═══════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Every host that composes a scenario PICKER must also compose a scenario CONTRIBUTOR.</b>
    /// 📐 `CE-049` wired CGF's picker and left its catalog empty; the two live ~700 lines apart in one
    /// file, so nothing connected them. ⇒ this rail ties them together for any future host.
    /// ⚠ A source scan is necessary: both are composition, invisible to reflection and to the call graph.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AHostThatComposesAPickerAlsoComposesAScenarioContributor(string project, string file)
    {
        var text = ReadHostSource(project, file);

        bool composesPicker = text.Contains("AssetPickerLauncher(", StringComparison.Ordinal);
        if (!composesPicker) return;   // ⭐ a host with no picker owes nothing

        Assert.Contains("ScenarioCatalogContributor(", text);
    }

    /// <summary>
    /// ⭐⭐ <b>Symptom 3: a windowed host with a main toolbar composes the perspective radio group.</b>
    /// 📐 `PerspectiveToolbarSection` was constructed in exactly ONE place repo-wide before `CE-054`, so
    /// CGF offered no way to switch between the perspectives it registers.
    /// </summary>
    [Theory]
    [InlineData("Hrot.CGF", "CgfSubsystem.cs")]
    [InlineData("Hrot.Editor", "EditorSubsystem.cs")]
    public void AWindowedHostComposesThePerspectiveToolbarSection(string project, string file)
    {
        var text = ReadHostSource(project, file);

        Assert.Contains("PerspectiveToolbarSection(", text);
    }

    // ── helper ───────────────────────────────────────────────────────────────

    private static string ReadHostSource(string project, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "Hrot", "Subsystems", project, file);
        Assert.True(File.Exists(path), $"expected {path} to exist — the rail's target moved.");
        return File.ReadAllText(path);
    }
}
