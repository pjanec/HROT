namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    public enum CoordinateSpace : byte
    {
        World       = 0,
        Screen      = 1,
        EntityLocal = 2,
        /// <summary>
        /// ⭐ CE-1033 S5b (docs/DESIGN_Map_3D_Mode.md §3.6, M14) — drawn INTO an entity's card: a small screen-space canvas above
        /// the entity, created because a primitive targets it. <c>AnchorIndex</c> = the entity's network id; <c>ZIndex</c> = the
        /// row (0 = card-wide); coordinates are card-local PIXELS, <c>SizeMode.ScreenPercent</c> = fractions of the card.
        /// ⚠ A terminal that does not know this value must SKIP the primitive, never draw it as world text.
        /// </summary>
        EntityCard = 3,
        /// <summary>
        /// ⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 R2-1, "S1d-2 AS BUILT") — drawn on the VIEW's HUD
        /// CANVAS: a <see cref="ViewCanvas.Width"/> × <see cref="ViewCanvas.Height"/> (16:9) reference, fitted uniformly into the view and centred
        /// (<see cref="ViewCanvas.Fit"/>), so a device's eyepiece keeps its aspect on any window. Coordinates AND sizes (thickness, font, radius,
        /// extents) are canvas pixels; a Text's <c>TextY</c> is its BASELINE and its <see cref="DebugPrimitive.LineOffsetPx"/> its horizontal
        /// alignment (<see cref="ViewCanvas.AlignOf"/>). Drawn after the scene by the one screen pass (<c>GizmoMap.Presentation.ViewCanvasDrawer</c>),
        /// 2-D and 3-D. ⚠ A terminal that does not know this value must SKIP the primitive, never draw it as world.
        /// </summary>
        ViewCanvas = 4
    }
}
