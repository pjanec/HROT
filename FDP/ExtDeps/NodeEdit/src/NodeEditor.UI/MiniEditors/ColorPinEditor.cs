using System.Collections.Generic;
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
    private sealed class ColorState
    {
        public Vector4 OriginalColor;
        public Vector4 CurrentColor;
    }

    // Maintain stable state per-widget while the popup is active
    private static readonly Dictionary<uint, ColorState> s_states = new();

    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        uint id = ImGui.GetID("##color");

        var color = value is Vector4 v4 ? v4 : Vector4.One;

        var swatchSize = new Vector2(ctx.MaxWidth > 0 ? ctx.MaxWidth : 24f, 16f);
        bool clicked = ImGui.ColorButton("##color", color,
            ImGuiColorEditFlags.AlphaPreview | ImGuiColorEditFlags.NoTooltip, swatchSize);

        if (clicked)
        {
            ImGui.OpenPopup("##color_popup");
            s_states[id] = new ColorState { OriginalColor = color, CurrentColor = color };
        }

        bool changed = false;

        if (ImGui.BeginPopup("##color_popup"))
        {
            if (s_states.TryGetValue(id, out var state))
                color = state.CurrentColor;

            if (ImGui.ColorPicker4("##picker", ref color,
                ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.DisplayRGB))
            {
                color = Vector4.Clamp(color, Vector4.Zero, Vector4.One);
                if (state != null) state.CurrentColor = color;
            }

            // Allow clean cancellation via Escape
            if (state != null && ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                state.CurrentColor = state.OriginalColor;
                ImGui.CloseCurrentPopup();
            }

            value = color;
            changed = true; // Force NodeRenderer to retain the override while popup is open

            ImGui.EndPopup();
        }
        else if (s_states.TryGetValue(id, out var state))
        {
            // The popup has just closed this frame. Emit the final commit.
            value = state.CurrentColor;
            changed = true;
            committed = true;
            s_states.Remove(id);
        }

        return changed;
    }
}
