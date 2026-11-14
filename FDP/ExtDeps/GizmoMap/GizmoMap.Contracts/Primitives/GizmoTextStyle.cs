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
    }
}
