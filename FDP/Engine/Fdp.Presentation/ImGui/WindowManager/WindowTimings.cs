using System.Diagnostics;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐ W-P1 (ui lane) — opt-in per-window CPU timing, the "where does the UI time go" half of <c>HROT_FRAME_STATS=1</c>: the frame stats said <i>ui 995 ms/frame</i> and could not say which window. <see cref="Enabled"/> is
/// switched on by the host's frame stats; <see cref="WindowManager.Render"/> times each <c>ManagedWindow.Render</c> (title bar + client area, the whole ImGui build of it) and the host's overlays, and a caller may time any named
/// part with <see cref="Scope"/>. Off ⇒ one static bool read per window, no allocation. <see cref="Drain"/> hands the accumulated (name, total ms, calls) and clears.
/// </summary>
public static class WindowTimings
{
    private static readonly Dictionary<string, (double Ms, int Calls, long Bytes)> s_acc = new();
    // ⭐ CE-2401 (R-2084) — the bytes each timed part ALLOCATES, beside its time: Start pushes the thread's allocation counter, Record pops it
    //   (Start/Record pair LIFO, as every caller nests them). The UI was ≈ 650 KB of garbage per frame and could not say which window.
    private static readonly long[] s_bytes = new long[32];
    private static int s_depth;
    /// <summary>On while a frame-stats log is wanted.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Starts a timestamp (0 when off).</summary>
    public static long Start()
    {
        if (!Enabled) return 0;
        if (s_depth < s_bytes.Length) s_bytes[s_depth] = GC.GetAllocatedBytesForCurrentThread();
        s_depth++;
        return Stopwatch.GetTimestamp();
    }

    /// <summary>Adds the time since <paramref name="start"/> to <paramref name="name"/> (no-op when off).</summary>
    public static void Record(string name, long start)
    {
        if (start == 0) return;
        if (s_depth > 0) s_depth--;
        if (!Enabled) return;
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        long bytes = s_depth < s_bytes.Length ? GC.GetAllocatedBytesForCurrentThread() - s_bytes[s_depth] : 0;
        s_acc[name] = s_acc.TryGetValue(name, out var a) ? (a.Ms + ms, a.Calls + 1, a.Bytes + bytes) : (ms, 1, bytes);
    }

    /// <summary>The accumulated timings, largest first; the accumulator is cleared.</summary>
    public static IReadOnlyList<(string Name, double Ms, int Calls)> Drain()
        => DrainWithBytes().Select(x => (x.Name, x.Ms, x.Calls)).ToList();

    /// <summary>⭐ CE-2401 — as <see cref="Drain"/>, with the bytes each part allocated in the window (largest time first).</summary>
    public static IReadOnlyList<(string Name, double Ms, int Calls, long Bytes)> DrainWithBytes()
    {
        var list = s_acc.Select(k => (k.Key, k.Value.Ms, k.Value.Calls, k.Value.Bytes)).OrderByDescending(x => x.Ms).ToList();
        s_acc.Clear();
        return list;
    }
}
