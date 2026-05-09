using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.IG.Tests.Gizmos;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="MeasureGizmo"/> (IG.3.4).
///
/// Validates the distance state-machine logic and mathematical accuracy without
/// requiring a Raylib window context.  All rendering-path assertions are omitted
/// for distance computation, which is exercised purely through
/// <see cref="MeasureGizmo.OnMouseEvent"/> and
/// <see cref="MeasureGizmo.LastMeasuredDistanceMeters"/>.
///
/// Coverage:
/// <list type="bullet">
///   <item>Initial state: not measuring, distance = NaN.</item>
///   <item>First left-click (release) arms the start point.</item>
///   <item>Second left-click (release) computes and exposes the distance.</item>
///   <item>Distance is correct for axial, diagonal, and coincident point pairs.</item>
///   <item>Right-press mid-measurement cancels without setting a distance.</item>
/// </list>
/// </summary>
public class MeasureToolTests
{
    // -- Initial state --------------------------------------------------------

    [Fact]
    public void InitialState_IsNotMeasuring()
    {
        var gizmo = new MeasureGizmo();

        Assert.False(gizmo.IsMeasuring);
    }

    [Fact]
    public void InitialState_LastDistanceIsNaN()
    {
        var gizmo = new MeasureGizmo();

        Assert.True(float.IsNaN(gizmo.LastMeasuredDistanceMeters));
    }

    // -- First click arms start point ----------------------------------------

    [Fact]
    public void OnMouseEvent_FirstLeftRelease_StartsIsMeasuring()
    {
        var gizmo = new MeasureGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f, 0f, 0f));

        Assert.True(gizmo.IsMeasuring);
    }

    [Fact]
    public void OnMouseEvent_FirstLeftRelease_DistanceRemainsNaN()
    {
        var gizmo = new MeasureGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f, 0f, 0f));

        // Distance is not set until the second click.
        Assert.True(float.IsNaN(gizmo.LastMeasuredDistanceMeters));
    }

    // -- Second click computes distance ---------------------------------------

    /// <summary>
    /// Horizontal segment: distance must match X-axis displacement exactly.
    /// </summary>
    [Fact]
    public void OnMouseEvent_SecondLeftRelease_ComputesHorizontalDistance()
    {
        var gizmo = new MeasureGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f,    0f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(300f,  0f, 0f));

        Assert.Equal(300f, gizmo.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Vertical segment: distance must match Y-axis displacement exactly.
    /// </summary>
    [Fact]
    public void OnMouseEvent_SecondLeftRelease_ComputesVerticalDistance()
    {
        var gizmo = new MeasureGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f,    0f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f,  400f, 0f));

        Assert.Equal(400f, gizmo.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Diagonal segment: distance must satisfy Pythagoras (3-4-5 right triangle).
    /// </summary>
    [Fact]
    public void OnMouseEvent_SecondLeftRelease_ComputesDiagonalDistance_Pythagoras()
    {
        var gizmo = new MeasureGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f, 0f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(3f, 4f, 0f));

        Assert.Equal(5f, gizmo.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Coincident points (start == end): distance is zero, not NaN or negative.
    /// </summary>
    [Fact]
    public void OnMouseEvent_SecondLeftRelease_CoincidentPoints_DistanceIsZero()
    {
        var gizmo = new MeasureGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(100f, 100f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(100f, 100f, 0f));

        Assert.Equal(0f, gizmo.LastMeasuredDistanceMeters, precision: 3);
    }

    /// <summary>
    /// Large-coordinate measurement ensures floating-point accuracy at
    /// typical exercise-area scales (several kilometres).
    /// </summary>
    [Fact]
    public void OnMouseEvent_LargeCoordinates_DistanceAccurate()
    {
        var gizmo = new MeasureGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(0f,      0f,     0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(5000f, 5000f, 0f));

        float expected = Vector2.Distance(new Vector2(0f, 0f), new Vector2(5000f, 5000f));
        Assert.Equal(expected, gizmo.LastMeasuredDistanceMeters, precision: 2);
    }

    // -- Right-press cancels --------------------------------------------------

    /// <summary>
    /// Right-pressing before the first click has no effect (not measuring, no distance).
    /// </summary>
    [Fact]
    public void OnMouseEvent_RightPressBeforeStart_DoesNotSetDistance()
    {
        var gizmo = new MeasureGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Right, true, new Vector3(0f, 0f, 0f));

        Assert.True(float.IsNaN(gizmo.LastMeasuredDistanceMeters));
    }

    /// <summary>
    /// Right-pressing after setting the start point cancels without computing distance.
    /// </summary>
    [Fact]
    public void OnMouseEvent_RightPressAfterStart_CancelsWithoutDistance()
    {
        var gizmo = new MeasureGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left,  false, new Vector3(0f, 0f, 0f));   // Set start
        gizmo.OnMouseEvent(MapMouseButton.Right, true,  new Vector3(100f, 0f, 0f)); // Cancel

        Assert.True(float.IsNaN(gizmo.LastMeasuredDistanceMeters));
    }

    // -- BUG2-T001: Draw crosshair when no start point ------------------------

    /// <summary>
    /// Calling <see cref="MeasureGizmo.UpdateAndDraw"/> with no start point set must not throw.
    /// </summary>
    [Fact]
    public void UpdateAndDraw_NoStartPoint_DoesNotThrow()
    {
        var spy   = new FullCapturingDrawBuilder();
        var gizmo = new MeasureGizmo();

        gizmo.OnDragUpdate(new Vector3(50f, 50f, 0f));

        var ex = Record.Exception(() => gizmo.UpdateAndDraw(0f, spy));
        Assert.Null(ex);
    }

    /// <summary>
    /// When no start point is set, <see cref="MeasureGizmo.UpdateAndDraw"/> must emit
    /// exactly four line primitives and one sphere primitive (the crosshair cursor).
    /// </summary>
    [Fact]
    public void UpdateAndDraw_NoStartPoint_DrawsCrosshair()
    {
        var spy   = new FullCapturingDrawBuilder();
        var gizmo = new MeasureGizmo();
        gizmo.OnDragUpdate(new Vector3(100f, 100f, 0f));

        gizmo.UpdateAndDraw(0f, spy);

        Assert.Equal(4, spy.LineCalls.Count);
        Assert.Equal(1, spy.SphereCalls.Count);
    }
}