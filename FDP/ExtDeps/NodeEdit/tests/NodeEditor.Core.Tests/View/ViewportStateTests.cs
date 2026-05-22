using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.View;

public class ViewportStateTests
{
    [Fact]
    public void GraphToScreen_then_ScreenToGraph_RoundTrips()
    {
        // Identity viewport (default)
        var vp = new ViewportState();
        var graphPt = new Vector2(100f, 200f);
        var screen = vp.GraphToScreen(graphPt);
        var back = vp.ScreenToGraph(screen);
        back.X.Should().BeApproximately(graphPt.X, 1e-4f);
        back.Y.Should().BeApproximately(graphPt.Y, 1e-4f);

        // Non-identity: pan and zoom
        vp.PanGraph = new Vector2(50f, 30f);
        vp.ZoomAt(Vector2.Zero, 2.0f);
        var graphPt2 = new Vector2(250f, 150f);
        var screen2 = vp.GraphToScreen(graphPt2);
        var back2 = vp.ScreenToGraph(screen2);
        back2.X.Should().BeApproximately(graphPt2.X, 1e-4f);
        back2.Y.Should().BeApproximately(graphPt2.Y, 1e-4f);
    }

    [Fact]
    public void ZoomAt_KeepsAnchorPointStable()
    {
        var vp = new ViewportState
        {
            CanvasScreenOrigin = new Vector2(10f, 10f),
            CanvasScreenSize = new Vector2(800f, 600f)
        };

        var anchorScreen = new Vector2(400f, 300f);
        var anchorGraphBefore = vp.ScreenToGraph(anchorScreen);

        vp.ZoomAt(anchorScreen, 1.5f);

        var anchorGraphAfter = vp.ScreenToGraph(anchorScreen);
        anchorGraphAfter.X.Should().BeApproximately(anchorGraphBefore.X, 1e-4f);
        anchorGraphAfter.Y.Should().BeApproximately(anchorGraphBefore.Y, 1e-4f);
    }

    [Fact]
    public void ZoomAt_ClampedToMin()
    {
        var vp = new ViewportState();
        // Apply a very large downward factor — should clamp to MinZoom
        vp.ZoomAt(Vector2.Zero, 0.01f);
        vp.Zoom.Should().BeApproximately(ViewportState.MinZoom, 1e-6f);
    }

    [Fact]
    public void ZoomAt_ClampedToMax()
    {
        var vp = new ViewportState();
        // Apply a very large upward factor — should clamp to MaxZoom
        vp.ZoomAt(Vector2.Zero, 1000f);
        vp.Zoom.Should().BeApproximately(ViewportState.MaxZoom, 1e-6f);
    }

    [Fact]
    public void FrameRect_CentersRect()
    {
        var vp = new ViewportState
        {
            CanvasScreenOrigin = new Vector2(0f, 0f),
            CanvasScreenSize = new Vector2(800f, 600f)
        };

        // A rect at graph origin (100,100) size 200x100
        var rect = new RectF(new Vector2(100f, 100f), new Vector2(200f, 100f));
        vp.FrameRect(rect, marginPx: 0f);

        // After framing, the rect center should map to the canvas center
        var canvasCenter = new Vector2(400f, 300f); // CanvasScreenOrigin + CanvasScreenSize * 0.5
        var rectCenterScreen = vp.GraphToScreen(rect.Center);
        rectCenterScreen.X.Should().BeApproximately(canvasCenter.X, 0.1f);
        rectCenterScreen.Y.Should().BeApproximately(canvasCenter.Y, 0.1f);
    }

    [Fact]
    public void Reset_RestoresIdentity()
    {
        var vp = new ViewportState();
        vp.ZoomAt(new Vector2(50f, 50f), 2.0f);
        vp.Pan(new Vector2(100f, 100f));

        vp.Reset();

        vp.Zoom.Should().BeApproximately(1.0f, 1e-6f);
        vp.PanGraph.Should().Be(Vector2.Zero);
    }
}
