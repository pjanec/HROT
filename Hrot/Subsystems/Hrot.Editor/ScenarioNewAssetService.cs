using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;

namespace Hrot.Editor;

/// <summary>
/// Scenario implementation of <see cref="INewAssetService"/>.
/// Routes scenario creation to <see cref="IScenarioCreationSession"/>
/// (<see cref="IEditorLogic"/> in production). No file I/O — scenarios
/// are saved by the editor backend.
/// </summary>
/// <remarks>
/// Design §18.3 / §19:
/// <b>FromSeed</b> → load the seed scenario then <c>SaveScenarioAs</c> under the new name.
/// Uses <see cref="IScenarioCreationSession"/> as a narrow testable seam.
///
/// <para>⛔⛔ <b><c>H3</c> — this kind offers NO BLANK TEMPLATE, deliberately</b> (design §2.1e ⑤c
/// <c>G3</c>). 📐 The measured reason: <c>Header.TerrainName</c> is stamped at save from the terrain the node has
/// RESIDENT, so a blank scenario has no way to acquire a terrain at all. ⇒ every new scenario starts from a SEED that
/// carries the terrain name, and <c>"Empty"</c> is gone rather than left as a trap. ⛔ <c>R-4018</c>: the TKB is no
/// longer part of this — a scenario never names one.</para>
/// </remarks>
public sealed class ScenarioNewAssetService : INewAssetService
{
    // ⭐⭐⭐ H2 / U9 + L6 (CE-4089) — HOW A SEED IS LOADED. 🔴 U9 (measured): a scenario load is a cluster transition keyed on a NAME that the master resolves
    //   against the NAS (nothing accepts a path), so "root-aware loads" are out and the seed's NAME must say where it lives. ⭐ L6: a recipe is named by
    //   its ROOT, `recipes/scenarios/<seed>` (ContentLayout.ScenarioRecipeName); the master's prefetch resolves it against the NAS recipes root through the ONE
    //   resolver (ContentLayout.ResolveScenarioDirectory). ⛔ The earlier design (H2) staged the seeds under `{scenarios root}/Recipes/` and loaded them as
    //   `Recipes/<seed>` — SUPERSEDED: that folder was inside the list "Open" shows, and the staging was a second copy of what the dev seed already puts in the NAS.

    private readonly IScenarioCreationSession _session;

    /// <summary>
    /// ⭐⭐⭐ <b>A PROVIDER, not a snapshot.</b> <c>H1</c>'s success condition is that
    /// <see cref="AvailableRecipes"/> *"re-reads the directory LIVE — it is called per-open, not
    /// snapshotted"*. ⛔ Holding a materialised list would satisfy the count on the first open and then
    /// go stale the moment a seed is added, which is the silent-wrong-answer shape the whole stage is
    /// about. ⚠ The first draft of this class DID hold a list; the success condition caught it.
    /// </summary>
    private readonly Func<IEnumerable<string>> _seedNames;

    public ScenarioNewAssetService(IScenarioCreationSession session)
    {
        _session   = session ?? throw new ArgumentNullException(nameof(session));
        _seedNames = static () => Array.Empty<string>();
    }

    /// <summary>
    /// ⭐⭐ <b><c>H1</c> — the production constructor</b>, which discovers seed scenarios and wraps them
    /// as recipe entries.
    ///
    /// <para>🔴 <b>It had ZERO production callers.</b> 📐 <c>EditorSubsystem</c> constructed the 1-arg
    /// ctor and <c>AssetRoots.ScenariosRecipesRoot</c> was referenced only by tests ⇒ the Scenario kind
    /// offered exactly one recipe, <c>"Empty"</c>. ⚠ The silent-default shape: the caller HAD the value
    /// (a static property) and did not pass it. 📄 design §2.1e ⑤c <c>G1</c>.</para>
    /// </summary>
    /// <param name="seedScenarioNames">
    /// The seed names, RELATIVE to the recipes root (<c>recipes/scenarios</c>) and WITHOUT the name prefix — this
    /// service owns that convention, so a caller enumerating the recipes directory passes what it finds.
    /// </param>
    public ScenarioNewAssetService(
        IScenarioCreationSession session,
        Func<IEnumerable<string>> seedScenarioNames)
    {
        _session   = session ?? throw new ArgumentNullException(nameof(session));
        _seedNames = seedScenarioNames ?? throw new ArgumentNullException(nameof(seedScenarioNames));
    }

    /// <summary>
    /// ⚠ Convenience overload for a FIXED set — used by rails that assert on a known list. ⛔ Not for
    /// production: it cannot re-read (see <see cref="_seedNames"/>).
    /// </summary>
    public ScenarioNewAssetService(
        IScenarioCreationSession session,
        IEnumerable<string> seedScenarioNames)
        : this(session, SnapshotOf(seedScenarioNames))
    {
    }

    private static Func<IEnumerable<string>> SnapshotOf(IEnumerable<string> names)
    {
        var fixedList = (names ?? Array.Empty<string>()).ToList();
        return () => fixedList;
    }

    /// <inheritdoc />
    public AssetKind Kind => AssetKind.Scenario;

    /// <inheritdoc />
    public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must not be empty.", nameof(name));

        // Build the full scenario name: <relPath>/<name> (or just <name> if relPath is empty).
        var fullName = string.IsNullOrEmpty(relPath) ? name : relPath + "/" + name;

        // ⛔⛔ H3 — REFUSE rather than mint a scenario with no terrain, and name the recipes
        //    that would have worked (the RecipeByName refusal shape: correctable in one step).
        //    ⚠ RecipeByName.Resolve returns (null, null) for a kind with no blank template — which its
        //    own header documents as legitimate — so the refusal has to live HERE, at the only place
        //    that knows a blank scenario is a trap. 📄 design §2.1e ⑤c G3.
        if (recipe == null || IsEmptyRecipe(recipe))
        {
            var available = AvailableRecipes();
            var offered = available.Count == 0
                ? "(none — no seed scenarios are present under recipes/scenarios)"
                : string.Join(", ", available.Select(r => $"'{r.Name}'"));
            throw new InvalidOperationException(
                "[ERROR] Scenario offers no blank template: a scenario minted with no terrain cannot acquire "
              + "one afterwards (the header's TerrainName is stamped from the terrain the node has resident). "
              + "Create from a seed instead. Available: "
              + offered + ". List them with GET /assets/recipes.");
        }

        // FromSeed: load the seed by the name the CLUSTER can resolve — the recipe's own name
        // (recipes/scenarios/<seed>, resolved by the master against the NAS recipes root) — then save under the new name.
        // ⭐ CE-4101 — the load is GUARDED (a run ⇒ Stop & Load, unsaved edits ⇒ Save / Discard / Cancel): the save-as follows the open that WENT OUT, ⛔ never a cancelled one (it would
        //   save whatever the world holds under the new name).
        _session.LoadScenarioByName(SeedNameToScenarioName(recipe.Name), () => _session.SaveScenarioAs(fullName));

        // Scenario assets don't have a file path in the Assets tree — they live
        // in the scenarios root (managed by IEditorLogic/NAS).
        return new ScenarioEditableAssetAdapter(
            Guid.NewGuid(), name, string.Empty, isRecipe: false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⭐⭐ <b>Re-read LIVE, never snapshotted</b> — the seed list is whatever the constructor was handed,
    /// and <c>EditorSubsystem</c> hands it a live enumeration of the recipes directory. ⛔ <c>"Empty"</c>
    /// is deliberately absent (<c>H3</c>).
    /// </remarks>
    public IReadOnlyList<IEditableAsset> AvailableRecipes()
    {
        var recipes = new List<IEditableAsset>();
        foreach (var name in _seedNames())
        {
            recipes.Add(new ScenarioEditableAssetAdapter(
                Guid.Empty, name, string.Empty, isRecipe: true));
        }
        return recipes;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ⛔⛔ <b><c>H3</c> — ALWAYS false.</b> Every Scenario recipe is a real seed from disk whose own
    /// name is a sensible default; there is no synthetic blank entry to recognise. ⚠ This is what makes
    /// <c>RecipeByName.Resolve(service, null)</c> return <c>(null, null)</c>, which
    /// <see cref="CreateNew"/> then refuses with the list of seeds.
    /// </remarks>
    public bool IsBlankTemplate(IEditableAsset recipe) => false;

    /// <summary>
    /// ⭐ The seed's display name → the scenario name the CLUSTER resolves. See
    /// <see cref="Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipeName"/> (L6) for the spelling.
    /// </summary>
    public static string SeedNameToScenarioName(string seedName)
        => Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipeName(seedName);

    /// <summary>
    /// ⭐ <c>CE-4140</c> — the description the New Scenario picker shows for a scenario RECIPE: the <c>header.description</c> of its <c>scenario.json</c> under <paramref name="recipesRoot"/>
    /// (default <see cref="AssetRoots.ScenariosRecipesRoot"/>). Null for a non-scenario asset, a missing / unreadable file or an empty description.
    /// </summary>
    public static string? DescribeRecipe(IEditableAsset recipe, string? recipesRoot = null)
    {
        if (recipe is not ScenarioEditableAssetAdapter) return null;
        try
        {
            var file = System.IO.Path.Combine(recipesRoot ?? AssetRoots.ScenariosRecipesRoot, recipe.Name, "scenario.json");
            if (!System.IO.File.Exists(file)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("header", out var header) && header.TryGetProperty("description", out var d)
                && d.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString()))
                return d.GetString();
        }
        catch (Exception) { /* an unreadable file simply has no description */ }
        return null;
    }

    private static bool IsEmptyRecipe(IEditableAsset recipe)
        => string.Equals(recipe.Name, "Empty", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Thin <see cref="IEditableAsset"/> adapter for scenario assets and recipes.
/// Scenarios have no DTO — they are saved/loaded by the editor backend.
/// </summary>
public sealed class ScenarioEditableAssetAdapter : IEditableAsset
{
    private readonly Guid _assetId;
    private readonly string _name;
    private readonly string _sourceFilePath;
    private readonly bool _isRecipe;

    public ScenarioEditableAssetAdapter(
        Guid assetId, string name, string sourceFilePath, bool isRecipe)
    {
        _assetId = assetId;
        _name = name;
        _sourceFilePath = sourceFilePath;
        _isRecipe = isRecipe;
    }

    public Guid AssetId => _assetId;
    public string Name => _name;
    public AssetKind Kind => AssetKind.Scenario;
    public string SourceFilePath => _sourceFilePath;
    public bool IsDirty => false;
    public bool IsEditorOwned => !_isRecipe; // recipes are not editor-owned documents
    public event Action? Changed { add { } remove { } }
}
