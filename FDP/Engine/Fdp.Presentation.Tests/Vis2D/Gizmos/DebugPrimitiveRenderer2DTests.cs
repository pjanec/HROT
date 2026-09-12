using Fdp.Presentation.Tests.Vis2D;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using Raylib_cs;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    // 🔴 CapturingRenderer2D and GeomScaleCapturingRenderer2D MOVED 2026-09-10 to
    //   Vis2D/Gizmos/CapturingRenderers.cs, and re-based onto the REAL renderer's seam.
    //   ⛔ They used to subclass the 43-line Fdp wrapper and override a DispatchShape hook it had
    //     invented, which fired BEFORE any filtering and left Raylib running underneath ⇒ these rails
    //     were vacuous where they passed and SIGSEGV where a primitive reached a draw call.
    //   📄 docs/DESIGN_Gizmo_Renderer_Seam.md §2 rows ②③④ · defect E1 · CE-259aa.

    // ---------------------------------------------------------------------------
    // Helper factories shared by tests in this file.
    // ---------------------------------------------------------------------------
    internal static class RenderTestHelpers
    {
        /// <summary>Builds a RenderContext with the given zoom and layer mask.</summary>
        public static RenderContext MakeCtx(float zoom = 1f, uint layerMask = 0xFFFF_FFFFu)
        {
            return new RenderContext { Zoom = zoom, VisibleLayersMask = layerMask,
                                       // 91d: production ALWAYS supplies a provider (MapCanvas:119).
                                       Resources = HeadlessResourceProvider.Instance };
        }

        /// <summary>Creates a Map2D Line primitive on the given layer / ZIndex.</summary>
        public static DebugPrimitive MakeLine(
            byte layer = 0, byte zIndex = 0,
            PipelineTarget target = PipelineTarget.Map2D)
        {
            var p = DebugPrimitive.MakeLine(Vector3.Zero, Vector3.One, Rgba32.White);
            p.TargetView = target;
            p.DebugLayer = layer;
            p.ZIndex     = zIndex;
            return p;
        }

        /// <summary>Creates a Map2D Line primitive with specific LOD limits.</summary>
        public static DebugPrimitive MakeLineLod(byte minLod, byte maxLod)
        {
            var p = MakeLine();
            p.MinZoomLod = minLod;
            p.MaxZoomLod = maxLod;
            return p;
        }
    }

    // ── GZ011 — filtering and sorting ─────────────────────────────────────────

    public class DebugPrimitiveRenderer2DTests
    {
        // SC-GZ011-1: TargetView=None => skipped.
        [Fact]
        public void SC_GZ011_1_TargetView_None_Skipped()
        {
            var renderer = new CapturingRenderer2D();
            var prim = RenderTestHelpers.MakeLine(target: PipelineTarget.None);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
        }

        // ⭐⭐⭐ SC-GZ011-2/3 RE-HOMED 2026-09-10 (R2, DESIGN_Gizmo_Renderer_Seam.md §6).
        //   ⛔ They used to call `renderer.SetLayerMask(...)` on the Fdp WRAPPER — a method with an
        //     EMPTY BODY. So neither rail ever set a mask, and SC-GZ011-3 "passed" only because the
        //     wrapper's pre-filter hook happened to give the count it wanted for other reasons.
        //   🔒 The authority is the BACKEND'S `LayerControlMask` PRIMITIVE, by ruling:
        //     docs/UX/Architect_Question_28_Map_Layers.md:20 — "the only filter that reaches drawn
        //     primitives. Default SetAll(); a backend LayerControlMask primitive asserts authority for
        //     the frame." ⇒ these now assert THAT, which is the mechanism the product actually uses.

        // SC-GZ011-2: layer-5 primitive, bit 5 SET in the frame's LayerControlMask => dispatched.
        [Fact]
        public void SC_GZ011_2_Layer5_MaskBitSet_Dispatched()
        {
            var renderer = new CapturingRenderer2D();
            var mask = new LayerMask256();
            mask.SetAll();
            var prim = RenderTestHelpers.MakeLine(layer: 5);

            renderer.Render(
                new[] { DebugPrimitive.MakeLayerControlMask(mask), prim },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
        }

        // SC-GZ011-3: layer-5 primitive, bit 5 CLEAR => skipped.
        // ⛔ RED-PROOF SHAPE: this is the rail that was impossible before — with the no-op SetLayerMask
        //    there was no way to express "bit 5 off" at all.
        [Fact]
        public void SC_GZ011_3_Layer5_MaskBitClear_Skipped()
        {
            var renderer = new CapturingRenderer2D();
            // ⭐ Build UP from zero — LayerMask256 has SetAll/SetBit/IsSet and deliberately no ClearBit;
            //   the backend asserts which layers ARE visible, it does not subtract.
            var mask = new LayerMask256();
            mask.SetBit(0);
            mask.SetBit(4);
            mask.SetBit(6);      // ⛔ 5 is NOT set
            var prim = RenderTestHelpers.MakeLine(layer: 5);

            renderer.Render(
                new[] { DebugPrimitive.MakeLayerControlMask(mask), prim },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
        }

        // SC-GZ011-3c: the mask is indexed BY LAYER, not merely on/off. Only bit 5 set ⇒ the layer-5
        // primitive draws and the layer-0 one does not. ⭐ This is the assertion the no-op SetLayerMask
        // made unreachable, and it is the one that would catch an off-by-one in the bit indexing.
        [Fact]
        public void SC_GZ011_3c_OnlyBit5Set_Layer5Draws_Layer0Skipped()
        {
            var renderer = new CapturingRenderer2D();
            var mask = new LayerMask256();
            mask.SetBit(5);

            renderer.Render(
                new[]
                {
                    DebugPrimitive.MakeLayerControlMask(mask),
                    RenderTestHelpers.MakeLine(layer: 0),
                    RenderTestHelpers.MakeLine(layer: 5),
                },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal((byte)5, renderer.Dispatched[0].DebugLayer);
        }

        // SC-GZ011-3b: absent LayerControlMask => the renderer defaults to ALL layers visible.
        // 🔒 docs/projects/FDP/ExtDeps/GizmoMap/GizmoMap.Example.md:517-518 — "Emit LayerControlMask
        //    every frame. The renderer treats each frame as authoritative. If LayerControlMask is
        //    absent, the renderer defaults to all layers visible."
        [Fact]
        public void SC_GZ011_3b_NoLayerControlMask_DefaultsToAllVisible()
        {
            var renderer = new CapturingRenderer2D();

            renderer.Render(new[] { RenderTestHelpers.MakeLine(layer: 5) }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
        }

        // SC-GZ011-6: ZIndex 1 pushed before ZIndex 0; after Render, ZIndex 0 is dispatched first.
        [Fact]
        public void SC_GZ011_6_SameLayer_ZIndex_SortedAscending()
        {
            var renderer = new CapturingRenderer2D();
            var p1 = RenderTestHelpers.MakeLine(layer: 0, zIndex: 1);
            var p2 = RenderTestHelpers.MakeLine(layer: 0, zIndex: 0);

            renderer.Render(new[] { p1, p2 }, RenderTestHelpers.MakeCtx());

            Assert.Equal(2, renderer.Dispatched.Count);
            Assert.Equal(0, renderer.Dispatched[0].ZIndex);
            Assert.Equal(1, renderer.Dispatched[1].ZIndex);
        }

        // SC-GZ011-7: MinZoomLod=8 (threshold 2.0f). zoom=1.0 => skipped; zoom=3.0 => dispatched.
        [Fact]
        public void SC_GZ011_7_MinZoomLod_CullsAtLowZoom()
        {
            var renderer = new CapturingRenderer2D();
            var prim = RenderTestHelpers.MakeLineLod(minLod: 8, maxLod: 0);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx(zoom: 1.0f));
            Assert.Equal(0, renderer.Dispatched.Count);

            renderer.Dispatched.Clear();

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx(zoom: 3.0f));
            Assert.Equal(1, renderer.Dispatched.Count);
        }

        // SC-GZ011-8: MaxZoomLod=8 (threshold 2.0f). zoom=1.0 => dispatched; zoom=3.0 => skipped.
        [Fact]
        public void SC_GZ011_8_MaxZoomLod_CullsAtHighZoom()
        {
            var renderer = new CapturingRenderer2D();
            var prim = RenderTestHelpers.MakeLineLod(minLod: 0, maxLod: 8);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx(zoom: 1.0f));
            Assert.Equal(1, renderer.Dispatched.Count);

            renderer.Dispatched.Clear();

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx(zoom: 3.0f));
            Assert.Equal(0, renderer.Dispatched.Count);
        }

        // SC-GZ011-9: Both LOD limits = 0 => never culled at any zoom.
        [Fact]
        public void SC_GZ011_9_ZeroLodLimits_NeverCulled()
        {
            var renderer = new CapturingRenderer2D();
            var prim = RenderTestHelpers.MakeLineLod(minLod: 0, maxLod: 0);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx(zoom: 0.001f));
            Assert.Equal(1, renderer.Dispatched.Count);

            renderer.Dispatched.Clear();

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx(zoom: 1000f));
            Assert.Equal(1, renderer.Dispatched.Count);
        }
    }

    // ── GZ012 / GZ027 — EntityLocal resolution ────────────────────────────────
    //
    // 🔴🔴 REWRITTEN 2026-09-10 (R4 + constraint C4, docs/DESIGN_Gizmo_Renderer_Seam.md §6).
    //   ⛔ These seven rails built an `EntityRepository`, stamped `AnchorIndex`/`AnchorGeneration` as an
    //     ECS handle, and expected the renderer to resolve `SimTransform` and skip dead entities. That
    //     is a RETIRED mechanism, and the design record shows it superseded TWICE:
    //       ① `.dev/_DONE/gizmos-1/design-talk.md:2778-2791` — add an `Entity Anchor` field, resolve
    //          SimTransform via the ECS;
    //       ② `feedback2.md:479` — "the DebugPrimitiveRenderer2D must STOP CASTING RAW NETWORK INTEGERS
    //          INTO LOCAL Entity HANDLES"; resolve through NetworkEntityMap instead;
    //       ③ 🔒 `feedback2.md:742-798` — the SpatialAnchor TWO-PASS, which "completely severs the
    //          presentation layer's reliance on the heavy simulation ECS (like SimTransform or
    //          NetworkEntityMap)", and `:871` "Eradicating Entity".
    //   ⭐⭐ Mechanism ③ is what is BUILT, and it has production producers:
    //     `EntityPresentationGizmo.cs:95` → `EntityPresentationGizmoShared.cs:18`
    //     `draw.DrawSpatialAnchor(networkId, …)`, cached at `DebugPrimitiveRenderer2D.cs:63` and
    //     resolved at `:102-106`. ⇒ 🔴 `CE-259y`'s premise — "the whole EntityLocal path is inert
    //     because the wrapper discards its ISimulationView" — is REFUTED: no view is wanted.
    //   ⭐ So these now drive the real mechanism, and they are the only rails that cover it here.

    public class DebugPrimitiveRenderer2DEntityLocalTests
    {
        /// <summary>The anchor the backend prepends for any entity emitting local graphics.</summary>
        private static DebugPrimitive Anchor(long networkId, float x, float y, float headingDeg = 0f)
            => DebugPrimitive.MakeSpatialAnchor(networkId, x, y, 0f, headingDeg, target: PipelineTarget.Map2D);

        /// <summary>
        /// An EntityLocal primitive keyed to <paramref name="networkId"/>. ⭐ Offset 8 carries the
        /// SpatialAnchor cache KEY here, not an ECS index — exactly what
        /// <c>DebugPrimitiveBuffer.DrawSemanticShape</c> writes (`(int)networkId`), and the 32-bit
        /// narrowing that constraint C7 / CE-259z pins.
        /// </summary>
        private static DebugPrimitive Local(DebugPrimitiveShape shape, long networkId)
        {
            var p = default(DebugPrimitive);
            p.Shape       = shape;
            p.Space       = CoordinateSpace.EntityLocal;
            p.Color       = Rgba32.Red;
            p.TargetView  = PipelineTarget.Map2D;
            p.AnchorIndex = (int)networkId;
            return p;
        }

        // SC-GZ012-1: an EntityLocal Line resolves to anchor position + local offset.
        [Fact]
        public void SC_GZ012_1_EntityLocal_Line_TranslatesPosition()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Line, 42L);
            p.LineStart = new Vector3(1f, 0f, 0f);
            p.LineEnd   = new Vector3(2f, 0f, 0f);

            renderer.Render(new[] { Anchor(42L, 10f, 20f), p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(11f, renderer.Dispatched[0].LineStart.X, precision: 3);
            Assert.Equal(20f, renderer.Dispatched[0].LineStart.Y, precision: 3);
            // ⭐ and the space is REWRITTEN to World, which is how the draw path knows it is resolved.
            Assert.Equal(CoordinateSpace.World, renderer.Dispatched[0].Space);
        }

        // SC-GZ012-2: an EntityLocal primitive whose anchor is ABSENT from the frame is SKIPPED.
        // ⭐⭐ This is the live equivalent of the old "dead entity" rail: the backend stops emitting the
        //   SpatialAnchor when the entity goes away, so absence IS the liveness signal.
        // ⛔ RED-PROOF SHAPE: drop the `continue` at DebugPrimitiveRenderer2D.cs:106 and an unresolved
        //    primitive draws at its raw LOCAL coordinates — i.e. near the world origin.
        [Fact]
        public void SC_GZ012_2_EntityLocal_MissingAnchor_Skipped()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Line, 42L);
            p.LineStart = Vector3.Zero;
            p.LineEnd   = Vector3.One;

            // No SpatialAnchor for 42 in this frame.
            renderer.Render(new[] { p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
        }

        // SC-GZ012-2b: an anchor for a DIFFERENT network id does not resolve this one. ⭐ Pins that the
        // cache is keyed, not merely present/absent.
        [Fact]
        public void SC_GZ012_2b_EntityLocal_WrongAnchorId_Skipped()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Line, 42L);
            p.LineStart = Vector3.Zero;
            p.LineEnd   = Vector3.One;

            renderer.Render(new[] { Anchor(999L, 10f, 20f), p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
        }
    }

    // ── CE-259z — DrawEntityLocal end-to-end against the renderer ─────────────

    public class DrawEntityLocalResolvesEndToEndTests
    {
        // ⭐⭐⭐ THE RAIL THAT WOULD HAVE CAUGHT CE-259z, and nothing had it: drive the PRODUCTION
        //   helper into the PRODUCTION renderer and check the primitive actually arrives.
        //   ⛔ RED-PROOF SHAPE: restore `p.AnchorIndex = anchor.Index` in
        //      DebugPrimitiveBuffer.DrawEntityLocal and pass an entity whose index differs from its
        //      network id — the cache lookup misses and Dispatched is EMPTY. That was the shipped
        //      behaviour: drawn nowhere, no error.
        [Fact]
        public void CE259z_DrawEntityLocal_ResolvesAgainstItsSpatialAnchor()
        {
            const long netId = 90210L;
            var buffer = new DebugPrimitiveBuffer(16);
            buffer.DrawSpatialAnchor(netId, worldX: 10f, worldY: 20f, worldZ: 0f, headingDeg: 0f);
            buffer.DrawEntityLocal(netId, new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), Rgba32.Red);

            var renderer = new CapturingRenderer2D();
            renderer.Render(buffer.GetFrame(), RenderTestHelpers.MakeCtx());

            // The anchor is not dispatched (it is frame state), the line is — resolved to world.
            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(CoordinateSpace.World, renderer.Dispatched[0].Space);
            Assert.Equal(11f, renderer.Dispatched[0].LineStart.X, precision: 3);
            Assert.Equal(20f, renderer.Dispatched[0].LineStart.Y, precision: 3);
        }

        // CE-259z-b: DrawEntityLocalInteractive sets the cache key and the sub-element, and 🔒 CANNOT
        // set an identity: for a Line, BoxAnchorId (long @44-51) OVERLAPS LineEnd.Z (@44-47) and
        // EndColor (@48-51). ⭐⭐ That is the STRUCTURAL reason behind CE-259ac — stronger than "the
        // hit-test does not handle Line" — and it means the row cannot be closed by teaching the
        // hit-test about lines: the primitive could not carry what it routes on.
        // 📌 Found by trying it: the first version of this rail stamped BoxAnchorId and read back 0,
        //    because `p.LineEnd = localEnd` had overwritten it.
        [Fact]
        public void CE259z_DrawEntityLocalInteractive_HasNoRoomForAnIdentity()
        {
            const long netId = 90210L;
            var buffer = new DebugPrimitiveBuffer(16);
            buffer.DrawEntityLocalInteractive(netId, Vector3.Zero, new Vector3(1f, 2f, 3f), Rgba32.Red,
                subElementId: 3);

            var prim = buffer.GetFrame()[0];
            Assert.Equal((int)netId, prim.AnchorIndex);    // the SpatialAnchor cache key
            Assert.Equal((ushort)3,  prim.SubElementId);    // offset 52 — unaffected by the overlap

            // ⭐ The geometry is intact precisely BECAUSE no identity was written over it.
            Assert.Equal(3f, prim.LineEnd.Z, precision: 3);

            // 🔒 And the overlap is real, not folklore — write the identity and the geometry dies.
            var corrupted = prim;
            corrupted.BoxAnchorId = netId;
            Assert.NotEqual(3f, corrupted.LineEnd.Z);
        }
    }

    // ── GZ027 — EntityLocal rendering for all shapes ──────────────────────────

    public class DebugPrimitiveRenderer2DEntityLocalAllShapesTests
    {
        private static DebugPrimitive Anchor(long networkId, Vector3 pos, float headingDeg = 0f)
            => DebugPrimitive.MakeSpatialAnchor(networkId, pos.X, pos.Y, pos.Z, headingDeg,
                                                target: PipelineTarget.Map2D);

        private static DebugPrimitive Local(DebugPrimitiveShape shape, long networkId = 7L)
        {
            var p = default(DebugPrimitive);
            p.Shape       = shape;
            p.Space       = CoordinateSpace.EntityLocal;
            p.TargetView  = PipelineTarget.Map2D;
            p.AnchorIndex = (int)networkId;
            return p;
        }

        // SC-GZ027-1: an EntityLocal Sphere at local (5,0,0) renders at anchor + (5,0,0).
        [Fact]
        public void SC_GZ027_1_EntityLocal_Sphere_TranslatesCenter()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Sphere);
            p.SphereCenter = new Vector3(5f, 0f, 0f);
            p.SphereRadius = 1f;

            renderer.Render(
                new[] { Anchor(7L, new Vector3(10f, 20f, 0f)), p },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(15f, renderer.Dispatched[0].SphereCenter.X, precision: 3);
            Assert.Equal(20f, renderer.Dispatched[0].SphereCenter.Y, precision: 3);
        }

        // SC-GZ027-2: an EntityLocal Arrow ROTATES with the anchor's heading. A 90° anchor turns a
        // local +X arrow into a world +Y one.
        [Fact]
        public void SC_GZ027_2_EntityLocal_Arrow_RotatesWithAnchorHeading()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Arrow);
            p.ArrowFrom = new Vector3(0f, 0f, 0f);
            p.ArrowTo   = new Vector3(10f, 0f, 0f);

            renderer.Render(
                new[] { Anchor(7L, Vector3.Zero, headingDeg: 90f), p },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(0f,  renderer.Dispatched[0].ArrowTo.X, precision: 3);
            Assert.Equal(10f, renderer.Dispatched[0].ArrowTo.Y, precision: 3);
        }

        // SC-GZ027-3: a Box2D's CENTRE translates with the anchor; its extents do not.
        [Fact]
        public void SC_GZ027_3_EntityLocal_Box2D_TranslatesCenter_NotExtents()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Box2D);
            p.BoxCenterX = 2f;
            p.BoxCenterY = 3f;
            p.BoxExtentX = 4f;
            p.BoxExtentY = 5f;

            renderer.Render(
                new[] { Anchor(7L, new Vector3(100f, 200f, 0f)), p },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(102f, renderer.Dispatched[0].BoxCenterX, precision: 3);
            Assert.Equal(203f, renderer.Dispatched[0].BoxCenterY, precision: 3);
            Assert.Equal(4f,   renderer.Dispatched[0].BoxExtentX, precision: 3);
            Assert.Equal(5f,   renderer.Dispatched[0].BoxExtentY, precision: 3);
        }

        // SC-GZ027-4: Text position translates with the anchor.
        [Fact]
        public void SC_GZ027_4_EntityLocal_Text_TranslatesPosition()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Text);
            p.TextX = 1f;
            p.TextY = 2f;

            renderer.Render(
                new[] { Anchor(7L, new Vector3(30f, 40f, 0f)), p },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(31f, renderer.Dispatched[0].TextX, precision: 3);
            Assert.Equal(42f, renderer.Dispatched[0].TextY, precision: 3);
        }

        // SC-GZ027-5: a zero-heading anchor translates without rotating — the identity case, which is
        // what catches a rotation applied in the wrong direction or in the wrong units.
        [Fact]
        public void SC_GZ027_5_EntityLocal_ZeroHeading_TranslatesOnly()
        {
            var renderer = new CapturingRenderer2D();
            var p = Local(DebugPrimitiveShape.Line);
            p.LineStart = new Vector3(1f, 2f, 0f);
            p.LineEnd   = new Vector3(3f, 4f, 0f);

            renderer.Render(
                new[] { Anchor(7L, new Vector3(10f, 10f, 0f), headingDeg: 0f), p },
                RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(11f, renderer.Dispatched[0].LineStart.X, precision: 3);
            Assert.Equal(12f, renderer.Dispatched[0].LineStart.Y, precision: 3);
            Assert.Equal(13f, renderer.Dispatched[0].LineEnd.X, precision: 3);
            Assert.Equal(14f, renderer.Dispatched[0].LineEnd.Y, precision: 3);
        }
    }

    // ── GZ028 — SizeMode.ScreenPixels scales geom dimensions ─────────────────

    // (GeomScaleCapturingRenderer2D also lives in Vis2D/Gizmos/CapturingRenderers.cs now.)

    public class DebugPrimitiveRenderer2DSizeModeTests
    {
        // SC-GZ028-1: Sphere with SizeMode.ScreenPixels at zoom=1 keeps radius 10; at zoom=2 -> 5.
        [Fact]
        public void SC_GZ028_1_Sphere_ScreenPixels_ScalesRadiusWithZoom()
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.Sphere;
            p.SizeMode     = SizeMode.ScreenPixels;
            p.TargetView   = PipelineTarget.Map2D;
            p.SphereRadius = 10f;

            var r1 = new GeomScaleCapturingRenderer2D();
            r1.Render(new[] { p }, RenderTestHelpers.MakeCtx(zoom: 1f));
            Assert.Equal(10f, r1.Records[0].EffectiveRadius, precision: 3);

            var r2 = new GeomScaleCapturingRenderer2D();
            r2.Render(new[] { p }, RenderTestHelpers.MakeCtx(zoom: 2f));
            Assert.Equal(5f, r2.Records[0].EffectiveRadius, precision: 3);
        }

        // SC-GZ028-2: Sphere with SizeMode.WorldMeters at zoom=2 keeps radius 10 unchanged.
        [Fact]
        public void SC_GZ028_2_Sphere_WorldMeters_RadiusUnchangedAtHighZoom()
        {
            var p = default(DebugPrimitive);
            p.Shape        = DebugPrimitiveShape.Sphere;
            p.SizeMode     = SizeMode.WorldMeters;
            p.TargetView   = PipelineTarget.Map2D;
            p.SphereRadius = 10f;

            var r = new GeomScaleCapturingRenderer2D();
            r.Render(new[] { p }, RenderTestHelpers.MakeCtx(zoom: 2f));

            Assert.Equal(10f, r.Records[0].EffectiveRadius, precision: 3);
        }

        // SC-GZ028-3: Arrow with SizeMode.ScreenPixels, ArrowHeadSize=8 at zoom=4 -> headSize=2.
        [Fact]
        public void SC_GZ028_3_Arrow_ScreenPixels_ScalesHeadSize()
        {
            var p = default(DebugPrimitive);
            p.Shape         = DebugPrimitiveShape.Arrow;
            p.SizeMode      = SizeMode.ScreenPixels;
            p.TargetView    = PipelineTarget.Map2D;
            p.ArrowHeadSize = 8f;

            var r = new GeomScaleCapturingRenderer2D();
            r.Render(new[] { p }, RenderTestHelpers.MakeCtx(zoom: 4f));

            Assert.Equal(2f, r.Records[0].EffectiveHeadSize, precision: 3);
        }

        // SC-GZ028-4: Box2D with SizeMode.ScreenPixels, extents (20,15) at zoom=2 -> (10, 7.5).
        [Fact]
        public void SC_GZ028_4_Box2D_ScreenPixels_ScalesExtents()
        {
            var p = default(DebugPrimitive);
            p.Shape      = DebugPrimitiveShape.Box2D;
            p.SizeMode   = SizeMode.ScreenPixels;
            p.TargetView = PipelineTarget.Map2D;
            p.BoxExtentX = 20f;
            p.BoxExtentY = 15f;

            var r = new GeomScaleCapturingRenderer2D();
            r.Render(new[] { p }, RenderTestHelpers.MakeCtx(zoom: 2f));

            Assert.Equal(10f,  r.Records[0].EffectiveExtentX, precision: 3);
            Assert.Equal(7.5f, r.Records[0].EffectiveExtentY, precision: 3);
        }
    }
}

