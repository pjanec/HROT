// SC-GZ026: geometry-aware hit-testing.
//
// 🔴🔴 RE-HOMED 2026-09-10 (R4, docs/DESIGN_Gizmo_Renderer_Seam.md §6).
//   ⛔ These four rails used to drive `DebugGizmoLayer.HandleInput(worldPos, button, isPressed)` and
//     assert it returned true. That method returns FALSE BY DESIGN — the gizmo layer declines
//     IMapLayer input because its input arrives through the inner terminal, which polls the hardware
//     (see the note on HandleInput itself, and MapCanvas.cs:229-252 offering it to every layer).
//   ⛔ They never caught that, because the class SIGSEGV'd before running: its renderer double
//     subclassed the wrapper, so Raylib drew underneath it (CE-259aa, defect E1).
//   ⭐⭐ But the GEOMETRY they assert — segment distance, sphere radius, screen-pixel radius scaling
//     with zoom — is UNIQUE COVERAGE: `GizmoLayerEntityHitTestTests` rails IDENTITY (anchors, capture
//     filtering) and `GizmoMap.Presentation.Tests` rails RENDERING. ⇒ R-137: unification may not cost
//     a capability, so the geometry is re-homed onto the LIVE hit-test rather than deleted.
//   ⭐ The live seam is `GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor` — the same
//     mechanism `CE-259p` exposed for the entity picker, public and headless.
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    public class DebugGizmoLayerHitTests
    {
        private static Entity DummyAnchor => new Entity(99, 1);

        /// <summary>An anchored primitive the hit-test will consider: a live ECS payload + a network id.</summary>
        private static DebugPrimitive Anchored(DebugPrimitiveShape shape, SizeMode sizeMode)
        {
            var anchor = DummyAnchor;
            var p = default(DebugPrimitive);
            p.Shape            = shape;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = sizeMode;
            p.TargetView       = PipelineTarget.Map2D;
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;
            p.BoxAnchorId      = 90210L;          // the identity, per DESIGN_Gizmo_Anchor_Identity §5.1
            return p;
        }

        private static (int Index, ushort Generation)? Pick(DebugPrimitive p, Vector2 at, float zoom = 1f)
            => GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(new[] { p }, at, zoom);

        // ⛔⛔ MEASURED 2026-09-10 — A CAPABILITY THE MIGRATION DROPPED, and these rails were its only
        //   record. SC-GZ026-1/2/4 originally hit-tested a LINE. The live hit-test
        //   (GizmoMap.Presentation.DebugGizmoLayer.FindTopmostInteractivePrimitive) handles
        //   **Box2D and Sphere ONLY** — bindings are skipped and every other shape falls through as a
        //   miss (matching DESIGN_Gizmo_Anchor_Identity.md §2 ⑯b). ⇒ a Line is UNPICKABLE today.
        //   🔒 R-137 — unification may not cost a capability — so the loss is FILED (CE-259ac), not
        //     silently accepted, and not re-implemented here on a rail's say-so: whether gizmo lines
        //     should be pickable is a UX decision with an owner, and no design record asks for it.
        //   ⭐ The GEOMETRY these rails existed to protect — inside/outside an extent, and a
        //     screen-pixel radius that scales with zoom — is preserved below on Box2D, the shape the
        //     live hit-test actually serves.

        // SC-GZ026-1: a click inside a Box2D's extent hits, and reports that box's anchor.
        [Fact]
        public void SC_GZ026_1_InsideBoxExtent_IsHit()
        {
            var p = Anchored(DebugPrimitiveShape.Box2D, SizeMode.WorldMeters);
            p.BoxCenterX = 50f;
            p.BoxCenterY = 0f;
            p.BoxExtentX = 8f;
            p.BoxExtentY = 8f;

            var hit = Pick(p, new Vector2(50f, 0f));

            Assert.NotNull(hit);
            Assert.Equal(99, hit!.Value.Index);
        }

        // SC-GZ026-2: a click well beyond the extent (+ the 5-unit grace radius) misses.
        [Fact]
        public void SC_GZ026_2_BeyondBoxExtent_IsMiss()
        {
            var p = Anchored(DebugPrimitiveShape.Box2D, SizeMode.WorldMeters);
            p.BoxCenterX = 50f;
            p.BoxCenterY = 0f;
            p.BoxExtentX = 8f;
            p.BoxExtentY = 8f;

            Assert.Null(Pick(p, new Vector2(110f, 0f)));
        }

        // SC-GZ026-2b: 🔒 the dropped capability, pinned so it cannot be lost twice. A Line is NOT
        // pickable — if someone implements line hit-testing this reddens, which is the moment to close
        // CE-259ac and restore SC-GZ026-1/2 in their original Line form.
        [Fact]
        public void SC_GZ026_2b_ALineIsNotPickable_CE259ac()
        {
            var p = Anchored(DebugPrimitiveShape.Line, SizeMode.WorldMeters);
            p.LineStart = new Vector3(0f, 0f, 0f);
            p.LineEnd   = new Vector3(100f, 0f, 0f);

            Assert.Null(Pick(p, new Vector2(50f, 0f)));   // dead centre of the segment
        }

        // SC-GZ026-3: a click within SphereRadius of the centre hits.
        [Fact]
        public void SC_GZ026_3_SphereCenter_IsHit()
        {
            var p = Anchored(DebugPrimitiveShape.Sphere, SizeMode.WorldMeters);
            p.SphereCenter = new Vector3(20f, 30f, 0f);
            p.SphereRadius = 10f;

            Assert.NotNull(Pick(p, new Vector2(22f, 31f)));
        }

        // SC-GZ026-3b: ...and outside it misses. ⭐ The pair is what pins the radius; either alone
        // passes for a hit-test that always says yes (or always no).
        [Fact]
        public void SC_GZ026_3b_OutsideSphereRadius_IsMiss()
        {
            var p = Anchored(DebugPrimitiveShape.Sphere, SizeMode.WorldMeters);
            p.SphereCenter = new Vector3(20f, 30f, 0f);
            p.SphereRadius = 10f;

            Assert.Null(Pick(p, new Vector2(60f, 30f)));
        }

        // SC-GZ026-4: a ScreenPixels hit radius SHRINKS in world units as zoom rises — the 5px radius
        // is 5 world units at zoom 1 and 0.5 at zoom 10, so a point 2 units out hits at zoom 1 and
        // misses at zoom 10. ⭐ This is the assertion the retired route could not express at all.
        [Fact]
        public void SC_GZ026_4_ScreenPixels_ZoomScalesHitRadius()
        {
            // A zero-extent Box2D, so the ONLY thing that can produce a hit is the grace radius:
            // 5 world units at zoom 1, 0.5 at zoom 10. A point 2 units away straddles the two.
            var p = Anchored(DebugPrimitiveShape.Box2D, SizeMode.ScreenPixels);
            p.BoxCenterX = 50f;
            p.BoxCenterY = 0f;
            p.BoxExtentX = 0f;
            p.BoxExtentY = 0f;

            Assert.NotNull(Pick(p, new Vector2(50f, 2f), zoom: 1f));
            Assert.Null(Pick(p, new Vector2(50f, 2f), zoom: 10f));
        }
    }
}
