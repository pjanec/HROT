namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ B-17 P7 ⑥ — how a <see cref="DebugPrimitiveShape.Text"/> is boxed. Carried at <see cref="DebugPrimitive.TextStyle"/> (offset 14, which
    /// aliases <see cref="DebugPrimitive.SizeMode"/>: a Text never reads SizeMode). One drawer honours it on every map, 2-D and 3-D
    /// (<c>GizmoMap.Presentation.GizmoTextDraw</c>). 📄 docs/UX/DESIGN_Map_Interaction.md "P7 — the build".
    /// </summary>
    public enum GizmoTextStyle : byte
    {
        /// <summary>The bare glyphs — what every text was before P7.</summary>
        Plain = 0,
        /// <summary>A translucent dark rounded box behind the text, so a hint reads over any terrain.</summary>
        Backdrop = 1,
        /// <summary>An ImGui-button blue box with a 1 px border — a label the user can click (the hit area is the gizmo's own).</summary>
        Button = 2,
        /// <summary>⭐ CE-1977 — <see cref="Backdrop"/>'s look for a label that belongs to the CURSOR (the armed tool's name and hint): the 3-D map never hides it
        /// behind the ground (its text sits below the cursor, over ground nearer the eye than the cursor's own point).</summary>
        Cursor = 3,
        /// <summary>⭐ Simulated devices S1d-2 (DESIGN_Simulated_Devices §3.6, mockup <c>devices-hud.svg</c>) — a HUD readout over the 3-D view: the glyphs
        /// with a dark halo round them (legible on sky and ground alike), no box.</summary>
        Halo = 4,
        /// <summary>⭐ S1d-2 — <see cref="Halo"/>, the glyphs thickened (the mockup's bold readouts; the gizmo font has no bold face).</summary>
        HaloBold = 5,
    }
}
