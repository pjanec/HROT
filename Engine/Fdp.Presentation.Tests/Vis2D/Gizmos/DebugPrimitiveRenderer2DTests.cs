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
            var cam = new Camera2D { Zoom = zoom };
            return new RenderContext { Camera = cam, VisibleLayersMask = layerMask };
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
}

