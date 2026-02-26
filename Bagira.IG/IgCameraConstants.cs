namespace Bagira.IG;

/// <summary>
/// Named constants for IG camera configuration.
/// Referenced by IgApplication and Bagira.IG.Tests — changing a value here propagates everywhere.
/// </summary>
public static class IgCameraConstants
{
    // --- Initial state ---
    public const float InitialPositionX = 5000f;
    public const float InitialPositionY = 5000f;
    public const float InitialZoom     = 0.5f;  // 2 m/px

    // --- Zoom limits ---
    public const float MinZoom = 0.01f; // 100 m/px (most zoomed out)
    public const float MaxZoom = 5.0f;  // 0.2 m/px (most zoomed in)

    // --- Zoom input ---
    /// <summary>Multiplicative factor applied per mouse-wheel tick (1.2 = 20% zoom per tick).</summary>
    public const float ZoomFactor = 1.2f;

    /// <summary>
    /// Value passed to MapCamera.ZoomSpeed to achieve ZoomFactor per tick.
    /// MapCamera formula: newZoom = targetZoom * (1 + ZoomSpeed * wheel)
    /// For wheel=1: newZoom = targetZoom * (1 + ZoomSpeedPerTick) = targetZoom * ZoomFactor
    /// Therefore: ZoomSpeedPerTick = ZoomFactor - 1.0 = 0.2
    /// </summary>
    public const float ZoomSpeedPerTick = 0.2f; // = ZoomFactor - 1.0f

    // --- Pan input ---
    public const float ArrowKeyPanSpeedMetersPerSecond = 10f;
}
