using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    /// <summary>
    /// ⭐⭐⭐ Rails for the entity hit-test — <c>CE-259p</c>.
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7g.
    ///
    /// <para>🔴 <b>The operator-found defect:</b> <i>"when picker active clicking entity did not close the
    /// picker."</i> 📐 EVERY production <c>IMapLayer.PickEntity</c> returned <c>null</c>, so
    /// <c>MapCanvas.PickTopmostEntity</c> was dead and <c>EntityPickerGizmo</c>'s pick arm — gated on
    /// <c>_hoveredValid</c>, which is set from that hit-test — was unreachable.</para>
    ///
    /// <para>⚠ <b>These rail the TERMINAL's static hit-test, not the layer wrapper</b>, because the wrapper
    /// needs a live <c>MapCamera</c> and a Raylib context. ⭐ The wrapper is three lines over this call;
    /// the LOGIC — topmost wins, unfiltered by capture, entity-domain only — is all here.</para>
    /// </summary>
    public sealed class GizmoLayerEntityHitTestTests
    {
        private const float Zoom = 1f;

        /// <summary>A box primitive anchored to a live local ECS entity, as an entity avatar draws.</summary>
        private static DebugPrimitive EntityBox(int index, ushort generation, float x, float y, float size = 4f)
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.Box2D;
            p.BoxCenterX    = x;
            p.BoxCenterY    = y;
            p.BoxExtentX    = size;
            p.BoxExtentY    = size;
            p.AnchorIndex      = index;
            p.AnchorGeneration = generation;
            return p;
        }

        /// <summary>⭐⭐⭐ THE claim: a click over an entity's drawn primitive resolves that entity.</summary>
        [Fact]
        public void AClickOverAnEntitysPrimitiveResolvesThatEntity()
        {
            var prims = new[] { EntityBox(index: 7, generation: 3, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                prims, new Vector2(100f, 50f), Zoom);

            Assert.NotNull(hit);
            Assert.Equal(7, hit!.Value.Index);
            Assert.Equal((ushort)3, hit.Value.Generation);
        }

        /// <summary>⭐ A click on empty map resolves nothing — and must not fabricate an entity.</summary>
        [Fact]
        public void AClickOnEmptyMapResolvesNothing()
        {
            var prims = new[] { EntityBox(index: 7, generation: 3, x: 100f, y: 50f) };

            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                prims, new Vector2(9000f, 9000f), Zoom));
        }

        /// <summary>⭐ An empty frame resolves nothing.</summary>
        [Fact]
        public void AnEmptyFrameResolvesNothing()
        {
            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                System.Array.Empty<DebugPrimitive>(), new Vector2(0f, 0f), Zoom));
        }

        /// <summary>
        /// ⛔⛔ <b>A primitive with <c>AnchorGeneration == 0</c> is NOT an entity</b> — it is a stateless
        /// tool handle or a remote network object, addressing a different domain. ⭐ Returning a fabricated
        /// <c>Entity(index, 0)</c> would hand the picker a handle that resolves to the wrong object, or to
        /// a live entity that merely shares an index.
        /// </summary>
        [Fact]
        public void ANonEntityAnchorIsNotReportedAsAnEntity()
        {
            var toolHandle = EntityBox(index: 7, generation: 0, x: 100f, y: 50f);

            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                new[] { toolHandle }, new Vector2(100f, 50f), Zoom));
        }

        /// <summary>⭐⭐ Topmost wins — the LAST submitted primitive is the one on top.</summary>
        [Fact]
        public void TheTopmostPrimitiveWins()
        {
            var prims = new[]
            {
                EntityBox(index: 1, generation: 1, x: 100f, y: 50f),
                EntityBox(index: 2, generation: 1, x: 100f, y: 50f),   // drawn later ⇒ on top
            };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                prims, new Vector2(100f, 50f), Zoom);

            Assert.Equal(2, hit!.Value.Index);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>UNFILTERED BY THE CAPTURE BINDING — this is the whole point.</b>
        /// 🔒 A picker holds exclusive focus so that nothing ELSE starts an interaction underneath it;
        /// ⛔ but it must still see what it is pointing at, or it can never pick. This rail pins that a
        /// capture binding in the frame does not blind the hit-test.
        /// </summary>
        [Fact]
        public void ACaptureBindingInTheFrameDoesNotBlindTheHitTest()
        {
            var binding = DebugPrimitive.MakeInputCaptureBinding(
                networkId: 4242, subElementId: 0, exclusive: true, wantsRawInput: true);

            var prims = new[] { binding, EntityBox(index: 7, generation: 3, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                prims, new Vector2(100f, 50f), Zoom);

            Assert.NotNull(hit);          // 🔴 null here is the picker going blind again
            Assert.Equal(7, hit!.Value.Index);
        }

        // ── S0 (DESIGN_Gizmo_Anchor_Identity.md §6) — THE EXCLUSIVE FILTER COMPARES THE WHOLE ANCHOR ──

        /// <summary>
        /// 🔴🔴🔴 <b>The defect, reproduced: a click LEAKED past an exclusive tool to whichever entity's
        /// ECS index equalled the active tool's id.</b>
        ///
        /// <para>📐 <c>GlobalGizmoManager</c> keys its capture binding by a TOOL id from
        /// <c>NewId()</c> — 1, 2, 3… — with NO generation stamp, while an entity pick box routes its ECS
        /// <c>AnchorIndex</c>: the same small-integer range. The filter compared only the VALUE, so tool
        /// id 3 and entity index 3 matched and the picker's click reached the entity underneath, which
        /// then dragged and got selected.</para>
        ///
        /// <para>⭐ Red-proof: drop the <c>prim.AnchorGeneration != exclusiveAnchorGen</c> term and this
        /// rail returns the entity.</para>
        /// </summary>
        [Fact]
        public void AToolDomainCaptureDoesNotAdmitAnEntityWhoseIndexEqualsTheToolId()
        {
            var prims = new[] { EntityBox(index: 3, generation: 4, x: 100f, y: 50f) };

            // A GLOBAL (tool) binding: MakeInputCaptureBinding leaves AnchorGeneration at 0.
            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchorUnderCapture(
                prims, new Vector2(100f, 50f), Zoom,
                exclusiveAnchorId: 3L, exclusiveAnchorGen: 0);

            Assert.Null(hit);
        }

        /// <summary>
        /// ⭐⭐ <b>The counter-case, so the fix cannot over-filter.</b> An ENTITY-domain capture — what
        /// <c>DataDrivenGizmoSystem</c> emits, keyed by <c>entity.Index</c> WITH the generation stamped —
        /// must still admit that entity, or handle dragging breaks.
        /// </summary>
        [Fact]
        public void AnEntityDomainCaptureStillAdmitsItsOwnEntity()
        {
            var prims = new[] { EntityBox(index: 3, generation: 4, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchorUnderCapture(
                prims, new Vector2(100f, 50f), Zoom,
                exclusiveAnchorId: 3L, exclusiveAnchorGen: 4);

            Assert.NotNull(hit);
            Assert.Equal(3, hit!.Value.Index);
            Assert.Equal((ushort)4, hit.Value.Generation);
        }

        /// <summary>
        /// ⭐ <b>A STALE handle is rejected too</b> — same index, generation bumped after the ECS slot was
        /// reused. 🔒 <c>DebugPrimitive.cs:31-33</c> warns about exactly this and nothing guarded it
        /// before S0.
        /// </summary>
        [Fact]
        public void AStaleAnchorGenerationIsRejected()
        {
            var prims = new[] { EntityBox(index: 3, generation: 9, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchorUnderCapture(
                prims, new Vector2(100f, 50f), Zoom,
                exclusiveAnchorId: 3L, exclusiveAnchorGen: 4);

            Assert.Null(hit);
        }

        /// <summary>⭐ With NO capture binding the filter is inert — the unfiltered picker path.</summary>
        [Fact]
        public void WithNoCaptureBindingEveryEntityIsStillPickable()
        {
            var prims = new[] { EntityBox(index: 3, generation: 4, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchorUnderCapture(
                prims, new Vector2(100f, 50f), Zoom,
                exclusiveAnchorId: null, exclusiveAnchorGen: 0);

            Assert.NotNull(hit);
            Assert.Equal(3, hit!.Value.Index);
        }
    }
}
