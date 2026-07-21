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

    // Punch-list #9: finer blueprint icon keys (Action / Condition / Function). Blueprints all share
    // AssetKind.Blueprint, so these are surfaced via IAssetIconKeyProvider rather than GetIconKey.
    /// <summary>Icon key for an AiPrimitive blueprint with Action intent.</summary>
    public const string BlueprintActionIconKey    = "asset/blueprint_action";
    /// <summary>Icon key for an AiPrimitive blueprint with Condition intent.</summary>
    public const string BlueprintConditionIconKey = "asset/blueprint_condition";
    /// <summary>Icon key for a Library-dispatch (function) blueprint.</summary>
    public const string BlueprintFunctionIconKey  = "asset/blueprint_function";

    /// <summary>
    /// Returns the <see cref="IIconProvider"/> key for a given <see cref="AssetKind"/>.
    /// </summary>
    /// <returns>The icon key string, e.g. <c>"asset/blueprint"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for unknown <see cref="AssetKind"/> values.
    /// </exception>
    /// <summary>
    /// Punch-list #9: resolves an asset's row/entry icon key, preferring an
    /// <see cref="IAssetIconKeyProvider"/> override (e.g. a Blueprint's Action/Condition/Function
    /// icon) and falling back to the per-<see cref="AssetKind"/> default. Both the Open-Asset picker
    /// (<c>AssetPickerSource</c>) and the asset-browser panel route through this so they stay consistent.
    /// </summary>
    public static string ResolveIconKey(IEditableAsset asset)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (asset is IAssetIconKeyProvider p && !string.IsNullOrEmpty(p.IconKey))
            return p.IconKey!;
        return GetIconKey(asset.Kind);
    }

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
