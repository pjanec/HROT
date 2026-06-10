namespace Hrot.Editor.AiShared;

/// <summary>
/// Maps <see cref="AssetKind"/> values to their corresponding
/// <see cref="IIconProvider"/> key strings (§5.2 AssetKind → IconKey).
/// <para>
/// The keys are resolved through <see cref="IIconProvider.TryGet"/> at render time,
/// keeping atlas layout concerns in one documented place.
/// </para>
/// <para>
/// <b>DEV-LEAD decision (DEC-2):</b> <see cref="AssetKind.Scenario"/> does not exist yet.
/// The scenario mapping is exposed via the <see cref="ScenarioIconKey"/> constant
/// rather than an <see cref="AssetKind.Scenario"/> arm. When <c>AssetKind.Scenario</c>
/// is added (MTB-P5-T2), the map should gain a <c>Scenario</c> arm returning
/// <see cref="ScenarioIconKey"/>.
/// </para>
/// </summary>
public static class AssetKindIcons
{
    /// <summary>
    /// The icon key for scenarios. Exposed as a dedicated constant because
    /// <see cref="AssetKind.Scenario"/> is not yet part of the enum (DEC-2).
    /// </summary>
    public const string ScenarioIconKey = "asset/scenario";

    /// <summary>
    /// Returns the <see cref="IIconProvider"/> key for a given <see cref="AssetKind"/>.
    /// </summary>
    /// <returns>The icon key string, e.g. <c>"asset/blueprint"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for unknown <see cref="AssetKind"/> values.
    /// </exception>
    public static string GetIconKey(AssetKind kind) => kind switch
    {
        AssetKind.Blueprint  => "asset/blueprint",
        AssetKind.BTree      => "asset/btree",
        AssetKind.Hsm        => "asset/hsm",
        AssetKind.Blackboard => "asset/blackboard",
        AssetKind.Utility    => "asset/utility",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"Unknown {nameof(AssetKind)} value: {kind}")
    };
}
