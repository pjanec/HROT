using Fdp.Presentation.Tests.Vis2D;
// SC-GZ025: DebugGizmoLayer starts an interaction on pickable primitive hit.
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Vis2D.Tests.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    public class DebugGizmoLayerActivationTests
    {
        // Build a RenderContext with identity camera so HitTest uses world coords directly.
        private static RenderContext MakeCtx(float zoom = 1f)
        {
            return new RenderContext { Zoom = zoom, VisibleLayersMask = 0xFFFF_FFFFu,
                                       // 91d: production ALWAYS supplies a provider (MapCanvas:119).
                                       Resources = HeadlessResourceProvider.Instance };
        }

        // Build a buffer with a single sphere at the origin that has a valid PickToken.
        private static DebugPrimitiveBuffer MakeSphereBuffer(Entity anchor)
        {
            var buf = new DebugPrimitiveBuffer(16);
            var p = default(DebugPrimitive);
            p.Shape            = DebugPrimitiveShape.Sphere;
            p.Space            = CoordinateSpace.World;
            p.SizeMode         = SizeMode.WorldMeters;
            p.TargetView       = PipelineTarget.Map2D;
            p.SphereCenter     = new Vector3(0f, 0f, 0f);
            p.SphereRadius     = 5f;
            p.AnchorIndex      = anchor.Index;
            p.AnchorGeneration = anchor.Generation;
            buf.Append(p);
            return buf;
        }

        // SC-GZ025-1: Clicking on a pickable primitive starts an interaction (inlined
        // into DebugGizmoLayer; no separate proxy tool pushed on MapCanvas).
        [Fact]
        public void SC_GZ025_1_HitPickable_PushesProxyTool()
        {
            var bus    = new FdpEventBus();
            var anchor = new Entity(1, 1);
            var buf    = MakeSphereBuffer(anchor);
            var layer  = new DebugGizmoLayer(31, buf, bus, new CapturingRenderer2D());

            // Simulate a Draw call so _lastCtx is populated.
            layer.Draw(MakeCtx());

            // Click at the sphere center.
            bool consumed = layer.HandleInput(Vector2.Zero, MapMouseButton.Left, isPressed: true);

            Assert.True(consumed);
            Assert.True(layer.TestHook_IsInteractionActive);
        }

        // SC-GZ025-2: GizmoInteractionStartedEvent is published exactly once when the tool
        // enters (via OnEnter), and contains the correct Token.
        [Fact]
        public void SC_GZ025_2_OnEnter_PublishesStartedEventOnce()
        {
            var bus    = new FdpEventBus();
            var anchor = new Entity(2, 1);
            var buf    = MakeSphereBuffer(anchor);
            var layer  = new DebugGizmoLayer(31, buf, bus, new CapturingRenderer2D());

            layer.Draw(MakeCtx());
            layer.HandleInput(Vector2.Zero, MapMouseButton.Left, isPressed: true);

            // Swap so the event is visible in Read<T>.
            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(anchor, events[0].Token.Target);
        }

        // SC-GZ025-3: Clicking outside any pickable primitive does NOT push a tool.
        [Fact]
        public void SC_GZ025_3_MissedClick_NoToolPushed()
        {
            var bus    = new FdpEventBus();
            var anchor = new Entity(3, 1);
            var buf    = MakeSphereBuffer(anchor);  // sphere at (0,0), radius 5
            var layer  = new DebugGizmoLayer(31, buf, bus, new CapturingRenderer2D());

            layer.Draw(MakeCtx());

            // Click far away from the sphere.
            bool consumed = layer.HandleInput(new Vector2(100f, 100f), MapMouseButton.Left, isPressed: true);

            Assert.False(consumed);
            Assert.False(layer.TestHook_IsInteractionActive);
        }

        // SC-GZ025-5: When canvas is null (fallback path), GizmoInteractionStartedEvent is
        // still published via the event bus directly.
        [Fact]
        public void SC_GZ025_5_NullCanvas_FallbackPublishesEvent()
        {
            var bus    = new FdpEventBus();
            var anchor = new Entity(5, 1);
            var buf    = MakeSphereBuffer(anchor);
            // No canvas needed after refactor.
            var renderer = new CapturingRenderer2D();            var layer    = new DebugGizmoLayer(31, buf, bus, renderer);

            layer.Draw(MakeCtx());
            layer.HandleInput(Vector2.Zero, MapMouseButton.Left, isPressed: true);

            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(anchor, events[0].Token.Target);
        }

        // Minimal IInputProvider stub: all values default to zero / false.
        private sealed class StubInputProvider : IInputProvider
        {
            public Vector2 MousePosition  => Vector2.Zero;
            public Vector2 MouseDelta     => Vector2.Zero;
            public float MouseWheelMove   => 0f;
            public bool IsMouseCaptured   => false;
            public bool IsKeyboardCaptured => false;
            public bool IsMouseButtonPressed(MapMouseButton b)  => false;
            public bool IsMouseButtonDown(MapMouseButton b)     => false;
            public bool IsMouseButtonReleased(MapMouseButton b) => false;
            public bool IsKeyPressed(MapKeyboardKey k)  => false;
            public bool IsKeyDown(MapKeyboardKey k)     => false;
            public bool IsKeyReleased(MapKeyboardKey k) => false;
            public int GetKeyPressed() => 0;
        }
    }
}
