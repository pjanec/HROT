using System;
using System.Numerics;
using Fdp.Presentation.Icons;
using ImGuiNET;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Shared transport-control icon drawing helpers. Renders Play/Pause/Step/Stop shapes
/// via <see cref="ImDrawListPtr"/> primitives and provides time/rate formatting shared
/// by status-bar and toolbar time-control sections.
/// </summary>
public static class TransportIcons
{
    /// <summary>Icon shapes understood by the transport drawing helpers.</summary>
    public enum BtnShape { Play, Pause, Step, Stop }

    /// <summary>Ordered list of choosable time-rate values shown in rate-selector popups.</summary>
    public static readonly float[] TimeRates = { 0.1f, 0.5f, 1.0f, 1.5f, 2.0f, 5.0f, 10.0f };

    // ── Button drawing ──────────────────────────────────────────────────────

    /// <summary>
    /// Draws a custom transport-control icon button using the
    /// <c>InvisibleButton + ImDrawList</c> pattern. Returns <c>true</c> on the frame it
    /// is clicked. When <paramref name="enabled"/> is <c>false</c> the icon is rendered
    /// dimmed and the hit area is replaced by a passive <c>Dummy</c> widget.
    /// </summary>
    public static bool DrawTransportButton(string id, float size, BtnShape shape, bool enabled)
    {
        // Capture the top-left screen position BEFORE the hit-area widget so we can
        // draw at that position via the ImDrawList regardless of cursor movement.
        var pos = ImGui.GetCursorScreenPos();

        bool clicked = false;
        bool hovered = false;
        bool pressed = false;

        if (enabled)
        {
            clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
            hovered = ImGui.IsItemHovered();
            pressed = ImGui.IsItemActive();
        }
        else
        {
            // Advance layout without registering a hit area.
            ImGui.Dummy(new Vector2(size, size));
        }

        var dl = ImGui.GetWindowDrawList();

        // Icon shape — shift 1 px down-right when pressed for tactile feedback.
        var drawPos = pressed ? pos + new Vector2(1f, 1f) : pos;
        DrawShape(dl, shape, drawPos, size, dim: !enabled, hovered: hovered);

        // Shared chrome: inset white hover frame on top (transport has no persistent toggle).
        IconButtonChrome.DrawHoverFrame(dl, pos, new Vector2(size, size), hovered);

        return clicked;
    }

    // ── Shape geometry ──────────────────────────────────────────────────────

    /// <summary>
    /// Draws the icon geometry onto <paramref name="dl"/> at the given
    /// <paramref name="pos"/> using filled primitives.
    /// </summary>
    public static void DrawShape(
        ImDrawListPtr dl, BtnShape shape, Vector2 pos, float size,
        bool dim, bool hovered)
    {
        float alpha = dim ? 0.28f : (hovered ? 1.0f : 0.85f);
        float pad   = MathF.Round(size * 0.22f);

        switch (shape)
        {
            case BtnShape.Play:
            {
                // Green filled triangle pointing right.
                uint col = ImGui.GetColorU32(new Vector4(0.20f, 0.78f, 0.20f, alpha));
                var p0 = pos + new Vector2(pad, pad);
                var p1 = pos + new Vector2(size - pad, size * 0.5f);
                var p2 = pos + new Vector2(pad, size - pad);
                dl.AddTriangleFilled(p0, p1, p2, col);
                break;
            }

            case BtnShape.Pause:
            {
                // Two white vertical bars.
                uint col  = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
                float bw  = MathF.Round(size * 0.18f);
                float bx  = pos.X + pad;
                float by  = pos.Y + pad;
                float bh  = size - pad * 2f;
                dl.AddRectFilled(
                    new Vector2(bx,           by),
                    new Vector2(bx + bw,       by + bh), col);
                dl.AddRectFilled(
                    new Vector2(bx + bw * 2.2f, by),
                    new Vector2(bx + bw * 3.2f, by + bh), col);
                break;
            }

            case BtnShape.Step:
            {
                // Small right-pointing triangle immediately left of a vertical bar.
                uint  col    = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
                float lineW  = MathF.Round(size * 0.16f);
                float lineX  = pos.X + size - pad - lineW;
                // Triangle tip stops at the left edge of the vertical bar.
                var   p0     = pos + new Vector2(pad, pad);
                var   p1     = new Vector2(lineX, pos.Y + size * 0.5f);
                var   p2     = pos + new Vector2(pad, size - pad);
                dl.AddTriangleFilled(p0, p1, p2, col);
                // Vertical bar.
                dl.AddRectFilled(
                    new Vector2(lineX,         pos.Y + pad),
                    new Vector2(lineX + lineW, pos.Y + size - pad), col);
                break;
            }

            case BtnShape.Stop:
            {
                // Red filled square.
                uint col = ImGui.GetColorU32(new Vector4(0.90f, 0.20f, 0.20f, alpha));
                dl.AddRectFilled(
                    pos + new Vector2(pad, pad),
                    pos + new Vector2(size - pad, size - pad), col);
                break;
            }
        }
    }

    // ── Formatting ──────────────────────────────────────────────────────────

    /// <summary>
    /// Formats a time-rate multiplier compactly: integers without decimal point,
    /// fractional values with one decimal place. Examples: 1x, 2x, 0.5x, 1.5x.
    /// </summary>
    public static string FormatRate(float rate)
    {
        int rounded = (int)MathF.Round(rate * 10f);
        return (rounded % 10 == 0)
            ? $"{rounded / 10}x"
            : $"{rate.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}x";
    }

    /// <summary>
    /// Formats a duration in seconds as <c>HH:MM:SS.mmm</c>.
    /// Example: 3661.234 → "01:01:01.234".
    /// </summary>
    public static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
