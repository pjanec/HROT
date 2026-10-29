using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐ <c>CE-1442</c> (U1, <c>DESIGN_Cluster_Load_Phase.md</c> §11.7 ⑤) — the ONE disabled row a picker shows while the cluster's list has not arrived
/// (<c>RunGate.HasInventory</c> false), so an empty list never reads as "there are none". Used by the recording picker and the asset picker's scenarios.
/// </summary>
public static class InventoryWaiting
{
    /// <summary>The waiting row, with <paramref name="id"/> so a picker can tell it from a real entry.</summary>
    public static PickerEntry Entry(string id)
        => new(Id: id, Name: Hrot.UI.Common.RunMode.RunGate.WaitingForInventoryText, Description: null, Category: null, Keywords: null, IconTextureId: null, Tag: null, IsEnabled: false);
}
