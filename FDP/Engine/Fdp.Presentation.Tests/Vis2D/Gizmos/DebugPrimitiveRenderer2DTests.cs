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
    // ---------------------------------------------------------------------------
    // Test double: overrides DispatchShape to capture primitives without Raylib.
    // Defined at namespace level so DebugGizmoLayerGizmoTests can also use it.
    // ---------------------------------------------------------------------------
    internal sealed class CapturingRenderer2D : DebugPrimitiveRenderer2D
    {
        public readonly List<DebugPrimitive> Dispatched = new();

        public CapturingRenderer2D(ISimulationView? view = null) : base(view) { }

        protected override void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
            => Dispatched.Add(prim);
    }

    // ---------------------------------------------------------------------------
    // Helper factories shared by tests in this file.
    // ---------------------------------------------------------------------------
    internal static class RenderTestHelpers
    {
        /// <summary>Builds a RenderContext with the given zoom and layer mask.</summary>
        public static RenderContext MakeCtx(float zoom = 1f, uint layerMask = 0xFFFF_FFFFu)
        {
            return new RenderContext { Zoom = zoom, VisibleLayersMask = layerMask };
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

        // SC-GZ011-2: Layer-5 primitive, bit-5 set in mask => dispatched.
        [Fact]
        public void SC_GZ011_2_Layer5_MaskBitSet_Dispatched()
        {
            var renderer = new CapturingRenderer2D();
            renderer.SetLayerMask(0xFFFF);
            var prim = RenderTestHelpers.MakeLine(layer: 5);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
        }

        // SC-GZ011-3: Layer-5 primitive, bit-5 clear in mask => skipped.
        [Fact]
        public void SC_GZ011_3_Layer5_MaskBitClear_Skipped()
        {
            var renderer = new CapturingRenderer2D();
            renderer.SetLayerMask(unchecked((ushort)~(1u << 5))); // bit 5 off
            var prim = RenderTestHelpers.MakeLine(layer: 5);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
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

    // ── GZ012 — EntityLocal resolution ────────────────────────────────────────

    public class DebugPrimitiveRenderer2DEntityLocalTests
    {
        // SC-GZ012-1: EntityLocal Line resolves to entity world position + local offset.
        [Fact]
        public void SC_GZ012_1_EntityLocal_Line_TranslatesPosition()
        {
            // Use EntityRepository as the real ISimulationView.
            var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();

            var entity = world.CreateEntity();
            world.SetComponent(entity, new SimTransform
            {
                Position = new Vector3(10f, 20f, 0f),
                Rotation = Quaternion.Identity,
            });

            var renderer = new CapturingRenderer2D(world);

            var prim = default(DebugPrimitive);
            prim.Shape            = DebugPrimitiveShape.Line;
            prim.Space            = CoordinateSpace.EntityLocal;
            prim.Color            = Rgba32.Red;
            prim.TargetView       = PipelineTarget.Map2D;
            prim.DebugLayer       = 0;
            prim.AnchorIndex      = entity.Index;
            prim.AnchorGeneration = entity.Generation;
            prim.LineStart        = new Vector3(1f, 0f, 0f); // Local offset
            prim.LineEnd          = new Vector3(2f, 0f, 0f);

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            // World start = tf.Position + Transform(localStart, Identity) = (10+1, 20+0, 0)
            Assert.Equal(11f, renderer.Dispatched[0].LineStart.X, precision: 3);
            Assert.Equal(20f, renderer.Dispatched[0].LineStart.Y, precision: 3);
        }

        // SC-GZ012-2: EntityLocal primitive for a non-alive (destroyed) entity is skipped.
        [Fact]
        public void SC_GZ012_2_EntityLocal_DeadEntity_Skipped()
        {
            var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();

            var entity = world.CreateEntity();
            world.SetComponent(entity, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });

            // Destroy the entity so IsAlive returns false.
            world.DestroyEntity(entity);

            var renderer = new CapturingRenderer2D(world);

            var prim = default(DebugPrimitive);
            prim.Shape            = DebugPrimitiveShape.Line;
            prim.Space            = CoordinateSpace.EntityLocal;
            prim.Color            = Rgba32.Red;
            prim.TargetView       = PipelineTarget.Map2D;
            prim.DebugLayer       = 0;
            prim.AnchorIndex      = entity.Index;
            prim.AnchorGeneration = entity.Generation;
            prim.LineStart        = Vector3.Zero;
            prim.LineEnd          = Vector3.One;

            renderer.Render(new[] { prim }, RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
        }
    }

    // ── GZ027 — EntityLocal rendering for all shapes ──────────────────────────

    public class DebugPrimitiveRenderer2DEntityLocalAllShapesTests
    {
        private static (EntityRepository world, Entity entity) MakeWorld(Vector3 pos)
        {
            var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            var entity = world.CreateEntity();
            world.SetComponent(entity, new SimTransform
            {
                Position = pos,
                Rotation = Quaternion.Identity,
            });
            return (world, entity);
        }

        // SC-GZ027-1: EntityLocal Sphere at local offset (5,0,0) renders at entity.Position + (5,0,0).
        [Fact]
        public void SC_GZ027_1_EntityLocal_Sphere_TranslatesCenter()
        {
            var (world, entity) = MakeWorld(new Vector3(10f, 20f, 0f));
            var renderer = new CapturingRenderer2D(world);

            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Sphere;
            p.Space            = CoordinateSpace.EntityLocal;
            p.TargetView       = PipelineTarget.Map2D;
            p.SphereCenter     = new Vector3(5f, 0f, 0f);
            p.SphereRadius     = 1f;
            p.AnchorIndex      = entity.Index;
            p.AnchorGeneration = entity.Generation;

            renderer.Render(new[] { p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            // Expected world center = (10+5, 20+0, 0) = (15, 20, 0)
            Assert.Equal(15f, renderer.Dispatched[0].SphereCenter.X, precision: 3);
            Assert.Equal(20f, renderer.Dispatched[0].SphereCenter.Y, precision: 3);
        }

        // SC-GZ027-2: EntityLocal Arrow rotates with the entity (90 degrees around Z).
        [Fact]
        public void SC_GZ027_2_EntityLocal_Arrow_RotatesWithEntity()
        {
            var world = new EntityRepository();
            world.RegisterComponent<SimTransform>();
            var entity = world.CreateEntity();
            // 90-degree rotation around Z axis.
            var rot90 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
            world.SetComponent(entity, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = rot90,
            });
            var renderer = new CapturingRenderer2D(world);

            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Arrow;
            p.Space            = CoordinateSpace.EntityLocal;
            p.TargetView       = PipelineTarget.Map2D;
            p.ArrowFrom        = new Vector3(0f, 0f, 0f);
            p.ArrowTo          = new Vector3(1f, 0f, 0f); // Local +X
            p.AnchorIndex      = entity.Index;
            p.AnchorGeneration = entity.Generation;

            renderer.Render(new[] { p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            // After 90-deg rotation around Z: local +X maps to world +Y.
            var dispatched = renderer.Dispatched[0];
            Assert.Equal(0f, dispatched.ArrowFrom.X, precision: 3);
            Assert.Equal(0f, dispatched.ArrowFrom.Y, precision: 3);
            // To: rotated (1,0,0) => (0,1,0) in world
            Assert.Equal(0f, dispatched.ArrowTo.X, precision: 3);
            Assert.Equal(1f, dispatched.ArrowTo.Y, precision: 3);
        }

        // SC-GZ027-3: EntityLocal Text at local (0,2,0) renders 2 units above entity position.
        [Fact]
        public void SC_GZ027_3_EntityLocal_Text_TranslatesAnchor()
        {
            var (world, entity) = MakeWorld(new Vector3(0f, 5f, 0f));
            var renderer = new CapturingRenderer2D(world);

            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Text;
            p.Space            = CoordinateSpace.EntityLocal;
            p.TargetView       = PipelineTarget.Map2D;
            p.TextX            = 0f;
            p.TextY            = 2f;   // local Y offset
            p.TextContent      = new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("hi");
            p.AnchorIndex      = entity.Index;
            p.AnchorGeneration = entity.Generation;

            renderer.Render(new[] { p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            // world Y = entity.Y + localY = 5 + 2 = 7
            Assert.Equal(7f, renderer.Dispatched[0].TextY, precision: 3);
            Assert.Equal(0f, renderer.Dispatched[0].TextX, precision: 3);
        }

        // SC-GZ027-4: EntityLocal primitive for a dead entity is silently skipped.
        [Fact]
        public void SC_GZ027_4_EntityLocal_DeadEntity_Skipped()
        {
            var (world, entity) = MakeWorld(Vector3.Zero);
            world.DestroyEntity(entity);
            var renderer = new CapturingRenderer2D(world);

            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Sphere;
            p.Space            = CoordinateSpace.EntityLocal;
            p.TargetView       = PipelineTarget.Map2D;
            p.SphereCenter     = new Vector3(1f, 0f, 0f);
            p.AnchorIndex      = entity.Index;
            p.AnchorGeneration = entity.Generation;

            renderer.Render(new[] { p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(0, renderer.Dispatched.Count);
        }

        // SC-GZ027-5 (regression): Existing EntityLocal Line behaviour still works.
        [Fact]
        public void SC_GZ027_5_EntityLocal_Line_Regression()
        {
            var (world, entity) = MakeWorld(new Vector3(3f, 4f, 0f));
            var renderer = new CapturingRenderer2D(world);

            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.EntityLocal;
            p.TargetView       = PipelineTarget.Map2D;
            p.LineStart        = new Vector3(0f, 0f, 0f);
            p.LineEnd          = new Vector3(1f, 0f, 0f);
            p.AnchorIndex      = entity.Index;
            p.AnchorGeneration = entity.Generation;

            renderer.Render(new[] { p }, RenderTestHelpers.MakeCtx());

            Assert.Equal(1, renderer.Dispatched.Count);
            Assert.Equal(3f, renderer.Dispatched[0].LineStart.X, precision: 3);
            Assert.Equal(4f, renderer.Dispatched[0].LineStart.Y, precision: 3);
            Assert.Equal(4f, renderer.Dispatched[0].LineEnd.X, precision: 3);
        }
    }

    // ── GZ028 — SizeMode.ScreenPixels scales geom dimensions ─────────────────

    // Captures the effective geometric parameters (post geomScale) passed to each dispatch.
    internal sealed class GeomScaleCapturingRenderer2D : DebugPrimitiveRenderer2D
    {
        public record DrawRecord(
            DebugPrimitiveShape Shape,
            float EffectiveRadius,
            float EffectiveHeadSize,
            float EffectiveExtentX,
            float EffectiveExtentY);

        public readonly List<DrawRecord> Records = new();

        public GeomScaleCapturingRenderer2D(ISimulationView? view = null) : base(view) { }

        protected override void DispatchShape(in DebugPrimitive prim, RenderContext ctx)
        {
            float zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
            float gs   = prim.SizeMode == SizeMode.ScreenPixels ? 1f / zoom : 1f;
            Records.Add(new DrawRecord(
                prim.Shape,
                prim.SphereRadius   * gs,
                prim.ArrowHeadSize  * gs,
                prim.BoxExtentX     * gs,
                prim.BoxExtentY     * gs));
        }
    }

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

