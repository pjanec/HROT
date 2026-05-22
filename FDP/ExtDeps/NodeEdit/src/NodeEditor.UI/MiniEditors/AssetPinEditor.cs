using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for asset-reference pins. Shows the asset name in a button;
/// clicking opens the host asset picker. Hosts should register a type-specific
/// override with picker-registry access for full UX.
/// </summary>
public sealed class AssetPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        string label = value?.ToString() ?? "(none)";

        ImGui.PushItemWidth(ctx.MaxWidth);
        ImGui.Button(TruncateLabel(label, ctx.MaxWidth), new System.Numerics.Vector2(ctx.MaxWidth, 0));
        ImGui.PopItemWidth();

        return false;
    }

    private static string TruncateLabel(string label, float maxWidth)
    {
        int maxChars = Math.Max(4, (int)(maxWidth / 7f));
        return label.Length > maxChars ? label[..(maxChars - 1)] + "…" : label;
    }
}
