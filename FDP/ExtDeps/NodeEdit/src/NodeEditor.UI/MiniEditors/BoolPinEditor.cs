using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>bool</c> pins. Renders a checkbox;
/// commits immediately on every click.
/// </summary>
public sealed class BoolPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        bool current = value is bool b && b;

        if (ImGui.Checkbox("##bool", ref current))
        {
            value = current;
            committed = true;
            return true;
        }

        return false;
    }
}
