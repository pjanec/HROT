namespace Hrot.Player.Systems;

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
    public float WorldMinX { get => _minX; set { if (_minX != value) { _minX = value; Version++; } } }
    private float _minX;

    /// <summary>Minimum Y world coordinate currently visible.</summary>
    public float WorldMinY { get => _minY; set { if (_minY != value) { _minY = value; Version++; } } }
    private float _minY;

    /// <summary>Maximum X world coordinate currently visible (right edge of the viewport).</summary>
    public float WorldMaxX { get => _maxX; set { if (_maxX != value) { _maxX = value; Version++; } } }
    private float _maxX;

    /// <summary>Maximum Y world coordinate currently visible.</summary>
    public float WorldMaxY { get => _maxY; set { if (_maxY != value) { _maxY = value; Version++; } } }
    private float _maxY;

    /// <summary>
    /// Current camera zoom in pixels per meter.
    /// Used by <see cref="MapCullingSystem"/> to assign
    /// <see cref="Components.CullingState.LodLevel"/>.
    /// Defaults to <see cref="PlayerCameraConstants.InitialZoom"/> until the application
    /// sets its first real value.
    /// </summary>
    public float Zoom { get => _zoom; set { if (_zoom != value) { _zoom = value; Version++; } } }
    private float _zoom = PlayerCameraConstants.InitialZoom;

    /// <summary>⭐ CE-1480 G4 — moves whenever any of the five values above ACTUALLY changes (an assignment of the same value does not): the one number
    /// <see cref="MapCullingSystem"/>'s <c>ChangeGate</c> watches, so the culling pass is skipped while the camera is still.</summary>
    public long Version { get; private set; }

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
