namespace Hrot.IG.Systems;

/// <summary>
/// Application-owned settings object that exposes the active camera's visible
/// world-space rectangle and zoom level to <see cref="MapCullingSystem"/>.
///
/// Updated by <c>IgApplication.Run()</c> each frame (from the corner screen
/// positions projected through <c>MapCamera.ScreenToWorld</c>) before the kernel
/// ticks, so culling always reflects the exact viewport the user sees.
///
/// Not an ECS component — injected at construction, following the same pattern
/// as <see cref="MapUserConfig"/> (§CODE-STANDARDS §3: no direct world mutation
/// from application shell).
/// </summary>
public class MapCameraViewport
{
    // ── World-space AABB of the visible rectangle ─────────────────────────────

    /// <summary>Minimum X world coordinate currently visible (left edge of the viewport).</summary>
    public float WorldMinX { get; set; }

    /// <summary>Minimum Y world coordinate currently visible.</summary>
    public float WorldMinY { get; set; }

    /// <summary>Maximum X world coordinate currently visible (right edge of the viewport).</summary>
    public float WorldMaxX { get; set; }

    /// <summary>Maximum Y world coordinate currently visible.</summary>
    public float WorldMaxY { get; set; }

    /// <summary>
    /// Current camera zoom in pixels per meter.
    /// Used by <see cref="MapCullingSystem"/> to assign
    /// <see cref="Components.CullingState.LodLevel"/>.
    /// Defaults to <see cref="IgCameraConstants.InitialZoom"/> until the application
    /// sets its first real value.
    /// </summary>
    public float Zoom { get; set; } = IgCameraConstants.InitialZoom;

    // ── Convenience query ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the given world-space point lies within the current
    /// viewport rectangle (inclusive on all edges).
    ///
    /// Zero allocations — no boxing, no LINQ.
    /// </summary>
    /// <param name="x">World X coordinate to test.</param>
    /// <param name="y">World Y coordinate to test.</param>
    public bool Contains(float x, float y)
        => x >= WorldMinX && x <= WorldMaxX
        && y >= WorldMinY && y <= WorldMaxY;
}
