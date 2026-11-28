using System;
using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-122 / D-124 / D-130, mockups <c>devices-hud.svg</c> panels 2–6,
    /// <c>devices-hud-designator.svg</c>) — THE HUD button of every device: <see cref="Button"/> draws the chip (its fill, its label, its hotkey in a key box)
    /// AND appends the chip's <see cref="DebugPrimitiveShape.PanelButton"/> hit zone in the SAME call, inside the HUD scope the projector draws in — ⛔ a
    /// projector never emits a hit zone separately, so the picture and what can be clicked cannot drift (by construction). The station runs no device code:
    /// its virtual panel adapter reads the zone and presses the element through the panel IO (D-130). Drawn on the view HUD canvas (<see cref="ViewCanvas"/>),
    /// sized from the chip's height so every device's chips share one look. 0 B (the caller holds its label and badge texts).
    /// </summary>
    public static class HudPanel
    {
        /// <summary>The chip's colours (the mockups': the house blue when lit, a dark glass otherwise, greyed text when disabled).</summary>
        public static readonly Rgba32 LitFill = new(51, 105, 173, 217), Fill = new(0, 0, 0, 158), Outline = new(138, 138, 150),
            Text = new(255, 255, 255), Key = new(200, 200, 208), Dim = new(106, 106, 116);

        /// <summary>The mockup's chip height every proportion below is taken at (mockup px).</summary>
        public const float ReferenceHeight = 20f;

        /// <summary>
        /// Draws the chip of element (<paramref name="kind"/>, <paramref name="index"/>) at the canvas rectangle <paramref name="at"/> + <paramref name="size"/>
        /// and appends its PanelButton. <paramref name="badge"/> is the hotkey as shown (<see cref="Badge"/>; empty = no key box), <paramref name="hotkey"/> its
        /// <c>EditorKey</c> value (<see cref="HotkeyCode"/>; 0 = none).
        /// </summary>
        public static void Button(IDebugDrawBuilder draw, DeviceElementKind kind, int index, Vector2 at, Vector2 size, in FixedString32 label, in FixedString32 badge,
                                  PanelButtonState state, ushort hotkey, byte layer)
        {
            float u = size.Y / ReferenceHeight;
            bool lit = state == PanelButtonState.Lit, dim = state == PanelButtonState.Disabled;
            draw.EmitRaw(ViewCanvas.Rect(at, size, lit ? LitFill : Fill, lit ? default : Outline, 1f * u, layer));
            draw.EmitRaw(ViewCanvas.Text(at.X + 7f * u, at.Y + 14f * u, label, dim ? Dim : Text, 10.5f * u, layer,
                                         style: lit ? GizmoTextStyle.HaloBold : GizmoTextStyle.Halo));
            if (!badge.IsEmpty)
            {
                float bw = BadgeWidth(badge.Length) * u;
                var box = new Vector2(at.X + size.X - 5f * u - bw, at.Y + 3.5f * u);
                draw.EmitRaw(ViewCanvas.Rect(box, new Vector2(bw, 13f * u), default, dim ? Dim : Key, 1f * u, layer));
                draw.EmitRaw(ViewCanvas.Text(box.X + bw * 0.5f, at.Y + 13.5f * u, badge, dim ? Dim : Key, 8.5f * u, layer, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            }
            draw.EmitRaw(PanelButtons.Make((byte)kind, (byte)Math.Clamp(index, 0, 255), at, size, state, hotkey, layer));
        }

        /// <summary>The key box's width for a badge of <paramref name="chars"/> characters (mockup px at <see cref="ReferenceHeight"/>).</summary>
        public static float BadgeWidth(int chars) => MathF.Max(13f, chars * 5.7f + 5f);

        /// <summary>A hotkey (an <c>EditorKey</c> name, <see cref="DeviceElementDto.Hotkey"/>) as its key box shows it: a letter or digit as itself, <c>Enter</c> ⇒
        /// <c>ENT</c>, <c>Equals</c> ⇒ <c>=</c>, <c>Minus</c> ⇒ <c>-</c>, anything else its first three letters. Built once per type (allocates).</summary>
        public static FixedString32 Badge(string? hotkey)
        {
            if (string.IsNullOrEmpty(hotkey)) return default;
            string text = hotkey switch
            {
                "Enter" => "ENT", "Equals" => "=", "Minus" => "-", "KpAdd" => "+", "KpSubtract" => "-", "Space" => "SPC",
                _ when hotkey.Length == 1 => hotkey.ToUpperInvariant(),
                _ when hotkey.Length == 2 && hotkey[0] == 'D' && char.IsDigit(hotkey[1]) => hotkey.Substring(1),
                _ => hotkey.Substring(0, Math.Min(3, hotkey.Length)).ToUpperInvariant(),
            };
            return new FixedString32(text);
        }

        /// <summary>A hotkey name as its <c>EditorKey</c> value (0 = none or unknown).</summary>
        public static ushort HotkeyCode(string? hotkey)
            => !string.IsNullOrEmpty(hotkey) && Enum.TryParse<NodeEditor.Primitives.EditorKey>(hotkey, true, out var key) ? (ushort)key : (ushort)0;
    }
}
