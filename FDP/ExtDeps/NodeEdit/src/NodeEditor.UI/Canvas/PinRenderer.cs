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
        // In low-zoom mode the node renders as a simplified solid block; skip all
        // pin glyph and label submissions to avoid exhausting the ImGui vertex budget.
        if (view.Viewport.IsLowZoom) return;

        var theme   = view.Host.Theme;
        var zoom    = view.Viewport.Zoom;
        bool alt    = (view.Host.Input.Modifiers & KeyModifiers.Alt) != 0;
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

            // Spec §8: hovered pins scale to 1.5× and brighten their outline.
            float currentRadius     = hovered ? radius * 1.2f : radius;
            float strokeThickness   = hovered ? 2f    : 1.5f;

            var typeColor = isExec
                ? DefaultTypeColors.ExecColor
                : pin.Type.HasValue ? view.TypeSystem.GetPinColor(pin.Type.Value) : new Vector4(0.6f, 0.6f, 0.6f, 1f);

            uint fillColor    = connected
                ? ImGui.GetColorU32(typeColor)
                : ImGui.GetColorU32(typeColor with { W = 0.25f });
            uint outlineColor = ImGui.GetColorU32(typeColor);
            if (hovered)
            {
                outlineColor = alt
                    ? ImGui.GetColorU32(new Vector4(1f, 0.8f, 0f, 1f))
                    : ImGui.GetColorU32(view.Host.Theme.PrimarySelectionAccent);
            }

            if (isExec)
            {
                DrawExecGlyph(dl, screenPos, zoom, fillColor, outlineColor, connected, hovered);
            }
            else
            {
                DrawDataGlyph(dl, pin.Shape, screenPos, currentRadius, fillColor, outlineColor, strokeThickness, connected);
            }

            // Pin label
            if (!view.Viewport.IsLowZoom)
                DrawPinLabel(dl, pin, screenPos, isInput, theme, zoom);
        }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private static void DrawExecGlyph(
        ImDrawListPtr dl,
        Vector2 center, float zoom,
        uint fill, uint outline, bool connected, bool hovered)
    {
        // Spec §8: scale to 1.5× when hovered.
        float scale = hovered ? 1.5f : 1.0f;
        float hw = 6f * zoom * 0.5f * scale;
        float hh = 7f * zoom * 0.5f * scale;
        var tip  = center + new Vector2(hw, 0f);
        var bl   = center + new Vector2(-hw,  hh);
        var tl   = center + new Vector2(-hw, -hh);

        if (connected)
            dl.AddTriangleFilled(tl, bl, tip, fill);
        else
            dl.AddTriangle(tl, bl, tip, outline, hovered ? 2f : 1.5f);
    }

    private static void DrawDataGlyph(
        ImDrawListPtr dl,
        PinShape shape,
        Vector2 center,
        float radius,
        uint fill,
        uint outline,
        float strokeThickness,
        bool connected)
    {
        switch (shape)
        {
            case PinShape.Diamond:
            {
                var p1 = center + new Vector2(0f, -radius);
                var p2 = center + new Vector2(radius, 0f);
                var p3 = center + new Vector2(0f, radius);
                var p4 = center + new Vector2(-radius, 0f);
                if (connected) dl.AddQuadFilled(p1, p2, p3, p4, fill);
                dl.AddQuad(p1, p2, p3, p4, outline, strokeThickness);
                break;
            }
            case PinShape.Square:
            {
                var pMin = center - new Vector2(radius, radius);
                var pMax = center + new Vector2(radius, radius);
                if (connected) dl.AddRectFilled(pMin, pMax, fill);
                dl.AddRect(pMin, pMax, outline, 0f, ImDrawFlags.None, strokeThickness);
                break;
            }
            case PinShape.Pentagon:
                if (connected) dl.AddNgonFilled(center, radius, fill, 5);
                dl.AddNgon(center, radius, outline, 5, strokeThickness);
                break;
            case PinShape.Triangle:
                if (connected) dl.AddNgonFilled(center, radius, fill, 3);
                dl.AddNgon(center, radius, outline, 3, strokeThickness);
                break;
            case PinShape.Circle:
            default:
                dl.AddCircleFilledOutline(center, radius, fill, outline, strokeThickness);
                break;
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
        float targetFontSize = ImGui.GetFontSize() * zoom;
        nint fontPtr = theme.GetFontForSize(targetFontSize);
        bool useFont = fontPtr != 0;

        unsafe
        {
            if (useFont) ImGui.PushFont(new ImFontPtr((ImFont*)(void*)fontPtr));
        }

        var font = ImGui.GetFont();
        var textSize = font.CalcTextSizeA(targetFontSize, float.MaxValue, 0f, pin.Label);
        Vector2 labelPos = isInput
            ? pinScreen + new Vector2(offsetX, -textSize.Y * 0.5f)
            : pinScreen - new Vector2(offsetX + textSize.X, textSize.Y * 0.5f);

        dl.AddText(font, targetFontSize, labelPos, textColor, pin.Label);

        if (useFont) ImGui.PopFont();
    }
}
