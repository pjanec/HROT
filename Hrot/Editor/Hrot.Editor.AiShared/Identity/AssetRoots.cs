using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared;

/// <summary>
/// The editor-side authority for the behaviour-asset roots — ⭐ <c>CE-4089</c>: it composes every path through
/// <see cref="ContentLayout"/> (docs/DESIGN_Content_Layout.md §2, L5), which owns the segment names of the content tree.
/// </summary>
/// <remarks>
/// <para>
/// <b>assets/behaviors/{Blueprints,HSMs,BTrees,Params}</b> — final assets (browse/save destination).<br/>
/// <b>recipes/behaviors/{Blueprints,HSMs,BTrees}</b> and <b>recipes/scenarios</b> — creation sources.
/// </para>
/// <para>
/// <b>Root resolution (L4):</b> <see cref="ConfiguredRoot"/> (<c>--asset-root</c>, ruling 67) → the <b>NAS root</b>
/// (<see cref="ContentLayout.NasRoot"/>). ⛔ No source-project walk-up and no <see cref="AppContext.BaseDirectory"/> arm for content:
/// a dev start SEEDS the NAS from the repository (<c>CuratedScenarios.SeedIntoNas</c>), and every node reads only the NAS.
/// </para>
/// <para>
/// Scenario has <b>no</b> Assets root — its only root here is <see cref="ScenariosRecipesRoot"/> (the seeds a new scenario is created
/// from). Kinds with no defined root (Blackboard, Utility) throw <see cref="ArgumentOutOfRangeException"/>.
/// </para>
/// </remarks>
public static class AssetRoots
{
    // ══ CONFIGURED ROOT — ruling 67 ═════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Ruling 67's configured authoring root.</b> <see langword="null"/> until <see cref="Configure"/> is called.
    ///
    /// <para>🔒 <b>User, <c>2026-08-14</c>:</b> *"we need a <b>config file provided asset path</b> for the CGF as well as the Editor (<b>same
    /// shared code</b>), with <b>fallback to the repo source</b> as of now."* — the order was config → source walk-up → <c>BaseDirectory</c>;
    /// ⭐ <c>CE-4089</c> (R-4097): it is now <b>config → the NAS root</b>, and it lives HERE because this class is the codebase's stated
    /// *"single authority"* for roots — ⛔ a config mechanism added beside it would be the third competing path authority.</para>
    /// </summary>
    public static string? ConfiguredRoot { get; private set; }

    /// <summary>
    /// ⭐⭐ <b>Points the roots at a configured directory. Call once, at the composition root.</b>
    ///
    /// <para>🔒 <b>A configured-but-missing root THROWS, and that is the ruling</b> — a typo in config is a startup failure, not an empty asset
    /// list three screens later.</para>
    ///
    /// <para>⭐ Passing <see langword="null"/> or whitespace CLEARS the configuration — which is what a host with no config setting must get.</para>
    ///
    /// <para>⚠ <b>A <c>static</c> setter on a <c>static</c> class is deliberate and was the ruling's own call</b> — it keeps all 30 call
    /// sites compiling. ⛔ The cost is that it is process-global; ⭐ tests must restore it, which <see cref="ConfiguredRoot"/> makes possible.</para>
    /// </summary>
    /// <param name="root">Absolute path to the authoring root, or <see langword="null"/> to clear.</param>
    /// <exception cref="DirectoryNotFoundException">Thrown when a non-empty <paramref name="root"/> does not exist. ⭐ Fail fast at startup.</exception>
    public static void Configure(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            ConfiguredRoot = null;
            return;
        }

        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException(
                $"AssetRoots.Configure: the configured authoring root '{full}' does not exist. " +
                "Ruling 67: a configured-but-missing root fails at startup rather than falling back " +
                "to another root.");

        ConfiguredRoot = full;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The full resolution: config → the NAS root</b> (<c>CE-4089</c> — was config → source walk-up → <c>BaseDirectory</c>).
    /// This is what a host should call instead of doing its own walk-up and fallback.
    ///
    /// <para>⭐ Never <see langword="null"/>. ⚠ <paramref name="csprojSegments"/> is IGNORED for content: it is kept so the ~30 call sites
    /// (which still pass their project path for <see cref="ResolveProjectDir"/>) compile unchanged.</para>
    /// </summary>
    /// <param name="kind">The asset kind whose <c>assets/behaviors/&lt;Folder&gt;</c> subfolder is wanted.</param>
    /// <param name="csprojSegments">Ignored — see the summary.</param>
    public static string ResolveAssetsRoot(AssetKind kind, params string[] csprojSegments)
        => Path.Combine(ResolveBase(csprojSegments), AssetsRelative(kind));

    /// <inheritdoc cref="ResolveAssetsRoot"/>
    public static string ResolveRecipesRoot(AssetKind kind, params string[] csprojSegments)
        => Path.Combine(ResolveBase(csprojSegments), RecipesRelative(kind));

    /// <summary>
    /// ⭐ The base resolution, in one place so <see cref="ResolveAssetsRoot"/>, <see cref="ResolveRecipesRoot"/> and the absolute-path
    /// members cannot disagree: <see cref="ConfiguredRoot"/>, else <see cref="ContentLayout.NasRoot"/>.
    /// </summary>
    /// <param name="csprojSegments">Ignored (kept for the call sites).</param>
    public static string ResolveBase(params string[] csprojSegments)
        => ConfiguredRoot ?? ContentLayout.NasRoot;

    /// <summary>
    /// ⭐ Which arm <see cref="ResolveBase"/> answers from — for the log line that tells an operator WHY the catalog looks the way it does.
    /// ⛔ Not a decision input; purely diagnostic.
    /// </summary>
    public static string DescribeBase(params string[] csprojSegments)
        => ConfiguredRoot != null
            ? $"config ({ConfiguredRoot})"
            : $"NAS root ({ContentLayout.NasRoot})";

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-098</c> (<c>J1-a</c>) — the WHOLE root-reporting policy: which arm answered, and a warning when the answering root holds
    /// no <c>assets/</c> folder.</b>
    ///
    /// <para>⭐⭐ <b>Sinks, not a logger.</b> This assembly has no logging dependency and the two hosts route differently (<c>Console</c> on the
    /// editor, <c>FdpLog&lt;CgfSubsystem&gt;</c> on CGF) ⇒ ⭐ the message BODY is shared so one fault has one wording; the PREFIX and the routing
    /// stay the host's.</para>
    ///
    /// <para>⚠ <c>CE-4089</c>: the warning's predicate is no longer "no source tree" — there is always a root now (config, else the NAS). It is
    /// "the root the catalog reads has no <c>assets/</c>", i.e. NOTHING was seeded or published there. ⭐ The decision is the same
    /// <see cref="ResolveBase"/> the resolution itself uses, never the diagnostic's wording.</para>
    /// </summary>
    /// <param name="info">⭐ Receives <c>"Authoring root resolved from &lt;arm&gt;."</c> — ⚠ ALWAYS, including the happy path.</param>
    /// <param name="warn">⭐⭐ Receives the message when the resolved root has no <c>assets/</c> folder. ⛔ Not an error: a fresh NAS is legal.</param>
    /// <param name="csprojSegments">Ignored (kept for the call sites).</param>
    public static void ReportBase(Action<string>? info, Action<string>? warn, params string[] csprojSegments)
    {
        info?.Invoke($"Authoring root resolved from {DescribeBase(csprojSegments)}.");

        var root = ResolveBase(csprojSegments);
        if (Directory.Exists(ContentLayout.Assets(root))) return;

        warn?.Invoke(
            $"The authoring root '{root}' has no '{ContentLayout.AssetsDirectoryName}/' folder, so the catalog will be empty. "
          + "⇒ a dev start seeds it from the repository (run the editor or the runner from a checkout); "
          + "a deployed node needs the content published to the NAS, or --asset-root.");
    }

    // ── Relative segment helpers (the names come from ContentLayout — ONE authority) ──

    private static string BehaviorFolder(AssetKind kind, string what) => kind switch
    {
        AssetKind.Blueprint => ContentLayout.BlueprintsFolder,
        AssetKind.BTree     => ContentLayout.BTreesFolder,
        AssetKind.Hsm       => ContentLayout.HsmsFolder,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"AssetKind.{kind} has no {what} root.")
    };

    /// <summary>
    /// The path of the <c>assets/behaviors/&lt;Folder&gt;</c> subfolder of <paramref name="kind"/>, RELATIVE to a content root
    /// (<c>assets/behaviors/Blueprints</c> for <see cref="AssetKind.Blueprint"/>, <c>…/HSMs</c>, <c>…/BTrees</c>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">For <see cref="AssetKind.Blackboard"/>, <see cref="AssetKind.Utility"/> and any kind with no Assets root.</exception>
    public static string AssetsRelative(AssetKind kind)
        => ContentLayout.BehaviorAssets(string.Empty, BehaviorFolder(kind, "Assets"));

    /// <summary>
    /// The path of the <c>recipes/…</c> subfolder of <paramref name="kind"/>, RELATIVE to a content root
    /// (<c>recipes/behaviors/&lt;Folder&gt;</c>; <c>recipes/scenarios</c> for <see cref="AssetKind.Scenario"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">For kinds with no Recipes root.</exception>
    public static string RecipesRelative(AssetKind kind) => kind == AssetKind.Scenario
        ? ScenariosRecipesRelative
        : ContentLayout.BehaviorRecipes(string.Empty, BehaviorFolder(kind, "Recipes"));

    /// <summary>Relative path of the scenario seeds: <c>recipes/scenarios</c>.</summary>
    public static string ScenariosRecipesRelative => ContentLayout.ScenarioRecipes(string.Empty);

    // ── Absolute-path properties ──

    /// <summary>
    /// ⭐⭐⭐ <b>The base every absolute-path member below hangs off</b> — the same <see cref="ResolveBase"/> as the catalog, so LISTING and
    /// CREATING an asset can never use two trees (ruling 67's split-brain warning).
    /// </summary>
    private static string AbsoluteBase => ResolveBase();

    /// <summary>Absolute path to the <c>assets/</c> root (every final asset: behaviours, terrain, buildings, icons, audio, textures).</summary>
    public static string AssetsRoot => ContentLayout.Assets(AbsoluteBase);

    /// <summary>Absolute path to <c>assets/behaviors</c> — the final behaviour assets (Blueprints, HSMs, BTrees, Params).</summary>
    public static string BehaviorAssetsRoot => ContentLayout.BehaviorAssets(AbsoluteBase);

    /// <summary>Absolute path to the <c>recipes/</c> root (creation sources).</summary>
    public static string RecipesRoot => ContentLayout.Recipes(AbsoluteBase);

    /// <summary>The absolute <c>assets/behaviors/&lt;Folder&gt;</c> of <paramref name="kind"/> (Blueprint, BTree, Hsm).</summary>
    /// <inheritdoc cref="AssetsRelative"/>
    public static string AssetsFor(AssetKind kind) =>
        Path.Combine(AbsoluteBase, AssetsRelative(kind));

    /// <summary>The absolute <c>recipes/…</c> subfolder of <paramref name="kind"/>.</summary>
    /// <inheritdoc cref="RecipesRelative"/>
    public static string RecipesFor(AssetKind kind) =>
        Path.Combine(AbsoluteBase, RecipesRelative(kind));

    /// <summary>Absolute path to <c>recipes/scenarios</c> (the scenario seed root).</summary>
    public static string ScenariosRecipesRoot =>
        Path.Combine(AbsoluteBase, ScenariosRecipesRelative);

    /// <summary>
    /// ⚠ <b><c>CE-4089</c>: NOT a content resolver any more</b> — no asset root comes from here (content is read from the NAS, see
    /// <see cref="ResolveBase"/>). It remains for the one non-content need left: finding the <c>.csproj</c> a full rebuild / hot reload builds
    /// (<c>EditorSubsystem</c>, <c>EditorApplication</c>) — ⛔ do not use it to locate assets.
    ///
    /// <para>Walks up from BOTH the working directory and <see cref="AppContext.BaseDirectory"/> (the editor measured that neither alone is
    /// enough: the working directory differs between <c>dotnet run</c>, a test harness and a launched binary).</para>
    /// </summary>
    /// <param name="csprojSegments">
    /// Path segments of the project file, relative to a repo root — e.g.
    /// <c>["Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj"]</c>.
    /// </param>
    /// <returns>The directory containing that <c>.csproj</c>, or <see langword="null"/> when it is not found (a deployed node).</returns>
    public static string? ResolveProjectDir(params string[] csprojSegments)
    {
        if (csprojSegments is null || csprojSegments.Length == 0) return null;

        var relative = Path.Combine(csprojSegments);

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = start;
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) return Path.GetDirectoryName(candidate);
                dir = Path.GetDirectoryName(dir);
            }
        }

        return null;
    }
}
