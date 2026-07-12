// SC-B28-4 through SC-B28-6: DebugGizmoLayer activates and deactivates capture mode
// in response to exclusive InputCaptureBinding primitives.
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Tests;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    // DebugGizmoLayer.Update() routes through GizmoMap.Presentation.DebugGizmoLayer.HandleInput,
    // which unconditionally reads ImGuiNET.ImGui.GetIO() to respect ImGui's mouse/keyboard
    // capture state. That call requires a current ImGui context (GImGui != null) or the native
    // ImGui.NET build aborts the process (Linux build has assertions enabled). Use the shared
    // headless ImGuiTestFixture and the "ImGui Sequential" collection like the other ImGui-touching
    // test classes.
    [Collection("ImGui Sequential")]
    public class DebugGizmoLayerCaptureTests
    {
        // Minimal IInputProvider stub: all values default to zero / false.
        private sealed class StubInput : IInputProvider
        {
            public Vector2 MousePosition   => Vector2.Zero;
            public Vector2 MouseDelta      => Vector2.Zero;
            public float MouseWheelMove    => 0f;
            public bool IsMouseCaptured    => false;
            public bool IsKeyboardCaptured => false;
            public bool IsMouseButtonPressed(MapMouseButton b)  => false;
            public bool IsMouseButtonDown(MapMouseButton b)     => false;
            public bool IsMouseButtonReleased(MapMouseButton b) => false;
            public bool IsKeyPressed(MapKeyboardKey k)  => false;
            public bool IsKeyDown(MapKeyboardKey k)     => false;
            public bool IsKeyReleased(MapKeyboardKey k) => false;
            public int GetKeyPressed() => 0;
        }

        // Build a buffer that contains one exclusive InputCaptureBinding.
        private static DebugPrimitiveBuffer MakeBindingBuffer(long networkId = 1L)
        {
            var buf = new DebugPrimitiveBuffer(16);
            var prim = DebugPrimitive.MakeInputCaptureBinding(networkId, subElementId: 0, exclusive: true);
            buf.Append(prim);
            return buf;
        }

        // SC-B28-4: Update pushes a tool named "GizmoCaptureProxy" when an exclusive
        // InputCaptureBinding is present in the buffer.
        [Fact]
        public void SC_B28_4_Update_PushesCaptureTool_WhenExclusiveBindingPresent()
        {
            var bus    = new FdpEventBus();
            var buf    = MakeBindingBuffer();
            var layer  = new DebugGizmoLayer(31, buf, bus);

            using var fixture = new ImGuiTestFixture();
            layer.Update(0f);

            Assert.True(layer.TestHook_IsCaptureActive);
        }

        // SC-B28-5: Update pops the capture tool when the InputCaptureBinding
        // disappears from the buffer on the next frame.
        [Fact]
        public void SC_B28_5_Update_PopsCaptureToolWhenBindingGone()
        {
            var bus    = new FdpEventBus();
            var buf    = MakeBindingBuffer();
            var layer  = new DebugGizmoLayer(31, buf, bus);

            using var fixture = new ImGuiTestFixture();

            // Frame 1: binding present -- capture active.
            layer.Update(0f);

            // Frame 2: clear buffer (no binding) -- capture inactive.
            buf.Clear();
            layer.Update(0f);

            Assert.False(layer.TestHook_IsCaptureActive);
        }

        // SC-B28-6: HandleHover on the pushed capture tool publishes a GizmoDragUpdateEvent.
        [Fact]
        public void SC_B28_6_GizmoCaptureProxyTool_HandleHover_PublishesDragUpdateEvent()
        {
            var bus    = new FdpEventBus();
            var buf    = MakeBindingBuffer();
            var layer  = new DebugGizmoLayer(31, buf, bus);

            using var fixture = new ImGuiTestFixture();
            layer.Update(0f);

            layer.HandleHover(new Vector2(10f, 20f));

            bus.SwapBuffers();
            var events = bus.Read<GizmoDragUpdateEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(10f, events[0].WorldPos.X, precision: 3);
            Assert.Equal(20f, events[0].WorldPos.Y, precision: 3);
        }
    }
}
