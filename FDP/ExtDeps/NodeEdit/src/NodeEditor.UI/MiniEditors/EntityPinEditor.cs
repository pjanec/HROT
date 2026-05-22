using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for entity-reference pins. Renders a picker button that
/// displays the entity label if available. Full picker integration requires the
/// host to register a custom editor with access to the picker registry; this
/// implementation provides the basic button scaffold.
/// </summary>
public sealed class EntityPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        string label = value?.ToString() ?? "(none)";

        ImGui.PushItemWidth(ctx.MaxWidth);
        // Display entity label; click opens picker (host must override for full UX)
        ImGui.Button(TruncateLabel(label, ctx.MaxWidth), new System.Numerics.Vector2(ctx.MaxWidth, 0));
        ImGui.PopItemWidth();

        return false;
    }

    private static string TruncateLabel(string label, float maxWidth)
    {
        // Rough truncation — exact pixel width requires CalcTextSize
        int maxChars = Math.Max(4, (int)(maxWidth / 7f));
        return label.Length > maxChars ? label[..(maxChars - 1)] + "…" : label;
    }
}
