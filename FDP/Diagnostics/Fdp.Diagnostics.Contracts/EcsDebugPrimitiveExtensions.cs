namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ Convenience projections from a <c>GizmoMap.Contracts.DebugPrimitive</c> to the ECS-side
    /// <see cref="PickToken"/>.
    ///
    /// <para>⛔⛔ <b>§6.7, 2026-09-11 — <c>GetAnchor()</c> IS GONE.</b> It returned
    /// <c>new Entity(p.AnchorIndex, p.AnchorGeneration)</c>: an ECS handle fabricated out of two fields
    /// that, for most primitive shapes, hold something else entirely (a narrowed <c>SpatialAnchor</c>
    /// cache key, a <c>StringHash</c>, a <c>LineOffsetPx</c> — see <c>DebugPrimitive.cs</c> offset 8/12).
    /// 📐 Measured: it had <b>no production caller</b>, only a rail asserting the fabrication itself.
    /// ⇒ 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7 — identity is the anchor network id, and a
    /// handle is resolved from it in a world, never reconstructed from wire-adjacent bytes.</para>
    ///
    /// <para>⚠ This mirrors <c>GizmoMap.Presentation.DebugGizmoLayer.MakePickToken</c>, which is the
    /// PRODUCTION path (it also carries <c>GizmoTypeId</c>). ⛔ Do not grow a second policy here — if the
    /// two ever need to differ, that is a finding, not a feature.</para>
    /// </summary>
    public static class EcsDebugPrimitiveExtensions
    {
        /// <summary>
        /// The primitive's pick token: its anchor IDENTITY (<c>BoxAnchorId</c> — a network id, a disjoint
        /// tool id, or 0) plus the sub-element index.
        /// </summary>
        public static PickToken GetPickToken(this DebugPrimitive p)
            => new PickToken { AnchorId = p.BoxAnchorId, SubElementId = p.SubElementId };
    }
}
