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
    /// <param name="kind">The asset kind.</param>
    /// <returns>
    /// <c>Assets/Blueprints</c> for <see cref="AssetKind.Blueprint"/>,
    /// <c>Assets/HSMs</c> for <see cref="AssetKind.Hsm"/>,
    /// <c>Assets/BTrees</c> for <see cref="AssetKind.BTree"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for <see cref="AssetKind.Blackboard"/> and <see cref="AssetKind.Utility"/>
    /// (no Assets root defined in §16), and for any future kind without an Assets root.
    /// </exception>
    public static string AssetsFor(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint => Path.Combine(AssetsRoot, "Blueprints"),
        AssetKind.BTree     => Path.Combine(AssetsRoot, "BTrees"),
        AssetKind.Hsm       => Path.Combine(AssetsRoot, "HSMs"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, $"AssetKind.{kind} has no Assets root.")
    };

    /// <summary>
    /// Returns the absolute path to the <c>Recipes/<paramref name="kind"/></c> subfolder
    /// for file-based kinds (Blueprint, BTree, Hsm).
    /// </summary>
    /// <param name="kind">The asset kind.</param>
    /// <returns>
    /// <c>Recipes/Blueprints</c> for <see cref="AssetKind.Blueprint"/>,
    /// <c>Recipes/HSMs</c> for <see cref="AssetKind.Hsm"/>,
    /// <c>Recipes/BTrees</c> for <see cref="AssetKind.BTree"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for <see cref="AssetKind.Blackboard"/> and <see cref="AssetKind.Utility"/>
    /// (no Recipes root defined in §16).
    /// Scenario recipes are exposed via <see cref="ScenariosRecipesRoot"/>. Once
    /// <c>AssetKind.Scenario</c> is added (MTB-P5-T2), this method will gain a Scenario arm.
    /// </exception>
    public static string RecipesFor(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint => Path.Combine(RecipesRoot, "Blueprints"),
        AssetKind.BTree     => Path.Combine(RecipesRoot, "BTrees"),
        AssetKind.Hsm       => Path.Combine(RecipesRoot, "HSMs"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, $"AssetKind.{kind} has no Recipes root.")
    };

    /// <summary>
    /// Absolute path to the <c>Recipes/Scenarios</c> directory (Scenario seed root).
    /// </summary>
    /// <remarks>
    /// Scenario has <b>no</b> Assets root — it is orchestrator/NAS-backed. This is the
    /// only Scenario root until <c>AssetKind.Scenario</c> is added in MTB-P5-T2, at which
    /// point <see cref="RecipesFor"/> will gain a Scenario arm and this member may be
    /// deprecated in favor of <c>RecipesFor(AssetKind.Scenario)</c>.
    /// </remarks>
    public static string ScenariosRecipesRoot => Path.Combine(RecipesRoot, "Scenarios");
}
