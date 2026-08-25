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
    /// Absolute path to the <c>Assets/</c> root directory (final assets).
    /// </summary>
    public static string AssetsRoot => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>
    /// Absolute path to the <c>Recipes/</c> root directory (creation sources).
    /// </summary>
    public static string RecipesRoot => Path.Combine(AppContext.BaseDirectory, "Recipes");

    /// <summary>
    /// Returns the absolute path to the <c>Assets/<paramref name="kind"/></c> subfolder
    /// for file-based kinds (Blueprint, BTree, Hsm).
    /// </summary>
    /// <inheritdoc cref="AssetsRelative"/>
    public static string AssetsFor(AssetKind kind) =>
        Path.Combine(AppContext.BaseDirectory, AssetsRelative(kind));

    /// <summary>
    /// Returns the absolute path to the <c>Recipes/<paramref name="kind"/></c> subfolder
    /// for file-based kinds (Blueprint, BTree, Hsm).
    /// </summary>
    /// <inheritdoc cref="RecipesRelative"/>
    public static string RecipesFor(AssetKind kind) =>
        Path.Combine(AppContext.BaseDirectory, RecipesRelative(kind));

    /// <summary>
    /// Absolute path to the <c>Recipes/Scenarios</c> directory (Scenario seed root).
    /// </summary>
    /// <inheritdoc cref="ScenariosRecipesRelative"/>
    public static string ScenariosRecipesRoot =>
        Path.Combine(AppContext.BaseDirectory, ScenariosRecipesRelative);

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
