using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>
/// The ONE drag-to-reorder gesture of the kit (chips of a <see cref="ChipRow"/>, the ⋮⋮ handle of a <see cref="ListBlock"/> / <see cref="GridBlock"/> row), on ImGui's own
/// <c>BeginDragDropSource/Target</c>. Drag is a BONUS (K4): the ⋮ menu always moves an item too.
/// </summary>
public static class KitDrag
{
    private const string PayloadType = "KIT_REORDER";
    private static uint _owner;
    private static int _from;

    /// <summary>Call straight after the item that is the source and / or the target. True on the frame an item of the SAME <paramref name="owner"/> was dropped on this one; <paramref name="from"/> is its index.</summary>
    public static unsafe bool Reorder(uint owner, int index, string preview, bool asSource, bool asTarget, out int from)
    {
        // K7 adopter (DESIGN_Struct_Editor §10.3): gestures are not keymap bindings yet
        from = -1;
        if (asSource && ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
        {
            byte tag = 1;
            ImGui.SetDragDropPayload(PayloadType, (IntPtr)(&tag), 1);
            _owner = owner;
            _from = index;
            ImGui.TextUnformatted(preview);
            ImGui.EndDragDropSource();
        }
        bool dropped = false;
        if (asTarget && ImGui.BeginDragDropTarget())
        {
            var p = ImGui.AcceptDragDropPayload(PayloadType);
            if (p.NativePtr != null && _owner == owner && _from != index) { from = _from; dropped = true; }
            ImGui.EndDragDropTarget();
        }
        return dropped;
    }
}
