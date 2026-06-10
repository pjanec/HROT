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
    /// Scenario recipes are exposed via <see cref="ScenariosRecipesRelative"/>. Once
    /// <c>AssetKind.Scenario</c> is added (MTB-P5-T2), this method will gain a Scenario arm.
    /// </exception>
    public static string RecipesRelative(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint => Path.Combine("Recipes", "Blueprints"),
        AssetKind.BTree     => Path.Combine("Recipes", "BTrees"),
        AssetKind.Hsm       => Path.Combine("Recipes", "HSMs"),
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
}
