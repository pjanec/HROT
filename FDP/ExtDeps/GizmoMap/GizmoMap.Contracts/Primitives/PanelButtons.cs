using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>⭐ SD-S5v (D-122) — how a HUD button is drawn and whether it takes a click: <see cref="Lit"/> = its element is active (a lamp in the chip),
    /// <see cref="Disabled"/> = the device would do nothing with it now (greyed; a click is still the device's to refuse, D-129).</summary>
    public enum PanelButtonState : byte { Normal = 0, Lit = 1, Disabled = 2 }

    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-122 / D-130) — THE <see cref="DebugPrimitiveShape.PanelButton"/>
    /// meta primitive: a HUD button's HIT ZONE, appended by the SAME call that draws its chip (<c>HudPanel.Button</c>, on the brain, inside the device's
    /// <c>HudScope</c> run) — so what the trainee sees and what he can click cannot drift. It carries the device ELEMENT the chip stands for (its kind and its
    /// index within that kind in the type's descriptor, D-142), the chip's rectangle on the view HUD canvas (<see cref="ViewCanvas"/>), its state and its
    /// hotkey (an <c>EditorKey</c> value, 0 = none). Never drawn (meta — every drawer skips it), never evicted (protected), never picked by the map's hit test;
    /// the station's virtual panel adapter reads the bound view's kept run and hit-tests it in CANVAS coordinates. ⭐ The ONE writer and reader. 0 B.
    /// </summary>
    public static class PanelButtons
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Is(in DebugPrimitive p) => p.Shape == DebugPrimitiveShape.PanelButton;

        /// <summary>A PanelButton for element (<paramref name="kind"/>, <paramref name="element"/>) over the canvas rectangle at <paramref name="at"/> (top-left)
        /// of <paramref name="size"/>.</summary>
        public static DebugPrimitive Make(byte kind, byte element, Vector2 at, Vector2 size, PanelButtonState state, ushort hotkey, byte layer)
            => new()
            {
                Shape = DebugPrimitiveShape.PanelButton, Space = CoordinateSpace.ViewCanvas, DebugLayer = layer,
                PanelKind = kind, PanelElement = element, PanelState = state, PanelHotkey = hotkey,
                PanelX = at.X, PanelY = at.Y, PanelW = size.X, PanelH = size.Y,
            };

        /// <summary>Whether the canvas point <paramref name="canvas"/> is on the button.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(in DebugPrimitive p, Vector2 canvas)
            => canvas.X >= p.PanelX && canvas.X < p.PanelX + p.PanelW && canvas.Y >= p.PanelY && canvas.Y < p.PanelY + p.PanelH;

        /// <summary>The TOPMOST (last emitted) PanelButton of <paramref name="frame"/> under the canvas point, or −1. An index loop — 0 B.</summary>
        public static int Hit(ReadOnlySpan<DebugPrimitive> frame, Vector2 canvas)
        {
            for (int i = frame.Length - 1; i >= 0; i--)
                if (frame[i].Shape == DebugPrimitiveShape.PanelButton && Contains(in frame[i], canvas)) return i;
            return -1;
        }

        /// <summary>The first PanelButton of <paramref name="frame"/> for element (<paramref name="kind"/>, <paramref name="element"/>), or −1.</summary>
        public static int Find(ReadOnlySpan<DebugPrimitive> frame, byte kind, byte element)
        {
            for (int i = 0; i < frame.Length; i++)
                if (frame[i].Shape == DebugPrimitiveShape.PanelButton && frame[i].PanelKind == kind && frame[i].PanelElement == element) return i;
            return -1;
        }

        /// <summary>A view point (pixels, the view's top-left origin) on the HUD canvas fitted into a view of <paramref name="viewSize"/> — the inverse of
        /// <see cref="ViewCanvas.Fit"/>.</summary>
        public static Vector2 ToCanvas(Vector2 viewPoint, Vector2 viewSize)
        {
            ViewCanvas.Fit(viewSize, out var origin, out float scale);
            return (viewPoint - origin) / MathF.Max(1e-6f, scale);
        }

        /// <summary>The button's centre on the view (pixels) — where a scripted click lands.</summary>
        public static Vector2 CentreOnView(in DebugPrimitive p, Vector2 viewSize)
        {
            ViewCanvas.Fit(viewSize, out var origin, out float scale);
            return ViewCanvas.ToView(new Vector2(p.PanelX + p.PanelW * 0.5f, p.PanelY + p.PanelH * 0.5f), origin, scale);
        }
    }
}
