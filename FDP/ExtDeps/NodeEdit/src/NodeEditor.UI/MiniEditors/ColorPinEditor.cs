using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for color (RGBA <see cref="Vector4"/>) pins. Shows a small
/// color swatch that opens <c>ImGui.ColorPicker4</c> in a popup on click.
/// </summary>
public sealed class ColorPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        var color = value is Vector4 v4 ? v4 : Vector4.One;

        // Small swatch button.
        var swatchSize = new Vector2(ctx.MaxWidth > 0 ? ctx.MaxWidth : 24f, 16f);
        bool clicked = ImGui.ColorButton("##color", color,
            ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.NoTooltip, swatchSize);

        if (clicked)
            ImGui.OpenPopup("##color_popup");

        bool changed = false;
        if (ImGui.BeginPopup("##color_popup"))
        {
            if (ImGui.ColorPicker4("##picker", ref color,
                ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.DisplayRGB))
            {
                // Clamp to [0,1]; color-space conversion is host responsibility.
                color = Vector4.Clamp(color, Vector4.Zero, Vector4.One);
                value = color;
                changed = true;
            }

            // Commit when popup closes.
            if (!ImGui.IsWindowAppearing() && !ImGui.IsAnyItemActive())
                committed = changed;

            ImGui.EndPopup();
        }

        return changed;
    }
}
