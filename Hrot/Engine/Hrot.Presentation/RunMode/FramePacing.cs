namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ <c>CE-4316</c> (lifecycle LC-10, the user's option A) — the ONE seam that lifts the window's frame cap while <i>Run to …</i> steps the cluster
/// (a step is retired per rendered frame, so the cap IS the speed), and puts it back. The window host records its cap when it opens the window
/// (<see cref="Register"/>); a host with no window (headless, a rail) registers nothing and the calls do nothing.
/// </summary>
public static class FramePacing
{
    private static System.Action<int>? _setTargetFps;
    private static int _normalFps;

    /// <summary>Is the cap lifted now?</summary>
    public static bool Unthrottled { get; private set; }

    /// <summary>The window host: <paramref name="setTargetFps"/> sets the window's cap (0 = none), <paramref name="normalFps"/> is its usual one.</summary>
    public static void Register(System.Action<int> setTargetFps, int normalFps)
    {
        _setTargetFps = setTargetFps;
        _normalFps = normalFps;
    }

    /// <summary>Lifts (<c>true</c>) or restores (<c>false</c>) the cap.</summary>
    public static void SetUnthrottled(bool on)
    {
        if (Unthrottled == on) return;
        Unthrottled = on;
        _setTargetFps?.Invoke(on ? 0 : _normalFps);
    }
}
