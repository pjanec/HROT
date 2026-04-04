using System.Numerics;
using Hrot.ScenarioEditor.Tools;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="MeasureTool"/> (IG.3.4).
///
/// Validates the distance state-machine logic and mathematical accuracy without
/// requiring a Raylib window context.  All rendering-path assertions are omitted â€”
/// distance computation is exercised purely through
/// <see cref="MeasureTool.HandleClick"/> and
/// <see cref="MeasureTool.LastMeasuredDistanceMeters"/>.
///
/// Coverage:
/// <list type="bullet">
///   <item>Initial state: not measuring, distance = NaN.</item>
///   <item>First left-click arms the start point.</item>
///   <item>Second left-click computes and exposes the distance.</item>
///   <item>Distance is correct for axial, diagonal, and coincident point pairs.</item>
///   <item>Right-click mid-measurement cancels without setting a distance.</item>
/// </list>
/// </summary>
public class MeasureToolTests
{
    // â”€â”€ Initial state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void InitialState_IsNotMeasuring()
    {
        var tool = new MeasureTool();

        Assert.False(tool.IsMeasuring);
    }

    [Fact]
    public void InitialState_LastDistanceIsNaN()
    {
        var tool = new MeasureTool();

        Assert.True(float.IsNaN(tool.LastMeasuredDistanceMeters));
    }

    // â”€â”€ First click arms start point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void HandleClick_FirstLeftClick_StartsIsMeasuring()
    {
        var tool = new MeasureTool();

        tool.HandleClick(new Vector2(0f, 0f), MouseButton.Left);

        Assert.True(tool.IsMeasuring);
    }

    [Fact]
    public void HandleClick_FirstLeftClick_DistanceRemainsNaN()
    {
        var tool = new MeasureTool();

        tool.HandleClick(new Vector2(0f, 0f), MouseButton.Left);

        // Distance is not set until the second click.
        Assert.True(float.IsNaN(tool.LastMeasuredDistanceMeters));
    }

    // â”€â”€ Second click computes distance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Horizontal segment: distance must match X-axis displacement exactly.
    /// </summary>
    [Fact]
    public void HandleClick_SecondLeftClick_ComputesHorizontalDistance()
    {
        var tool = new MeasureTool();
        tool.HandleClick(new Vector2(0f,    0f), MouseButton.Left);
        tool.HandleClick(new Vector2(300f,  0f), MouseButton.Left);

        Assert.Equal(300f, tool.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Vertical segment: distance must match Y-axis displacement exactly.
    /// </summary>
    [Fact]
    public void HandleClick_SecondLeftClick_ComputesVerticalDistance()
    {
        var tool = new MeasureTool();
        tool.HandleClick(new Vector2(0f,    0f), MouseButton.Left);
        tool.HandleClick(new Vector2(0f,  400f), MouseButton.Left);

        Assert.Equal(400f, tool.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Diagonal segment: distance must satisfy Pythagoras (3-4-5 right triangle).
    /// </summary>
    [Fact]
    public void HandleClick_SecondLeftClick_ComputesDiagonalDistance_Pythagoras()
    {
        var tool = new MeasureTool();
        tool.HandleClick(new Vector2(0f, 0f), MouseButton.Left);
        tool.HandleClick(new Vector2(3f, 4f), MouseButton.Left);

        Assert.Equal(5f, tool.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Coincident points (start == end): distance is zero, not NaN or negative.
    /// </summary>
    [Fact]
    public void HandleClick_SecondLeftClick_CoincidentPoints_DistanceIsZero()
    {
        var tool = new MeasureTool();
        tool.HandleClick(new Vector2(100f, 100f), MouseButton.Left);
        tool.HandleClick(new Vector2(100f, 100f), MouseButton.Left);

        Assert.Equal(0f, tool.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Large-coordinate measurement â€” ensures floating-point accuracy at
    /// typical exercise-area scales (several kilometres).
    /// </summary>
    [Fact]
    public void HandleClick_LargeCoordinates_DistanceAccurate()
    {
        var tool = new MeasureTool();
        tool.HandleClick(new Vector2(0f,      0f     ), MouseButton.Left);
        tool.HandleClick(new Vector2(5000f, 5000f), MouseButton.Left);

        float expected = Vector2.Distance(new Vector2(0f, 0f), new Vector2(5000f, 5000f));
        Assert.Equal(expected, tool.LastMeasuredDistanceMeters, precision: 2);
    }

    // â”€â”€ Right-click cancels â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Right-clicking before the first click has no effect (not measuring, no distance).
    /// </summary>
    [Fact]
    public void HandleClick_RightClickBeforeStart_DoesNotSetDistance()
    {
        var tool = new MeasureTool();

        tool.HandleClick(new Vector2(0f, 0f), MouseButton.Right);

        Assert.True(float.IsNaN(tool.LastMeasuredDistanceMeters));
    }

    /// <summary>
    /// Right-clicking after setting the start point cancels without computing distance.
    /// </summary>
    [Fact]
    public void HandleClick_RightClickAfterStart_CancelsWithoutDistance()
    {
        var tool = new MeasureTool();
        tool.HandleClick(new Vector2(0f, 0f), MouseButton.Left); // Set start

        tool.HandleClick(new Vector2(100f, 0f), MouseButton.Right); // Cancel

        // IsMeasuring is no longer meaningful after pop, but distance should stay NaN.
        Assert.True(float.IsNaN(tool.LastMeasuredDistanceMeters));
    }

    // â”€â”€ Input returns correct consumed flags â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void HandleClick_LeftClick_ReturnsTrue_Consumed()
    {
        var tool = new MeasureTool();

        bool consumed = tool.HandleClick(new Vector2(0f, 0f), MouseButton.Left);

        Assert.True(consumed);
    }

    [Fact]
    public void HandleClick_RightClick_ReturnsTrue_Consumed()
    {
        var tool = new MeasureTool();

        bool consumed = tool.HandleClick(new Vector2(0f, 0f), MouseButton.Right);

        Assert.True(consumed);
    }

    [Fact]
    public void HandleHover_DoesNotConsumeInput()
    {
        var tool = new MeasureTool();

        bool consumed = tool.HandleHover(new Vector2(50f, 50f));

        Assert.False(consumed);
    }

    // â”€â”€ BUG2-T001: Draw crosshair when no start point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Calling <see cref="MeasureTool.Draw"/> with no start point set must not throw.
    /// </summary>
    [Fact]
    public void Draw_NoStartPoint_DoesNotThrow()
    {
        var tool = new MeasureTool();
        tool.TestHook_SkipRaylibCalls = true;

        // Move cursor so _currentPoint is at a known non-default position.
        tool.HandleHover(new Vector2(50f, 50f));

        var ex = Record.Exception(() => tool.Draw(new RenderContext { Camera = new Camera2D { Zoom = 1f } }));
        Assert.Null(ex);
    }

    /// <summary>
    /// When no start point is set, <see cref="MeasureTool.Draw"/> must issue exactly
    /// four line draw commands and one circle draw command (the crosshair cursor).
    /// </summary>
    [Fact]
    public void Draw_NoStartPoint_DrawsCrosshair()
    {
        var tool = new MeasureTool();
        tool.TestHook_SkipRaylibCalls = true;
        tool.HandleHover(new Vector2(100f, 100f));

        tool.Draw(new RenderContext { Camera = new Camera2D { Zoom = 1f } });

        Assert.Equal(4, tool.TestHook_LineDrawCount);
        Assert.Equal(1, tool.TestHook_CircleDrawCount);
    }
}
