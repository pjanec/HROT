using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Vis2D.Tests.Input;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    public class GizmoInteractionProxyToolTests
    {
        private static PickToken MakeToken() => new PickToken { SubElementId = 42u };

        // SC-GZ010-1: HandleDrag publishes GizmoDragUpdateEvent with WorldPos.X == 5f, Y == 10f.
        [Fact]
        public void SC_GZ010_1_HandleDrag_PublishesDragUpdateEvent()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var tool = new GizmoInteractionProxyTool(token, bus);

            // GZ046: arm the drag before dragging.
            tool.HandlePress(Vector2.Zero, MapMouseButton.Left);

            var worldPos = new Vector2(5f, 10f);
            var result = tool.HandleDrag(worldPos, Vector2.Zero);
            bus.SwapBuffers();

            var events = bus.Read<GizmoDragUpdateEvent>();
            Assert.True(result);
            Assert.Equal(1, events.Length);
            Assert.Equal(5f, events[0].WorldPos.X);
            Assert.Equal(10f, events[0].WorldPos.Y);
            Assert.Equal(0f, events[0].WorldPos.Z);
            Assert.Equal(token.SubElementId, events[0].Token.SubElementId);

            bus.Dispose();
        }

        // SC-GZ010-2: HandleClick(Right) publishes GizmoInteractionCancelEvent and pops canvas.
        [Fact]
        public void SC_GZ010_2_HandleClickRight_PublishesCancelAndPopsCanvas()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            var tool = new GizmoInteractionProxyTool(token, bus);
            canvas.PushTool(tool);

            Assert.Same(tool, canvas.ActiveTool);

            var result = tool.HandleClick(new Vector2(1f, 2f), MapMouseButton.Right);
            bus.SwapBuffers();

            Assert.True(result);
            Assert.Null(canvas.ActiveTool);

            var events = bus.Read<GizmoInteractionCancelEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(token.SubElementId, events[0].Token.SubElementId);

            bus.Dispose();
        }

        // SC-GZ010-3: HandleKeyPressed(Escape) publishes GizmoInteractionCancelEvent and pops canvas.
        [Fact]
        public void SC_GZ010_3_HandleKeyEscape_PublishesCancelAndPopsCanvas()
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

            var events = bus.Read<GizmoInteractionCancelEvent>();
            Assert.Equal(1, events.Length);

            bus.Dispose();
        }

        // SC-GZ010-4: HandleClick(Left) after press publishes GizmoInteractionCommitEvent and pops canvas.
        [Fact]
        public void SC_GZ010_4_HandleClickLeft_PublishesCommitAndPopsCanvas()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var input = new MockInputProvider();
            var canvas = new MapCanvas(input);
            var tool = new GizmoInteractionProxyTool(token, bus);
            canvas.PushTool(tool);

            // GZ046: arm the drag before clicking to commit.
            tool.HandlePress(Vector2.Zero, MapMouseButton.Left);

            var worldPos = new Vector2(3f, 7f);
            var result = tool.HandleClick(worldPos, MapMouseButton.Left);
            bus.SwapBuffers();

            Assert.True(result);
            Assert.Null(canvas.ActiveTool);

            var events = bus.Read<GizmoInteractionCommitEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(3f, events[0].WorldPos.X);
            Assert.Equal(7f, events[0].WorldPos.Y);

            bus.Dispose();
        }

        // SC-GZ010-5 (negative): HandleClick(Middle) returns false and publishes nothing.
        [Fact]
        public void SC_GZ010_5_HandleClickMiddle_ReturnsFalse()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var tool = new GizmoInteractionProxyTool(token, bus);

            var result = tool.HandleClick(Vector2.Zero, MapMouseButton.Middle);

            Assert.False(result);

            bus.Dispose();
        }

        // SC-GZ010-6 (negative): HandleKeyPressed(A) returns false.
        [Fact]
        public void SC_GZ010_6_HandleKeyA_ReturnsFalse()
        {
            var bus = new FdpEventBus();
            var token = MakeToken();
            var tool = new GizmoInteractionProxyTool(token, bus);

            var result = tool.HandleKeyPressed((MapKeyboardKey)65); // 'A' key

            Assert.False(result);

            bus.Dispose();
        }
    }
}
