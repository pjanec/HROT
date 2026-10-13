using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// ⭐ <c>CE-1061</c> (U5a, R-3001; <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10b D5) — a blueprint names its params DTO: the
/// write behind the outline's chooser. ⚠ A blueprint has no <c>ParamsDefaultJson</c> yet (CE-3194 ③), so there are no overrides to clear.
/// </summary>
public static class BlueprintParamsDto
{
    /// <summary>Sets <paramref name="asset"/>'s <c>ParamsDtoTypeId</c>; false (nothing changes) when it already names it or the id is blank.</summary>
    public static bool Set(BlueprintAsset asset, string typeId, Action? markDirty)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (string.IsNullOrWhiteSpace(typeId) || string.Equals(asset.ParamsDtoTypeId, typeId, StringComparison.Ordinal)) return false;
        asset.ParamsDtoTypeId = typeId;
        markDirty?.Invoke();
        return true;
    }
}
