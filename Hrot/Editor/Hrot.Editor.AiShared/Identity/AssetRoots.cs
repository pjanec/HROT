namespace Hrot.Editor.AiShared;

/// <summary>
/// Single authority for the two root families described in DESIGN.md §16.
/// </summary>
/// <remarks>
/// <para>
/// <b>Assets/</b> — final assets (browse/save destination): Blueprints, HSMs, BTrees.<br/>
/// <b>Recipes/</b> — creation sources: Blueprints, HSMs, BTrees, Scenarios.
/// </para>
/// <para>
/// <b>Root resolution:</b> resolved via <see cref="AppContext.BaseDirectory"/> because
/// <c>Hrot.Editor.AiShared</c> does not reference <c>Hrot.AI.Behaviors</c>. Both
/// assemblies deploy to the same output directory at runtime, so the result is identical
/// to resolving from the Behaviors assembly location (DEV-LEAD decision per §13).
/// </para>
/// <para>
/// Scenario has <b>no</b> Assets root — Scenarios are orchestrator/NAS-backed and their
/// only root is <see cref="ScenariosRecipesRoot"/>. <see cref="AssetKind.Scenario"/> does
/// not exist yet (it is added later in MTB-P5-T2), so in this batch the Scenario recipe
/// root is exposed as a dedicated member. Kinds with no defined root (§16: Blackboard,
/// Utility) throw <see cref="ArgumentOutOfRangeException"/>.
/// </para>
/// </remarks>
public static class AssetRoots
{
    // ══ CONFIGURED ROOT — ruling 67 ═════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Ruling 67's configured authoring root — the one true authoring blocker on a deployed
    /// node.</b> <see langword="null"/> until <see cref="Configure"/> is called.
    ///
    /// <para>🔒 <b>User, <c>2026-08-14</c>:</b> *"we need a <b>config file provided asset path</b> for the
    /// CGF as well as the Editor (<b>same shared code</b>), with <b>fallback to the repo source</b> as of
    /// now."* ⇒ the resolution order is <b>config → source walk-up → <c>BaseDirectory</c></b>, and it
    /// lives HERE because this class is the codebase's stated *"single authority"* for roots — ⛔ a
    /// config mechanism added beside it would be the third competing path authority, which is what
    /// ruling 67 explicitly warns against.</para>
    /// </summary>
    public static string? ConfiguredRoot { get; private set; }

    /// <summary>
    /// ⭐⭐ <b>Points the roots at a configured directory. Call once, at the composition root.</b>
    ///
    /// <para>🔒 <b>A configured-but-missing root THROWS, and that is the ruling</b> — *"silently falling
    /// through to the walk-up would reintroduce 'it worked on the dev box'."* ⛔ So a typo in config is a
    /// startup failure, not an empty asset list three screens later.</para>
    ///
    /// <para>⭐ Passing <see langword="null"/> or whitespace CLEARS the configuration and restores the
    /// pre-ruling-67 behaviour exactly — which is what a host with no config setting must get, and what
    /// keeps every existing call site unchanged.</para>
    ///
    /// <para>⚠ <b>A <c>static</c> setter on a <c>static</c> class is deliberate and was the ruling's own
    /// call</b> — *"`AssetRoots` is a static class ⇒ lean: an explicit `Configure(...)` at composition
    /// keeps all 30 call sites compiling; a provider is cleaner but ripples."* ⛔ The cost is that it is
    /// process-global; ⭐ tests must restore it, which <see cref="ConfiguredRoot"/> makes possible.</para>
    /// </summary>
    /// <param name="root">Absolute path to the authoring root, or <see langword="null"/> to clear.</param>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when a non-empty <paramref name="root"/> does not exist. ⭐ Fail fast at startup.
    /// </exception>
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
                "to the dev-only source walk-up.");

        ConfiguredRoot = full;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The full ruling-67 resolution: config → source walk-up → <c>BaseDirectory</c>.</b>
    /// This is what a host should call instead of doing the walk-up and its own fallback.
    ///
    /// <para>⭐ Never <see langword="null"/>: the last arm is the output directory, which always exists.
    /// ⚠ That is a deliberate difference from <see cref="ResolveProjectDir"/>, whose <c>null</c> means
    /// *"there is no source tree"* and must stay honest.</para>
    /// </summary>
    /// <param name="kind">The asset kind whose <c>Assets/</c> subfolder is wanted.</param>
    /// <param name="csprojSegments">
    /// Project-file segments for the dev-time walk-up — see <see cref="ResolveProjectDir"/>.
    /// Pass none to skip the walk-up arm entirely.
    /// </param>
    public static string ResolveAssetsRoot(AssetKind kind, params string[] csprojSegments)
        => Path.Combine(ResolveBase(csprojSegments), AssetsRelative(kind));

    /// <inheritdoc cref="ResolveAssetsRoot"/>
    public static string ResolveRecipesRoot(AssetKind kind, params string[] csprojSegments)
        => Path.Combine(ResolveBase(csprojSegments), RecipesRelative(kind));

    /// <summary>
    /// ⭐ The three-arm base resolution, in one place so <see cref="ResolveAssetsRoot"/> and
    /// <see cref="ResolveRecipesRoot"/> cannot disagree about the order.
    /// </summary>
    /// <returns>
    /// Which arm answered, for a caller that wants to LOG it — ⚠ and one should: *"the catalog is
    /// empty"* and *"the catalog is pointed somewhere else"* are different problems.
    /// </returns>
    public static string ResolveBase(params string[] csprojSegments)
    {
        // ① config — ruling 67's answer for a deployed node.
        if (ConfiguredRoot != null) return ConfiguredRoot;

        // ② the source walk-up — "fallback to the repo source as of now" (the user's own words).
        var projectDir = ResolveProjectDir(csprojSegments);
        if (projectDir != null) return projectDir;

        // ③ the output directory — always exists, and is what every pre-ruling-67 property used.
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// ⭐ Which arm <see cref="ResolveBase"/> would answer from — for the log line that tells an
    /// operator WHY the catalog looks the way it does. ⛔ Not a decision input; purely diagnostic.
    /// </summary>
    public static string DescribeBase(params string[] csprojSegments)
    {
        if (ConfiguredRoot != null)               return $"config ({ConfiguredRoot})";
        var dir = ResolveProjectDir(csprojSegments);
        if (dir != null)                          return $"source walk-up ({dir})";
        return $"output directory ({AppContext.BaseDirectory}) — no config and no source tree";
    }

    // ── Relative segment helpers (single authority for §16 segment names) ──

    /// <summary>
    /// Returns the relative path segment for the <c>Assets/<paramref name="kind"/></c>
    /// subfolder (e.g. <c>"Assets/Blueprints"</c>). Suitable for combining with a
    /// project directory resolved from the <c>.csproj</c> or any other base path.
    /// </summary>
    /// <param name="kind">The asset kind.</param>
    /// <returns>
    /// <c>"Assets/Blueprints"</c> for <see cref="AssetKind.Blueprint"/>,
    /// <c>"Assets/HSMs"</c> for <see cref="AssetKind.Hsm"/>,
    /// <c>"Assets/BTrees"</c> for <see cref="AssetKind.BTree"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for <see cref="AssetKind.Blackboard"/> and <see cref="AssetKind.Utility"/>
    /// (no Assets root defined in §16), and for any future kind without an Assets root.
    /// </exception>
    public static string AssetsRelative(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint => Path.Combine("Assets", "Blueprints"),
        AssetKind.BTree     => Path.Combine("Assets", "BTrees"),
        AssetKind.Hsm       => Path.Combine("Assets", "HSMs"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, $"AssetKind.{kind} has no Assets root.")
    };

    /// <summary>
    /// Returns the relative path segment for the <c>Recipes/<paramref name="kind"/></c>
    /// subfolder (e.g. <c>"Recipes/Blueprints"</c>). Suitable for combining with a
    /// project directory resolved from the <c>.csproj</c> or any other base path.
    /// </summary>
    /// <param name="kind">The asset kind.</param>
    /// <returns>
    /// <c>"Recipes/Blueprints"</c> for <see cref="AssetKind.Blueprint"/>,
    /// <c>"Recipes/HSMs"</c> for <see cref="AssetKind.Hsm"/>,
    /// <c>"Recipes/BTrees"</c> for <see cref="AssetKind.BTree"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for <see cref="AssetKind.Blackboard"/> and <see cref="AssetKind.Utility"/>
    /// (no Recipes root defined in §16).
    /// </exception>
    public static string RecipesRelative(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint => Path.Combine("Recipes", "Blueprints"),
        AssetKind.BTree     => Path.Combine("Recipes", "BTrees"),
        AssetKind.Hsm       => Path.Combine("Recipes", "HSMs"),
        AssetKind.Scenario  => ScenariosRecipesRelative,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, $"AssetKind.{kind} has no Recipes root.")
    };

    /// <summary>
    /// Relative path segment for the <c>Recipes/Scenarios</c> directory.
    /// </summary>
    /// <remarks>
    /// Scenario has <b>no</b> Assets root — it is orchestrator/NAS-backed. This is the
    /// only Scenario root until <c>AssetKind.Scenario</c> is added in MTB-P5-T2, at which
    /// point <see cref="RecipesRelative"/> will gain a Scenario arm and this member may be
    /// deprecated in favor of <c>RecipesRelative(AssetKind.Scenario)</c>.
    /// </remarks>
    public static string ScenariosRecipesRelative => Path.Combine("Recipes", "Scenarios");

    // ── Absolute-path properties (output-dir consumers) ──

    /// <summary>
    /// ⭐⭐⭐ <b>The base every absolute-path member below hangs off — <see cref="ConfiguredRoot"/> when
    /// ruling 67's config is set, otherwise <see cref="AppContext.BaseDirectory"/> exactly as before.</b>
    ///
    /// <para>⛔⛔ <b>This is the half of ruling 67 that is easy to miss, and shipping without it would
    /// have been a SPLIT BRAIN.</b> 📐 <see cref="AssetsFor"/> is what
    /// <c>BlueprintAssetContributor.BaseFolder</c>, <c>BTreeJsonAssetContributor</c>,
    /// <c>HsmJsonAssetContributor</c>, <c>BTreeNewAssetService</c> and <c>HsmNewAssetService</c> all
    /// resolve from — i.e. where assets are BROWSED and CREATED. ⇒ had these stayed on
    /// <c>BaseDirectory</c> while the catalog resolved from config, a configured node would have LISTED
    /// assets from one tree and CREATED them in another: two competing path authorities, which is the
    /// precise failure ruling 67 exists to prevent.</para>
    ///
    /// <para>⭐ Unset config ⇒ byte-identical to the previous behaviour, so all ~30 call sites and every
    /// dev box are unchanged. ⚠ That is why the ruling chose <c>Configure(...)</c> over a provider.</para>
    /// </summary>
    private static string AbsoluteBase => ConfiguredRoot ?? AppContext.BaseDirectory;

    /// <summary>
    /// Absolute path to the <c>Assets/</c> root directory (final assets).
    /// ⭐ Honours ruling 67's <see cref="ConfiguredRoot"/> — see <see cref="AbsoluteBase"/>.
    /// </summary>
    public static string AssetsRoot => Path.Combine(AbsoluteBase, "Assets");

    /// <summary>
    /// Absolute path to the <c>Recipes/</c> root directory (creation sources).
    /// ⭐ Honours ruling 67's <see cref="ConfiguredRoot"/> — see <see cref="AbsoluteBase"/>.
    /// </summary>
    public static string RecipesRoot => Path.Combine(AbsoluteBase, "Recipes");

    /// <summary>
    /// Returns the absolute path to the <c>Assets/<paramref name="kind"/></c> subfolder
    /// for file-based kinds (Blueprint, BTree, Hsm).
    /// </summary>
    /// <inheritdoc cref="AssetsRelative"/>
    public static string AssetsFor(AssetKind kind) =>
        Path.Combine(AbsoluteBase, AssetsRelative(kind));

    /// <summary>
    /// Returns the absolute path to the <c>Recipes/<paramref name="kind"/></c> subfolder
    /// for file-based kinds (Blueprint, BTree, Hsm).
    /// </summary>
    /// <inheritdoc cref="RecipesRelative"/>
    public static string RecipesFor(AssetKind kind) =>
        Path.Combine(AbsoluteBase, RecipesRelative(kind));

    /// <summary>
    /// Absolute path to the <c>Recipes/Scenarios</c> directory (Scenario seed root).
    /// </summary>
    /// <inheritdoc cref="ScenariosRecipesRelative"/>
    public static string ScenariosRecipesRoot =>
        Path.Combine(AbsoluteBase, ScenariosRecipesRelative);

    /// <summary>
    /// ⭐⭐⭐ <b>Resolve the SOURCE-TREE project directory that holds the authoring assets, by walking up
    /// for its <c>.csproj</c>.</b>
    ///
    /// <para>⚠⚠ <b>Why a walk-up at all.</b> A host's <c>BaseDirectory</c> is its <c>bin</c> folder, not the
    /// source tree, and the editor-owned <c>*.btree.json</c> / <c>*.hsm.json</c> / <c>*.bp.json</c> assets live
    /// in the SOURCE tree. ⛔ A hard-coded <c>"../../../"</c> breaks the moment a host runs from a different
    /// bin depth — 📌 measured, and it is why the editor grew this walk in the first place.</para>
    ///
    /// <para>⭐⭐ <b>Lifted here because a SECOND host now needs it</b> *(<c>CgfSubsystem</c>, cgf==editor
    /// slice 2)*. ⛔ <c>AssetRoots</c> is this codebase's stated <i>"single authority"</i> for roots, so a
    /// private copy in each composition root is the duplicate ruling 9 forbids.
    /// ⚠⚠ <b>Two inline copies remain in <c>EditorSubsystem</c></b> *(the catalog block and the
    /// QuickReload block)* — ⭐ they should ROUTE here, but that file belongs to another lane, so the
    /// re-route is FILED rather than done *(<c>CE-018</c>)</para>
    ///
    /// <para>🔴 <b>This is ruling 67's blocker in one place.</b> On a DEPLOYED node there is no source tree,
    /// so this answers <see langword="null"/> — ⭐ which is the honest answer, and the caller must SAY so
    /// rather than silently indexing nothing. ⛔ The fix is config-into-roots, not a deeper walk.</para>
    /// </summary>
    /// <param name="csprojSegments">
    /// ⭐ Path segments of the project file, relative to a repo root — e.g.
    /// <c>["Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj"]</c>.
    /// </param>
    /// <returns>The directory containing that <c>.csproj</c>, or <see langword="null"/> when it is not found.</returns>
    public static string? ResolveProjectDir(params string[] csprojSegments)
    {
        if (csprojSegments is null || csprojSegments.Length == 0) return null;

        var relative = Path.Combine(csprojSegments);

        // ⭐ BOTH starting points, in this order — 📐 the editor measured that neither alone is enough:
        //   the working directory differs between `dotnet run`, a test harness and a launched binary.
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
