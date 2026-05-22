using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.UI.Util;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Draws pin glyphs (circle for data, arrow-triangle for exec) and their labels.
/// Also draws the type-color fill to distinguish connected vs unconnected pins.
/// </summary>
internal sealed class PinRenderer
{
    private const float PinRadiusPx     = 5f;
    private const float PinLabelOffsetX = 8f;

    /// <summary>Draw all visible pin glyphs and labels for a node.</summary>
    public void DrawNodePins(
        GraphView view,
        ImDrawListPtr dl,
        INodeModel node,
        Dictionary<PinId, Vector2> pinPositions,
        HashSet<PinId> connectedInputPins)
    {
        var theme   = view.Host.Theme;
        var zoom    = view.Viewport.Zoom;
        float radius = PinRadiusPx * MathF.Sqrt(zoom); // scale with zoom

        foreach (var pin in node.Pins)
        {
            if (pin.IsAdvanced && !node.ShowAdvancedPins) continue;
            if (!pinPositions.TryGetValue(pin.Id, out var screenPos)) continue;

            bool isInput  = pin.Direction == PinDirection.Input;
            bool isExec   = pin.Kind == PinKind.Exec;
            bool connected = isInput
                ? connectedInputPins.Contains(pin.Id)
                : view.Model.Links.Any(l => l.FromPin == pin.Id);

            bool hovered = view.Interaction.Hover is { Kind: HoverKind.Pin } h && h.Pin == pin.Id;

            var typeColor = isExec
                ? DefaultTypeColors.ExecColor
                : pin.Type.HasValue ? view.TypeSystem.GetPinColor(pin.Type.Value) : new Vector4(0.6f, 0.6f, 0.6f, 1f);

            uint fillColor    = connected
                ? ImGui.GetColorU32(typeColor)
                : ImGui.GetColorU32(typeColor with { W = 0.25f });
            uint outlineColor = hovered
                ? ImGui.GetColorU32(view.Host.Theme.PrimarySelectionAccent)
                : ImGui.GetColorU32(typeColor);

            if (isExec)
                DrawExecGlyph(dl, screenPos, zoom, fillColor, outlineColor, connected);
            else
                dl.AddCircleFilledOutline(screenPos, radius, fillColor, outlineColor, 1.5f);

            // Pin label
            if (!view.Viewport.IsLowZoom)
                DrawPinLabel(dl, pin, screenPos, isInput, theme, zoom);
        }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private static void DrawExecGlyph(
        ImDrawListPtr dl,
        Vector2 center, float zoom,
        uint fill, uint outline, bool connected)
    {
        // Arrow pointing right for outputs, left for inputs (chevron shape).
        float hw = 6f * zoom * 0.5f;
        float hh = 7f * zoom * 0.5f;
        var tip  = center + new Vector2(hw, 0f);
        var bl   = center + new Vector2(-hw,  hh);
        var tl   = center + new Vector2(-hw, -hh);

        if (connected)
            dl.AddTriangleFilled(tl, bl, tip, fill);
        else
        {
            dl.AddTriangle(tl, bl, tip, outline, 1.5f);
        }
    }

    private static void DrawPinLabel(
        ImDrawListPtr dl,
        IPinModel pin,
        Vector2 pinScreen,
        bool isInput,
        IEditorTheme theme,
        float zoom)
    {
        if (string.IsNullOrEmpty(pin.Label)) return;

        uint textColor = ImGui.GetColorU32(pin.IsOptional ? theme.TextMuted : theme.TextDefault);
        float offsetX  = PinLabelOffsetX * zoom;
        Vector2 labelPos = isInput
            ? pinScreen + new Vector2(offsetX, -ImGui.GetFontSize() * 0.5f)
            : pinScreen - new Vector2(offsetX + ImGui.CalcTextSize(pin.Label).X, ImGui.GetFontSize() * 0.5f);

        dl.AddText(labelPos, textColor, pin.Label);
    }
}
