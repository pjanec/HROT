namespace Hrot.Editor.AiShared;

/// <summary>
/// Maps <see cref="AssetKind"/> values to their corresponding
/// <see cref="IIconProvider"/> key strings (§5.2 AssetKind → IconKey).
/// <para>
/// The keys are resolved through <see cref="IIconProvider.TryGet"/> at render time,
/// keeping atlas layout concerns in one documented place.
/// </para>
/// </summary>
public static class AssetKindIcons
{
    /// <summary>
    /// The icon key for scenarios. Available as a constant for consumers
    /// that need the key without an <see cref="AssetKind.Scenario"/> value.
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
        AssetKind.Scenario   => ScenarioIconKey,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, $"Unknown {nameof(AssetKind)} value: {kind}")
    };
}
