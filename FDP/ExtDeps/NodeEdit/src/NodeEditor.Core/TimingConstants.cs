namespace NodeEditor.Core;

/// <summary>
/// Centralized timing constants. All animation durations and thresholds
/// live here so they're tweakable without spelunking through render code.
/// </summary>
public static class TimingConstants
{
    /// <summary>Mouse-down to drag threshold in pixels.</summary>
    public const float DragThresholdPixels = 4f;

    /// <summary>Snap-to-pin radius in canvas pixels during wire drag.</summary>
    public const float SnapToPinRadius = 14f;

    /// <summary>Tooltip appears after this delay.</summary>
    public static readonly TimeSpan TooltipDelay = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan TooltipFade = TimeSpan.FromMilliseconds(80);

    /// <summary>Camera animation when frame-to-target.</summary>
    public static readonly TimeSpan FrameAnimDuration = TimeSpan.FromMilliseconds(180);

    /// <summary>Wire connect animation.</summary>
    public static readonly TimeSpan WireConnectSnap = TimeSpan.FromMilliseconds(120);

    /// <summary>Wire disconnect recoil animation.</summary>
    public static readonly TimeSpan WireDisconnectRecoil = TimeSpan.FromMilliseconds(120);

    /// <summary>Reroute insertion scale-in.</summary>
    public static readonly TimeSpan RerouteScaleIn = TimeSpan.FromMilliseconds(100);

    /// <summary>Node creation fade-in.</summary>
    public static readonly TimeSpan NodeCreateFadeIn = TimeSpan.FromMilliseconds(100);

    /// <summary>Node deletion fade-out.</summary>
    public static readonly TimeSpan NodeDeleteFadeOut = TimeSpan.FromMilliseconds(80);

    /// <summary>Wire flow animation loop period (debug viz).</summary>
    public static readonly TimeSpan WireFlowLoop = TimeSpan.FromMilliseconds(400);

    /// <summary>Executing-node pulse period (debug viz).</summary>
    public static readonly TimeSpan ExecutingPulse = TimeSpan.FromMilliseconds(500);

    /// <summary>After-glow on recently-executed wire/node.</summary>
    public static readonly TimeSpan RecentlyExecutedFade = TimeSpan.FromMilliseconds(800);

    /// <summary>Pan inertia after release.</summary>
    public static readonly TimeSpan PanInertia = TimeSpan.FromMilliseconds(250);

    /// <summary>Popup open/close animation.</summary>
    public static readonly TimeSpan PopupOpen = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan PopupClose = TimeSpan.FromMilliseconds(80);

    /// <summary>Toast notification visible duration.</summary>
    public static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(3);

    /// <summary>Hot reload badge fade window.</summary>
    public static readonly TimeSpan ReloadBadgeFade = TimeSpan.FromSeconds(2);

    /// <summary>Minimum/maximum camera zoom factor.</summary>
    public const float MinZoom = 0.25f;
    public const float MaxZoom = 3.0f;

    /// <summary>Below this zoom, render simplified.</summary>
    public const float LowZoomThreshold = 0.5f;
}
