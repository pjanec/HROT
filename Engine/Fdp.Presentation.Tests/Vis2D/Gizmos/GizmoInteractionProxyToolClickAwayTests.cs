// SC-GZ046: GizmoInteractionProxyTool click-away guard and HandlePress routing.
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Vis2D.Tests;
using Fdp.Toolkit.Vis2D.Tests.Input;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    public class GizmoInteractionProxyToolClickAwayTests
    {
        private static PickToken MakeToken() => new PickToken { SubElementId = 99u };

        // SC-GZ046-1: HandlePress+HandleDrag+HandleClick(Left) commits and pops tool.
        [Fact]
        public void SC_GZ046_1_PressAndClickLeft_CommitsAndPopsTool()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            var tool = new GizmoInteractionProxyTool(token, bus);
            canvas.PushTool(tool);

            tool.HandlePress(Vector2.Zero, MapMouseButton.Left);
            tool.HandleDrag(new Vector2(1f, 2f), Vector2.Zero);
            var result = tool.HandleClick(new Vector2(1f, 2f), MapMouseButton.Left);
            bus.SwapBuffers();

            Assert.True(result);
            Assert.Null(canvas.ActiveTool);
            var commits = bus.Read<GizmoInteractionCommitEvent>();
            Assert.Equal(1, commits.Length);
            var cancels = bus.Read<GizmoInteractionCancelEvent>();
            Assert.Equal(0, cancels.Length);

            bus.Dispose();
        }

        // SC-GZ046-2: HandleClick(Left) without prior HandlePress cancels and returns false.
        [Fact]
        public void SC_GZ046_2_HandleClickLeftWithoutPress_CancelsAndReturnsFalse()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            var tool = new GizmoInteractionProxyTool(token, bus);
            canvas.PushTool(tool);

            var result = tool.HandleClick(new Vector2(5f, 5f), MapMouseButton.Left);
            bus.SwapBuffers();

            Assert.False(result);
            Assert.Null(canvas.ActiveTool);
            var cancels = bus.Read<GizmoInteractionCancelEvent>();
            Assert.Equal(1, cancels.Length);
            var commits = bus.Read<GizmoInteractionCommitEvent>();
            Assert.Equal(0, commits.Length);

            bus.Dispose();
        }

        // SC-GZ046-3: HandleClick(Right) cancels and returns true.
        [Fact]
        public void SC_GZ046_3_HandleClickRight_CancelsAndReturnsTrue()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            var tool = new GizmoInteractionProxyTool(token, bus);
            canvas.PushTool(tool);

            var result = tool.HandleClick(Vector2.Zero, MapMouseButton.Right);
            bus.SwapBuffers();

            Assert.True(result);
            Assert.Null(canvas.ActiveTool);
            var cancels = bus.Read<GizmoInteractionCancelEvent>();
            Assert.Equal(1, cancels.Length);

            bus.Dispose();
        }

        // SC-GZ046-4: After cancel, _dragActive is false (drag does nothing afterwards).
        [Fact]
        public void SC_GZ046_4_AfterCancel_DragDoesNothing()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var tool = new GizmoInteractionProxyTool(token, bus);

            // Arm then cancel via right-click.
            tool.HandlePress(Vector2.Zero, MapMouseButton.Left);
            tool.HandleClick(Vector2.Zero, MapMouseButton.Right);
            bus.SwapBuffers();

            // Drain cancel event.
            bus.Read<GizmoInteractionCancelEvent>();

            // Now drag should do nothing.
            var result = tool.HandleDrag(new Vector2(1f, 1f), Vector2.Zero);
            bus.SwapBuffers();

            Assert.False(result);
            var updates = bus.Read<GizmoDragUpdateEvent>();
            Assert.Equal(0, updates.Length);

            bus.Dispose();
        }

        // SC-GZ046-5 / SC-GZ046-7 (regression): Escape publishes CancelEvent and pops tool.
        [Fact]
        public void SC_GZ046_5_EscapeKey_CancelsAndPopsTool()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            var tool = new GizmoInteractionProxyTool(token, bus);
            canvas.PushTool(tool);

            var result = tool.HandleKeyPressed(MapKeyboardKey.Escape);
            bus.SwapBuffers();

            Assert.True(result);
            Assert.Null(canvas.ActiveTool);
            var cancels = bus.Read<GizmoInteractionCancelEvent>();
            Assert.Equal(1, cancels.Length);

            bus.Dispose();
        }

        // SC-GZ046-6: HandleDrag without prior HandlePress returns false, no event.
        [Fact]
        public void SC_GZ046_6_HandleDragWithoutPress_ReturnsFalseNoEvent()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var tool = new GizmoInteractionProxyTool(token, bus);

            var result = tool.HandleDrag(new Vector2(3f, 4f), Vector2.Zero);
            bus.SwapBuffers();

            Assert.False(result);
            var updates = bus.Read<GizmoDragUpdateEvent>();
            Assert.Equal(0, updates.Length);

            bus.Dispose();
        }

        // SC-GZ046-6b: MapCanvas calls ActiveTool.HandlePress before routing to layers.
        [Fact]
        public void SC_GZ046_6b_MapCanvas_CallsActiveToolHandlePressBeforeLayers()
        {
            var canvas = new TestableMapCanvas();
            var recorder = new PressingRecorderTool();
            canvas.PushTool(recorder);

            canvas.InputProvider.IsLeftPressed = true;
            canvas.InputProvider.MousePosition = new Vector2(50f, 50f);
            canvas.HandleInput();

            Assert.True(recorder.HandlePressCalled);
            Assert.Equal(MapMouseButton.Left, recorder.LastButton);
        }

        private sealed class PressingRecorderTool : IMapTool
        {
            public string Name => "Recorder";
            public bool HandlePressCalled { get; private set; }
            public MapMouseButton LastButton { get; private set; }

            public void OnEnter(MapCanvas canvas)  { }
            public void OnExit()                   { }
            public void Update(float dt)           { }
            public void Draw(RenderContext ctx)    { }
            public bool HandleHover(Vector2 wp)    => false;
            public bool HandleDrag(Vector2 wp, Vector2 d) => false;
            public bool HandleClick(Vector2 wp, MapMouseButton b) => false;
            public bool HandleKeyPressed(MapKeyboardKey k) => false;

            public bool HandlePress(Vector2 worldPos, MapMouseButton button)
            {
                HandlePressCalled = true;
                LastButton = button;
                return true; // consume so layer routing is skipped
            }
        }
    }
}
