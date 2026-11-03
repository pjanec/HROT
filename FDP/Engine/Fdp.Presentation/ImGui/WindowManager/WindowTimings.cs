using System.Diagnostics;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐ W-P1 (ui lane) — opt-in per-window CPU timing, the "where does the UI time go" half of <c>HROT_FRAME_STATS=1</c>: the frame stats said <i>ui 995 ms/frame</i> and could not say which window. <see cref="Enabled"/> is
/// switched on by the host's frame stats; <see cref="WindowManager.Render"/> times each <c>ManagedWindow.Render</c> (title bar + client area, the whole ImGui build of it) and the host's overlays, and a caller may time any named
/// part with <see cref="Scope"/>. Off ⇒ one static bool read per window, no allocation. <see cref="Drain"/> hands the accumulated (name, total ms, calls) and clears.
/// </summary>
public static class WindowTimings
{
    private static readonly Dictionary<string, (double Ms, int Calls)> s_acc = new();
    /// <summary>On while a frame-stats log is wanted.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Starts a timestamp (0 when off).</summary>
    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    /// <summary>Adds the time since <paramref name="start"/> to <paramref name="name"/> (no-op when off).</summary>
    public static void Record(string name, long start)
    {
        if (!Enabled || start == 0) return;
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        s_acc[name] = s_acc.TryGetValue(name, out var a) ? (a.Ms + ms, a.Calls + 1) : (ms, 1);
    }

    /// <summary>The accumulated timings, largest first; the accumulator is cleared.</summary>
    public static IReadOnlyList<(string Name, double Ms, int Calls)> Drain()
    {
        var list = s_acc.Select(k => (k.Key, k.Value.Ms, k.Value.Calls)).OrderByDescending(x => x.Ms).ToList();
        s_acc.Clear();
        return list;
    }
}
