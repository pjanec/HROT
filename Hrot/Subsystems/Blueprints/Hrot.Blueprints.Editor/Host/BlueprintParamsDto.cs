using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// ⭐ <c>CE-1061</c> U5a/U5b (R-3001; <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10c E3/E6) — a blueprint's params DTO and its
/// default overrides (<c>ParamsDefaultJson</c>), written TOGETHER: the outline's chooser, a default edit and every undo are this one call.
/// ⭐ <c>CE-1062</c>: <see cref="Run"/> puts it on the document's undo stack.
/// </summary>
public static class BlueprintParamsDto
{
    /// <summary>Sets both fields; false (nothing changes) when they already hold these values.</summary>
    public static bool Set(BlueprintAsset asset, string? typeId, string? defaultsJson)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string? t = string.IsNullOrWhiteSpace(typeId) ? null : typeId;
        string? d = string.IsNullOrWhiteSpace(defaultsJson) ? null : defaultsJson;
        if (string.Equals(asset.ParamsDtoTypeId, t, StringComparison.Ordinal)
            && string.Equals(asset.ParamsDefaultJson, d, StringComparison.Ordinal)) return false;
        asset.ParamsDtoTypeId   = t;
        asset.ParamsDefaultJson = d;
        return true;
    }

    /// <summary>
    /// Writes the pair as ONE undo entry on <paramref name="history"/> (the document's <c>GraphView.Undo</c>; the sink marks the asset
    /// dirty and re-projects the pins); with no stack it applies directly and calls <paramref name="markDirty"/>. The undo restores the
    /// asset's current pair.
    /// </summary>
    public static void Run(BlueprintAsset asset, string? typeId, string? defaultsJson, string label,
        NodeEditor.Core.Commands.UndoStack? history, Action? markDirty)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string? oldType = asset.ParamsDtoTypeId, oldDefaults = asset.ParamsDefaultJson;
        if (!Hrot.Editor.AiShared.Documents.AssetEdits.Run(history, label,
                () => Set(asset, typeId, defaultsJson), () => Set(asset, oldType, oldDefaults)))
            markDirty?.Invoke();
    }
}
