using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b>The ECS-bus pick token. Identity is the ANCHOR NETWORK ID — §6.7, 2026-09-11.</b>
    /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7.
    ///
    /// <para>⛔⛔ <b>It used to hold <c>Entity Target</c></b> — a process-local ECS handle — and that one
    /// field shaped the whole pipeline around it: the terminal forwarded its handle as a token payload so
    /// this could be filled without a lookup; the DDS egress translator looked the handle back UP to a
    /// network id to put it on the wire; the ingress translator looked the received id back DOWN to a
    /// handle to put it in here. ⇒ two map round-trips per remote interaction, and a node without a
    /// <c>NetworkEntityMap</c> <b>dropped every incoming interaction silently</b>
    /// (<c>GizmoInteractionIngressTranslator</c> returned early on the miss).</para>
    ///
    /// <para>⭐⭐ Now the id travels unchanged from the picked primitive to the wire and back, and the
    /// <b>consumer</b> resolves it — <c>DataDrivenGizmoSystem</c> and <c>SelectionInteractionSystem</c>
    /// each hold an <c>EntityRepository</c>, which is exactly where a resolve can be done. ⭐ It is also
    /// the shape <c>GlobalGizmoManager</c> has always had: <c>Dictionary&lt;long, IEntityStatefulGizmo&gt;</c>,
    /// keyed by the anchor id.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PickToken
    {
        /// <summary>
        /// The anchor's stable identity: an entity's <c>NetworkIdentity.Value</c>, a disjoint tool id
        /// (≥ <c>GlobalGizmoManager.ToolAnchorIdBase</c>), <c>-1</c> for the canvas, or <c>0</c> for none.
        /// </summary>
        public long AnchorId;

        public uint SubElementId;
        public uint GizmoTypeId;  // composite routing key carried through the ECS event bus

        /// <summary>
        /// ⭐⭐⭐ <b><c>&gt; 0</c>, not <c>!= 0</c>, and the difference is load-bearing.</b> The two
        /// "no entity anchor" values are <c>0</c> (a left-click on empty canvas — the terminal passes
        /// <c>default(GizmoPickToken)</c>) and <b><c>-1</c>, the CANVAS SENTINEL</b> that
        /// <c>GizmoMap.Presentation.DebugGizmoLayer</c> uses for the canvas context menu. ⛔ A
        /// <c>!= 0</c> test calls <c>-1</c> valid, and the only production reader of this flag is
        /// <c>Fdp.Presentation DebugGizmoLayer:295/:303</c> choosing
        /// <c>EntityLocal</c> vs <c>World</c> for a drag/commit position — so it would place a canvas
        /// drag in an entity's local frame.
        ///
        /// <para>⚠ Every legitimate anchor is positive: entity network ids count from 1
        /// (<c>SequentialIdAllocator</c>) and tool ids start at <c>GlobalGizmoManager.ToolAnchorIdBase</c>
        /// = <c>1&lt;&lt;40</c> (§6.1). ⛔ The negative range is reserved for sentinels precisely because
        /// <c>-1</c> was already taken.</para>
        /// </summary>
        public bool IsValid => AnchorId > 0;
    }
}
