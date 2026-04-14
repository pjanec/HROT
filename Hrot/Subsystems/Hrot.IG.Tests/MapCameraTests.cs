using System.Numerics;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.IG;

namespace Hrot.IG.Tests;

/// <summary>
/// Tests verifying the behavioural constraints of MapCamera as used by IgApplication.
/// All min/max limits are referenced from IgCameraConstants rather than duplicated here.
/// </summary>
public class MapCameraZoomTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Creates a camera configured with IG-spec limits and zoom speed.</summary>
    private static MapCamera CreateIgCamera()
    {
        var cam = new MapCamera
        {
            MinZoom   = IgCameraConstants.MinZoom,
            MaxZoom   = IgCameraConstants.MaxZoom,
            ZoomSpeed = IgCameraConstants.ZoomSpeedPerTick
        };
        // Step once so InnerCamera state matches _targetZoom (both start at 1.0).
        cam.Update(0f);
        return cam;
    }

    /// <summary>
    /// Advances the simulation by <paramref name="frames"/> frames at 60 FPS.
    /// After 100+ frames the exponential-decay interpolation is within float precision.
    /// </summary>
    private static void Simulate(MapCamera cam, int frames = 120)
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < frames; i++)
            cam.Update(dt);
    }

    /// <summary>Applies <paramref name="ticks"/> upward scroll inputs and simulates convergence.</summary>
    private static void ScrollUp(MapCamera cam, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            cam.ProcessInput(1.0f, Vector2.Zero, isPanDown: false, isInputCaptured: false);
        Simulate(cam);
    }

    /// <summary>Applies <paramref name="ticks"/> downward scroll inputs and simulates convergence.</summary>
    private static void ScrollDown(MapCamera cam, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            cam.ProcessInput(-1.0f, Vector2.Zero, isPanDown: false, isInputCaptured: false);
        Simulate(cam);
    }

    // -------------------------------------------------------------------------
    // Zoom: upper clamp
    // -------------------------------------------------------------------------

    [Fact]
    public void Zoom_WhenScrollingFarUp_NeverExceedsMaxZoom()
    {
        var cam = CreateIgCamera();

        // Drive well past MaxZoom (requires only ~10 ticks from 1.0 to 5.0,
        // but 200 is used to confirm repeated inputs beyond the limit are safe).
        ScrollUp(cam, ticks: 200);

        Assert.True(cam.Zoom <= IgCameraConstants.MaxZoom,
            $"Zoom {cam.Zoom} exceeded MaxZoom {IgCameraConstants.MaxZoom}");
    }

    [Fact]
    public void Zoom_AfterReachingMaxZoom_AdditionalScrollKeepsZoomAtMax()
    {
        var cam = CreateIgCamera();
        ScrollUp(cam, ticks: 200); // saturate at max

        float zoomAtMax = cam.Zoom;

        // One more tick must not exceed the already-clamped value.
        ScrollUp(cam, ticks: 1);

        Assert.True(cam.Zoom <= IgCameraConstants.MaxZoom,
            $"Zoom {cam.Zoom} exceeded MaxZoom after additional scroll.");
        Assert.Equal(zoomAtMax, cam.Zoom, precision: 4);
    }

    [Fact]
    public void Zoom_WhenScrollingFarUp_ConvergesToMaxZoom()
    {
        var cam = CreateIgCamera();
        ScrollUp(cam, ticks: 200);

        // After saturation and convergence, zoom must be at — not merely near — the ceiling.
        Assert.Equal(IgCameraConstants.MaxZoom, cam.Zoom, precision: 3);
    }

    // -------------------------------------------------------------------------
    // Zoom: lower clamp
    // -------------------------------------------------------------------------

    [Fact]
    public void Zoom_WhenScrollingFarDown_NeverGoesBelowMinZoom()
    {
        var cam = CreateIgCamera();
        ScrollDown(cam, ticks: 200);

        Assert.True(cam.Zoom >= IgCameraConstants.MinZoom,
            $"Zoom {cam.Zoom} dropped below MinZoom {IgCameraConstants.MinZoom}");
    }

    [Fact]
    public void Zoom_AfterReachingMinZoom_AdditionalScrollKeepsZoomAtMin()
    {
        var cam = CreateIgCamera();
        ScrollDown(cam, ticks: 200);

        float zoomAtMin = cam.Zoom;
        ScrollDown(cam, ticks: 1);

        Assert.True(cam.Zoom >= IgCameraConstants.MinZoom,
            $"Zoom {cam.Zoom} dropped below MinZoom after additional scroll.");
        Assert.Equal(zoomAtMin, cam.Zoom, precision: 4);
    }

    [Fact]
    public void Zoom_WhenScrollingFarDown_ConvergesToMinZoom()
    {
        var cam = CreateIgCamera();
        ScrollDown(cam, ticks: 200);

        Assert.Equal(IgCameraConstants.MinZoom, cam.Zoom, precision: 3);
    }

    // -------------------------------------------------------------------------
    // Zoom: single-tick factor
    // -------------------------------------------------------------------------

    [Fact]
    public void Zoom_SingleScrollUp_IncreasesZoomByExactly1Point2Factor()
    {
        var cam = CreateIgCamera();
        float initialZoom = cam.Zoom; // 1.0 (default constructor)

        ScrollUp(cam, ticks: 1);

        float expected = initialZoom * IgCameraConstants.ZoomFactor;
        Assert.Equal(expected, cam.Zoom, precision: 4);
    }

    [Fact]
    public void Zoom_SingleScrollDown_DecreasesZoomByZoomSpeedFactor()
    {
        var cam = CreateIgCamera();
        float initialZoom = cam.Zoom;

        ScrollDown(cam, ticks: 1);

        // Camera formula: newZoom = initial * (1 + ZoomSpeed * wheel)
        // wheel = -1 → newZoom = initial * (1 - ZoomSpeedPerTick) = initial * 0.8
        float expected = initialZoom * (1.0f - IgCameraConstants.ZoomSpeedPerTick);
        Assert.Equal(expected, cam.Zoom, precision: 4);
    }

    // -------------------------------------------------------------------------
    // Zoom: boundary — scrolling near (but not at) limits still moves zoom
    // -------------------------------------------------------------------------

    [Fact]
    public void Zoom_WhenBelowMax_ScrollUpIncreasesZoom()
    {
        var cam = CreateIgCamera();
        // Start at a zoom well within limits (default 1.0, max is 5.0).
        float before = cam.Zoom;

        ScrollUp(cam, ticks: 1);

        Assert.True(cam.Zoom > before,
            $"Expected zoom to increase above {before}, got {cam.Zoom}");
    }

    [Fact]
    public void Zoom_WhenAboveMin_ScrollDownDecreasesZoom()
    {
        var cam = CreateIgCamera();
        float before = cam.Zoom;

        ScrollDown(cam, ticks: 1);

        Assert.True(cam.Zoom < before,
            $"Expected zoom to decrease below {before}, got {cam.Zoom}");
    }
}

/// <summary>
/// Tests verifying MapCamera panning behaviour as used by IgApplication.
/// </summary>
public class MapCameraPanTests
{
    private static MapCamera CreateIgCamera()
    {
        var cam = new MapCamera
        {
            MinZoom   = IgCameraConstants.MinZoom,
            MaxZoom   = IgCameraConstants.MaxZoom,
            ZoomSpeed = IgCameraConstants.ZoomSpeedPerTick
        };
        cam.Update(0f);
        return cam;
    }

    private static void Simulate(MapCamera cam, int frames = 120)
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < frames; i++)
            cam.Update(dt);
    }

    // -------------------------------------------------------------------------
    // Middle-mouse drag pan
    // -------------------------------------------------------------------------

    [Fact]
    public void Pan_DraggingRight_CameraTargetXDecreases()
    {
        // Dragging right means the user pulls the map rightward.
        // The world "slides" right under the cursor → camera target moves left (−X).
        var cam = CreateIgCamera();
        float zoomSnapshot = cam.Zoom; // 1.0

        // Frame 1: button-down at (100, 100) — drag starts, no delta yet.
        cam.ProcessInput(0f, new Vector2(100, 100), isPanDown: true, isInputCaptured: false);
        cam.Update(1f / 60f);

        // Frame 2: still down, cursor moved 50 px to the right.
        cam.ProcessInput(0f, new Vector2(150, 100), isPanDown: true, isInputCaptured: false);

        // Release.
        cam.ProcessInput(0f, new Vector2(150, 100), isPanDown: false, isInputCaptured: false);
        Simulate(cam);

        // deltaScreen.X = +50  →  deltaWorld.X = +50 / zoom = +50
        // _targetTarget.X -= +50  →  target moves to −50 from origin
        float expectedX = -(50f / zoomSnapshot);
        Assert.Equal(expectedX, cam.Target.X, precision: 2);
    }

    [Fact]
    public void Pan_DraggingDown_CameraTargetYIncreases()
    {
        // Dragging downward (in screen space) → world "slides" down → target.Y increases.
        var cam = CreateIgCamera();
        float zoomSnapshot = cam.Zoom;

        cam.ProcessInput(0f, new Vector2(100, 100), isPanDown: true, isInputCaptured: false);
        cam.Update(1f / 60f);

        cam.ProcessInput(0f, new Vector2(100, 150), isPanDown: true, isInputCaptured: false);
        cam.ProcessInput(0f, new Vector2(100, 150), isPanDown: false, isInputCaptured: false);
        Simulate(cam);

        float expectedY = -(50f / zoomSnapshot); // _targetTarget.Y -= +50 → target.Y = −50
        Assert.Equal(expectedY, cam.Target.Y, precision: 2);
    }

    [Fact]
    public void Pan_LargerDrag_ProducesproportionalTargetOffset()
    {
        // Drag of 100 px should produce exactly twice the world displacement of a 50 px drag.
        var cam50  = CreateIgCamera();
        var cam100 = CreateIgCamera();

        // 50-px drag
        cam50.ProcessInput(0f, new Vector2(0, 0), isPanDown: true, isInputCaptured: false);
        cam50.Update(1f / 60f);
        cam50.ProcessInput(0f, new Vector2(50, 0), isPanDown: true, isInputCaptured: false);
        cam50.ProcessInput(0f, Vector2.Zero, isPanDown: false, isInputCaptured: false);
        Simulate(cam50);

        // 100-px drag
        cam100.ProcessInput(0f, new Vector2(0, 0), isPanDown: true, isInputCaptured: false);
        cam100.Update(1f / 60f);
        cam100.ProcessInput(0f, new Vector2(100, 0), isPanDown: true, isInputCaptured: false);
        cam100.ProcessInput(0f, Vector2.Zero, isPanDown: false, isInputCaptured: false);
        Simulate(cam100);

        Assert.Equal(cam100.Target.X, cam50.Target.X * 2f, precision: 2);
    }

    // -------------------------------------------------------------------------
    // Input-capture suppression
    // -------------------------------------------------------------------------

    [Fact]
    public void Pan_WhenInputCaptured_DoesNotMoveCameraTarget()
    {
        // When ImGui has keyboard/mouse capture, camera must not respond.
        var cam = CreateIgCamera();

        cam.ProcessInput(0f, new Vector2(0, 0), isPanDown: true, isInputCaptured: true);
        cam.Update(1f / 60f);
        cam.ProcessInput(0f, new Vector2(100, 0), isPanDown: true, isInputCaptured: true);
        Simulate(cam);

        Assert.Equal(0f, cam.Target.X, precision: 4);
    }

    [Fact]
    public void Zoom_WhenInputCaptured_ZoomRemainsUnchanged()
    {
        var cam = CreateIgCamera();
        float initialZoom = cam.Zoom;

        // Scroll while captured — should be ignored.
        cam.ProcessInput(5.0f, Vector2.Zero, isPanDown: false, isInputCaptured: true);
        Simulate(cam);

        Assert.Equal(initialZoom, cam.Zoom, precision: 4);
    }
}
