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
/// <c>G3</c>). 📐 The measured reason: <c>Header.TkbName</c> is stamped at save from
/// <c>ITkbDatabase.ActiveTkbName</c>, whose only writer reads it back OUT of a staged scenario header —
/// so a blank scenario has no way to acquire a TKB at all and inherits the last cluster load's, or
/// <c>null</c>, at which point <c>ScenarioSerializer</c> omits the whole header. ⚠ With the terrain
/// name (§2.1e ①) the identical hole opens. ⇒ every new scenario starts from a SEED that carries both
/// names, and <c>"Empty"</c> is gone rather than left as a trap.</para>
/// </remarks>
public sealed class ScenarioNewAssetService : INewAssetService
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>H2</c> / <c>U9</c> — the reserved subfolder of the NAS scenarios root that seed
    /// scenarios are staged into, and therefore the prefix a seed is LOADED by.</b>
    ///
    /// <para>🔴 <b>Why a prefix and not a root-aware load — U9 answered by measurement, and BOTH of the
    /// plan's proposed options are wrong.</b> 📐 <c>IScenarioCreationSession.LoadScenarioByName</c> →
    /// <c>EditorApplication.cs:138</c> → <c>EditorScenarioSession.OpenForEdit</c> (<c>:147</c>) takes a
    /// NAME, stashes it and publishes a <c>TransitionStateIntent</c>: the name crosses the cluster as
    /// <c>ScenarioId</c> and <b>every node resolves it against its own NAS scenarios root</b>
    /// (<c>EditorBootstrap.cs:23</c> — <c>{NasBasePath}/scenarios/{name}/scenario.json</c>;
    /// <c>OrchestrationConstants.GetSharedScenariosRoot()</c> for the shared one, which CGF uses too).
    /// ⇒ ⛔ <b>nothing anywhere accepts a path</b>, so "have <c>AvailableRecipes()</c> hand back full
    /// paths" cannot work; and ⛔ a root-aware load would have to carry "which root" across the wire to
    /// every node, where <c>Recipes/Scenarios</c> — a LOCAL authoring/output path
    /// (<c>AssetRoots.cs:294</c>) — does not exist at all.</para>
    ///
    /// <para>⭐⭐ <b>The third option, which changes no seam:</b> <c>OpenForEdit</c>'s own contract already
    /// says <i>"the name may be a relative path (e.g. <c>Combat/Patrol</c>)"</i>. ⇒ stage the seeds into
    /// <c>{scenarios root}/Recipes/</c> and load them by the name <c>Recipes/&lt;seed&gt;</c>. The name
    /// stays a name, the cluster resolves it exactly as always, and the staging reuses
    /// <c>CuratedScenarios.SeedFrom</c> — the shipped mechanism that already copies committed scenarios
    /// into the working root. 📄 design §10.8.</para>
    /// </summary>
    public const string SeedSubfolder = "Recipes";

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
    /// The seed names, RELATIVE to the recipes root and WITHOUT <see cref="SeedSubfolder"/> — this
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

        // ⛔⛔ H3 — REFUSE rather than mint a scenario with no terrain and no TKB, and name the recipes
        //    that would have worked (the RecipeByName refusal shape: correctable in one step).
        //    ⚠ RecipeByName.Resolve returns (null, null) for a kind with no blank template — which its
        //    own header documents as legitimate — so the refusal has to live HERE, at the only place
        //    that knows a blank scenario is a trap. 📄 design §2.1e ⑤c G3.
        if (recipe == null || IsEmptyRecipe(recipe))
        {
            var available = AvailableRecipes();
            var offered = available.Count == 0
                ? "(none — no seed scenarios are deployed under Recipes/Scenarios)"
                : string.Join(", ", available.Select(r => $"'{r.Name}'"));
            throw new InvalidOperationException(
                "[ERROR] Scenario offers no blank template: a scenario minted with no terrain and no TKB "
              + "cannot acquire either afterwards (the header's TkbName is stamped from a value whose only "
              + "writer reads it back out of a staged header). Create from a seed instead. Available: "
              + offered + ". List them with GET /assets/recipes.");
        }

        // FromSeed: load the seed by the name the CLUSTER can resolve — the reserved subfolder of the
        // scenarios root the seeds are staged into (see SeedSubfolder) — then save under the new name.
        _session.LoadScenarioByName(SeedNameToScenarioName(recipe.Name));
        _session.SaveScenarioAs(fullName);

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
    /// <see cref="SeedSubfolder"/> for why this is a name and not a path.
    /// </summary>
    public static string SeedNameToScenarioName(string seedName)
        => SeedSubfolder + "/" + seedName;

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
