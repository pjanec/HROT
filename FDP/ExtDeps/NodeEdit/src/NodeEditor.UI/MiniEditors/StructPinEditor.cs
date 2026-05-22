using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for composite struct pins. In composite mode renders a
/// collapsible tree header; in split mode the canvas exposes individual
/// field pins instead and this editor renders nothing.
/// </summary>
public sealed class StructPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;

        // Split mode: nothing to render — canvas handles per-field pins.
        // We detect split mode by the absence of a boxed value with child data.
        // For now, render a simple read-only label; a full implementation
        // would recurse into child field editors.
        string typeName = ctx.Type.IsEmpty ? "Struct" : ctx.Type.Id;
        ImGui.TextDisabled(typeName);

        return false;
    }
}
