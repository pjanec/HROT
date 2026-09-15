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
    /// the LOGIC — topmost wins, unfiltered by capture, identity-bearing only — is all here.</para>
    ///
    /// <para>⭐⭐⭐ <b>RE-BASED <c>2026-09-11</c> onto <c>PickTopmostAnchorId</c> (§6.7).</b> The seam used to
    /// be <c>PickTopmostEntityAnchor</c>, returning <c>(int Index, ushort Generation)</c> — the hit
    /// primitive's ECS handle — and every rail here asserted that handle. ⛔ That handle no longer travels:
    /// the seam answers with the anchor's <b>network id</b> and the caller resolves it in a world. ⚠ The
    /// CLAIMS are unchanged (topmost wins · capture does not blind · tool ids are disjoint); only the
    /// value each one reads changed, which is exactly what a re-base should look like.</para>
    /// </summary>
    public sealed class GizmoLayerEntityHitTestTests
    {
        private const float Zoom = 1f;

        /// <summary>
        /// A box primitive identified by <paramref name="networkId"/>, as an entity avatar draws
        /// (<c>EntityPresentationGizmoShared.EmitPickBox</c>).
        /// ⭐ §6.7 — <c>BoxAnchorId</c> is the ONLY identity stamped; offsets 8/12 are left alone.
        /// </summary>
        private static DebugPrimitive EntityBox(
            long networkId, float x, float y, float size = 4f)
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.Box2D;
            p.BoxCenterX    = x;
            p.BoxCenterY    = y;
            p.BoxExtentX    = size;
            p.BoxExtentY    = size;
            p.BoxAnchorId   = networkId;
            return p;
        }

        /// <summary>⭐⭐⭐ THE claim: a click over an entity's drawn primitive resolves that entity.</summary>
        [Fact]
        public void AClickOverAnEntitysPrimitiveResolvesThatEntity()
        {
            var prims = new[] { EntityBox(networkId: 7003L, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(
                prims, new Vector2(100f, 50f), Zoom);

            Assert.NotNull(hit);
            Assert.Equal(7003L, hit!.Value);
        }

        /// <summary>⭐ A click on empty map resolves nothing — and must not fabricate an anchor.</summary>
        [Fact]
        public void AClickOnEmptyMapResolvesNothing()
        {
            var prims = new[] { EntityBox(networkId: 7003L, x: 100f, y: 50f) };

            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(
                prims, new Vector2(9000f, 9000f), Zoom));
        }

        /// <summary>⭐ An empty frame resolves nothing.</summary>
        [Fact]
        public void AnEmptyFrameResolvesNothing()
        {
            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(
                System.Array.Empty<DebugPrimitive>(), new Vector2(0f, 0f), Zoom));
        }

        /// <summary>
        /// ⛔⛔ <b>A primitive with no anchor id is NOT reportable</b> — it is a sub-element-only handle,
        /// and answering with <c>0</c> would let the caller resolve "entity zero" or route an interaction
        /// to whatever happens to sit at that id.
        /// <para>⚠ §6.7 — the DISCRIMINATOR changed: this used to be <c>AnchorGeneration == 0</c>
        /// ("no live local entity"). It is now <c>BoxAnchorId == 0</c> ("no identity"), which is the same
        /// claim expressed in the field that actually carries identity. A TOOL handle is no longer in this
        /// bucket at all — it has a real id, in the disjoint range, and correctly resolves to no
        /// entity.</para>
        /// </summary>
        [Fact]
        public void APrimitiveWithNoAnchorIdIsNotReported()
        {
            var subElementOnly = EntityBox(networkId: 0L, x: 100f, y: 50f);
            subElementOnly.SubElementId = 4;   // interactive, but nothing identifies its owner

            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(
                new[] { subElementOnly }, new Vector2(100f, 50f), Zoom));
        }

        /// <summary>⭐⭐ Topmost wins — the LAST submitted primitive is the one on top.</summary>
        [Fact]
        public void TheTopmostPrimitiveWins()
        {
            var prims = new[]
            {
                EntityBox(networkId: 1001L, x: 100f, y: 50f),
                EntityBox(networkId: 1002L, x: 100f, y: 50f),   // drawn later ⇒ on top
            };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(
                prims, new Vector2(100f, 50f), Zoom);

            Assert.Equal(1002L, hit!.Value);
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

            var prims = new[] { binding, EntityBox(networkId: 7003L, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(
                prims, new Vector2(100f, 50f), Zoom);

            Assert.NotNull(hit);          // 🔴 null here is the picker going blind again
            Assert.Equal(7003L, hit!.Value);
        }

        // ── S5 (DESIGN_Gizmo_Anchor_Identity.md §6) — THE EXCLUSIVE FILTER COMPARES THE NETWORK ID ──
        //
        // ⚠ These replace the interim S0 rails. S0 compared the WHOLE anchor (value + generation) as a
        //   stop-gap while two id domains still existed; S5 collapses identity to the NETWORK ID, so the
        //   generation is no longer part of the comparison and the rails say so in those terms.

        private const long EntityNetId = 4242L;

        /// <summary>
        /// 🔴🔴🔴 <b>The defect, in S5 terms: a TOOL capture must not admit an ENTITY.</b>
        ///
        /// <para>📐 Tool ids and entity network ids now share ONE numeric space, so they are allocated
        /// disjointly — <c>GlobalGizmoManager.ToolAnchorIdBase</c> (§6.1). Before that, tool ids ran
        /// 1, 2, 3… and network ids 2, 3, 4…, so a global tool's exclusive binding admitted whichever
        /// entity's id collided, and the picker's click reached it: it dragged and got selected.</para>
        /// </summary>
        [Fact]
        public void AToolCaptureDoesNotAdmitAnEntity()
        {
            var prims = new[] { EntityBox(networkId: EntityNetId, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorIdUnderCapture(
                prims, new Vector2(100f, 50f), Zoom,
                exclusiveAnchorId: GizmoMap.Presentation.DebugGizmoLayer.ToolCaptureIdForTests(1));

            Assert.Null(hit);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The disjoint-range guarantee, asserted rather than assumed (§6.1).</b> A tool anchor id
        /// can never equal a plausible entity network id — <c>SequentialIdAllocator</c> starts at 2 and the
        /// editor's at 1000, while tool ids start above 2^40.
        /// </summary>
        [Fact]
        public void AToolAnchorIdCanNeverCollideWithAnEntityNetworkId()
        {
            long firstToolId = GizmoMap.Presentation.DebugGizmoLayer.ToolCaptureIdForTests(1);

            Assert.True(firstToolId > 1L << 39,
                $"tool ids must live in a disjoint high range; got {firstToolId}");
            Assert.True(firstToolId > 1_000_000_000L, "a network id could never reach the tool range");
        }

        /// <summary>
        /// ⭐⭐ <b>The counter-case, so the fix cannot over-filter.</b> An entity-scoped capture — what
        /// <c>DataDrivenGizmoSystem</c> emits, keyed by the entity's NETWORK id (S5) — must still admit
        /// that entity, or handle dragging breaks.
        /// </summary>
        [Fact]
        public void AnEntityCaptureStillAdmitsItsOwnEntity()
        {
            var prims = new[] { EntityBox(networkId: EntityNetId, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorIdUnderCapture(
                prims, new Vector2(100f, 50f), Zoom, exclusiveAnchorId: EntityNetId);

            Assert.NotNull(hit);
            Assert.Equal(EntityNetId, hit!.Value);
        }

        /// <summary>⭐ A capture for a DIFFERENT entity does not admit this one.</summary>
        [Fact]
        public void AnEntityCaptureDoesNotAdmitADifferentEntity()
        {
            var prims = new[] { EntityBox(networkId: EntityNetId, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorIdUnderCapture(
                prims, new Vector2(100f, 50f), Zoom, exclusiveAnchorId: EntityNetId + 1);

            Assert.Null(hit);
        }

        /// <summary>⭐ With NO capture binding the filter is inert — the unfiltered picker path.</summary>
        [Fact]
        public void WithNoCaptureBindingEveryEntityIsStillPickable()
        {
            var prims = new[] { EntityBox(networkId: EntityNetId, x: 100f, y: 50f) };

            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorIdUnderCapture(
                prims, new Vector2(100f, 50f), Zoom, exclusiveAnchorId: null);

            Assert.NotNull(hit);
            Assert.Equal(EntityNetId, hit!.Value);
        }
    }
}
