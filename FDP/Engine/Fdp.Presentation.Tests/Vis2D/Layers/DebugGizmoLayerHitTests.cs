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
        /// <summary>⭐ §6.7 — an anchored primitive the hit-test will consider: a network id, and that
        /// is all it takes. (There used to be a <c>DummyAnchor</c> ECS handle stamped alongside.)</summary>
        private const long AnchorNetId = 90210L;

        private static DebugPrimitive Anchored(DebugPrimitiveShape shape, SizeMode sizeMode)
        {
            var p = default(DebugPrimitive);
            p.Shape            = shape;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = sizeMode;
            p.TargetView       = PipelineTarget.Map2D;
            // ⭐ §6.7 — only the identity is stamped. `AnchorIndex`/`AnchorGeneration` used to carry
            //   the ECS handle here too; nothing reads one now.
            p.BoxAnchorId      = AnchorNetId;
            return p;
        }

        // ⭐ §6.7 — the seam answers with the anchor's NETWORK ID (it was PickTopmostEntityAnchor,
        //   returning the hit primitive's (Index, Generation) ECS handle).
        private static long? Pick(DebugPrimitive p, Vector2 at, float zoom = 1f)
            => GizmoMap.Presentation.DebugGizmoLayer.PickTopmostAnchorId(new[] { p }, at, zoom);

        // 📌 HISTORY, 2026-09-10 → 2026-09-11. SC-GZ026-1/2/4 originally hit-tested a LINE, and the live
        //   hit-test served Box2D and Sphere only ⇒ a capability the terminal migration had dropped
        //   (R-137). It was filed as CE-259ac and I proposed ACCEPTING it. 🔴 The user declined, and was
        //   right: the layout fact (a Line cannot host BoxAnchorId) argued against storing the identity
        //   INSIDE a Line — not against clickable lines. ⭐ CE-259ac is now FIXED via an oriented-box
        //   hit-test + DebugPrimitive.MakePickSegment; see SC-GZ026-5..5e below.
        //   ⭐ These first rails stay on Box2D, which is the right shape for an extent test.

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
            Assert.Equal(AnchorNetId, hit!.Value);
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

        // ✅✅ SC-GZ026-2b REPLACED 2026-09-11 — CE-259ac is FIXED, so the pin that said "a Line is not
        //   pickable" is gone and the rails below assert the capability instead.
        //   🔒 User ruling: "what is the issue with clickability of something as simple as a line?
        //     I do not want to accept it." ⇒ and the honest answer was: nothing. The hit-test had only
        //     ever implemented two shapes and never learned rotation.
        //   ⭐ A clickable segment is a THIN ORIENTED BOX (DebugPrimitive.MakePickSegment) — the same
        //     pattern EntityPresentationGizmoShared.EmitPickBox already uses for a point, generalised.

        // SC-GZ026-5: 🔴🔴 THE ONE THAT DISCRIMINATES — a click on a diagonal segment AWAY FROM ITS
        //   MIDPOINT picks it.
        //   ⛔ RED-PROOF SHAPE (verified 2026-09-11): set `rad = 0f` in the oriented-box branch of
        //      FindTopmostInteractivePrimitive and BOTH probes below MISS.
        //   📌 And here is what the old bug actually WAS, which my first version of this rail got
        //      wrong: an un-rotated reading of a segment box does NOT give "the segment's bounding
        //      square". MakePickSegment yields extents (length/2, thickness/2) = (70.7, 1) here, so
        //      ignoring the angle leaves a 141-long, 2-thick corridor ALONG THE X AXIS through the
        //      midpoint — whatever direction the segment actually runs. ⇒ you could only ever click a
        //      diagonal near its middle, and only horizontally. That is why probing the MIDPOINT
        //      (50,50) proves nothing: it hits either way. These probes are off-centre on purpose.
        [Theory]
        [InlineData(75f, 75f)]
        [InlineData(25f, 25f)]
        public void SC_GZ026_5_PickSegment_OffCentreOnTheLine_IsHit(float x, float y)
        {
            var p = DebugPrimitive.MakePickSegment(
                new Vector2(0f, 0f), new Vector2(100f, 100f),
                networkId: 90210L, pickThickness: 2f,
                sizeMode: SizeMode.WorldMeters);

            var hit = Pick(p, new Vector2(x, y));

            Assert.NotNull(hit);
            Assert.Equal(90210L, hit!.Value);
        }

        // SC-GZ026-5b: and a click well off the diagonal misses — the corridor is narrow, not a square.
        // ⚠ NOT a red-proof: these miss with or without the rotation (see SC-GZ026-5's note). They pin
        //   the thickness, which is worth pinning on its own.
        [Fact]
        public void SC_GZ026_5b_PickSegment_OffTheLine_IsMiss()
        {
            var p = DebugPrimitive.MakePickSegment(
                new Vector2(0f, 0f), new Vector2(100f, 100f),
                networkId: 90210L, pickThickness: 2f,
                sizeMode: SizeMode.WorldMeters);

            Assert.Null(Pick(p, new Vector2(95f, 5f)));    // bounding-box corner, far from the line
            Assert.Null(Pick(p, new Vector2(5f, 95f)));    // and the other one
        }

        // SC-GZ026-5c: the segment's identity and sub-element survive — so a route can tell WHICH edge
        // was clicked, which is the whole point of making them clickable.
        [Fact]
        public void SC_GZ026_5c_PickSegment_CarriesIdentityAndSubElement()
        {
            var p = DebugPrimitive.MakePickSegment(
                new Vector2(0f, 0f), new Vector2(10f, 0f),
                networkId: 90210L, subElementId: 7);

            var token = GizmoMap.Presentation.DebugGizmoLayer.MakePickToken(in p);

            Assert.Equal(90210L, token.AnchorId);     // the S5 identity, full 64-bit
            Assert.Equal(7u,     token.SubElementId); // which segment
        }

        // SC-GZ026-5d: the geometry is honest — a click beyond an endpoint misses, and the pick
        // corridor is as narrow as asked. ⭐ Paired with 5 so neither "always hits" nor "always misses"
        // can pass.
        [Fact]
        public void SC_GZ026_5d_PickSegment_RespectsLengthAndThickness()
        {
            var p = DebugPrimitive.MakePickSegment(
                new Vector2(0f, 0f), new Vector2(100f, 0f),
                networkId: 90210L, pickThickness: 2f,
                sizeMode: SizeMode.WorldMeters);

            Assert.NotNull(Pick(p, new Vector2(50f, 0f)));    // on it
            Assert.Null(Pick(p, new Vector2(140f, 0f)));      // past the end, beyond the grace radius
            Assert.Null(Pick(p, new Vector2(50f, 40f)));      // well off to the side
        }

        // SC-GZ026-5e: ⭐ a zero-angle segment behaves EXACTLY as the old axis-aligned test did —
        // the rotation term must be a generalisation, not a behaviour change for existing boxes.
        [Fact]
        public void SC_GZ026_5e_ZeroAngleBox_IsUnchangedByTheOrientedTest()
        {
            var p = Anchored(DebugPrimitiveShape.Box2D, SizeMode.WorldMeters);
            p.BoxCenterX = 50f;
            p.BoxCenterY = 0f;
            p.BoxExtentX = 8f;
            p.BoxExtentY = 8f;
            p.BoxAngleDeg = 0f;

            Assert.NotNull(Pick(p, new Vector2(54f, 4f)));    // inside the extent
            Assert.Null(Pick(p, new Vector2(80f, 0f)));       // outside it
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
