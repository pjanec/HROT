// SC-GZ026: Geometry-aware hit-testing in DebugGizmoLayer.
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Vis2D.Tests.Gizmos;
using Raylib_cs;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    public class DebugGizmoLayerHitTests
    {
        private static Entity DummyAnchor => new Entity(99, 1);

        private static RenderContext MakeCtx(float zoom = 1f)
        {
            var cam = new Camera2D { Zoom = zoom, Target = Vector2.Zero, Offset = Vector2.Zero };
            return new RenderContext { Camera = cam, VisibleLayersMask = 0xFFFF_FFFFu };
        }

        private static DebugGizmoLayer MakeLayer(DebugPrimitive prim, out FdpEventBus bus, float zoom = 1f)
        {
            bus = new FdpEventBus();
            var buf = new DebugPrimitiveBuffer(8);
            buf.Append(prim);
            var renderer = new CapturingRenderer2D();
            var layer = new DebugGizmoLayer(31, buf, bus, renderer);
            layer.Draw(MakeCtx(zoom));
            return layer;
        }

        // SC-GZ026-1: Click on the midpoint of a 100-unit Line primitive triggers a hit.
        [Fact]
        public void SC_GZ026_1_LineMidpoint_IsHit()
        {
            var anchor = DummyAnchor;
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.WorldMeters;
            p.TargetView       = PipelineTarget.Map2D;
            p.LineStart        = new Vector3(0f, 0f, 0f);
            p.LineEnd          = new Vector3(100f, 0f, 0f);
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;

            var layer = MakeLayer(p, out _);
            // Midpoint at (50, 0) — exactly on the segment.
            bool hit = layer.HandleInput(new Vector2(50f, 0f), MouseButton.Left, isPressed: true);

            Assert.True(hit);
        }

        // SC-GZ026-2: Click 10 units beyond the endpoint of a 100-unit Line misses.
        [Fact]
        public void SC_GZ026_2_BeyondEndpoint_IsMiss()
        {
            var anchor = DummyAnchor;
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.WorldMeters;
            p.TargetView       = PipelineTarget.Map2D;
            p.LineStart        = new Vector3(0f, 0f, 0f);
            p.LineEnd          = new Vector3(100f, 0f, 0f);
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;

            var layer = MakeLayer(p, out _);
            // 10 units beyond endpoint: (110, 0). HitRadiusWorld = 5, so no hit.
            bool hit = layer.HandleInput(new Vector2(110f, 0f), MouseButton.Left, isPressed: true);

            Assert.False(hit);
        }

        // SC-GZ026-3: Click within SphereRadius of a Sphere center triggers a hit.
        [Fact]
        public void SC_GZ026_3_SphereCenter_IsHit()
        {
            var anchor = DummyAnchor;
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Sphere;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.WorldMeters;
            p.TargetView       = PipelineTarget.Map2D;
            p.SphereCenter     = new Vector3(20f, 30f, 0f);
            p.SphereRadius     = 8f;
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;

            var layer = MakeLayer(p, out _);
            // Click exactly at sphere center.
            bool hit = layer.HandleInput(new Vector2(20f, 30f), MouseButton.Left, isPressed: true);

            Assert.True(hit);
        }

        // SC-GZ026-4: With SizeMode.ScreenPixels at zoom=2, the effective hit radius is halved.
        // A point 4 world-units away from a line should hit when HitRadius=5 and zoom=2
        // (effectiveRadius = 5/2 = 2.5 < 4 → miss); but a point 2 world-units away should hit.
        [Fact]
        public void SC_GZ026_4_ScreenPixels_ZoomScalesHitRadius()
        {
            var anchor = DummyAnchor;

            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Line;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.ScreenPixels;  // zoom-affected hit radius
            p.TargetView       = PipelineTarget.Map2D;
            p.LineStart        = new Vector3(0f, 0f, 0f);
            p.LineEnd          = new Vector3(100f, 0f, 0f);
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;

            // zoom=2 => effectiveRadius = 5/2 = 2.5
            var layer = MakeLayer(p, out _, zoom: 2f);

            // 2 world units from the line (perpendicular) => hit (2 <= 2.5).
            bool hit2 = layer.HandleInput(new Vector2(50f, 2f), MouseButton.Left, isPressed: true);
            Assert.True(hit2);

            // Must recreate layer because it consumed the hit (buffer unchanged, just re-test).
            var layer2 = MakeLayer(p, out _, zoom: 2f);
            // 4 world units from the line => miss (4 > 2.5).
            bool miss4 = layer2.HandleInput(new Vector2(50f, 4f), MouseButton.Left, isPressed: true);
            Assert.False(miss4);
        }
    }
}
