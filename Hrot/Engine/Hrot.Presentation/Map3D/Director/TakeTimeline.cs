using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>A shot placed in sim time: it is held from <see cref="Start"/> to <see cref="End"/>.</summary>
public sealed record TimelineShot(int Number, ShotSpec Shot, double Start, double End)
{
    public double Seconds => End - Start;
}

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §0, §3a) — a take laid out in SIM time from its anchor times. As <c>film.py</c>'s plan does,
/// a shot starts at its <c>at</c>, or when the shot before it ends if that is later; the sim runs on between shots (on film the gap is not
/// filmed). ⭐ A shot whose anchor has no time yet is left out and named (<see cref="Unresolved"/>); ⛔ a replay shot is skipped in slice 1
/// (R-4061) and named (<see cref="SkippedReplays"/>).
/// </summary>
public sealed class TakeTimeline
{
    private TakeTimeline(IReadOnlyList<TimelineShot> shots, double end, IReadOnlyList<string> unresolved, IReadOnlyList<int> replays)
    {
        Shots = shots;
        End = end;
        Unresolved = unresolved;
        SkippedReplays = replays;
    }

    public IReadOnlyList<TimelineShot> Shots { get; }

    /// <summary>The sim time the take ends (its <c>endAt</c>, else the last shot's end).</summary>
    public double End { get; }

    /// <summary>"shot 2: anchor 'fire'" — shots left out because an anchor's time is not known.</summary>
    public IReadOnlyList<string> Unresolved { get; }

    /// <summary>The numbers of the replay shots skipped.</summary>
    public IReadOnlyList<int> SkippedReplays { get; }

    /// <summary>The first sim time a shot is held (0 when there is none).</summary>
    public double FirstStart => Shots.Count > 0 ? Shots[0].Start : 0d;

    /// <summary>
    /// Lays <paramref name="take"/> out with <paramref name="anchorTime"/>'s times; <paramref name="from"/> / <paramref name="to"/> (a reel
    /// item's trim) keep only the shots within them.
    /// </summary>
    public static TakeTimeline Plan(Take take, Func<string, double?> anchorTime, AtExpr? from = null, AtExpr? to = null)
    {
        var shots = new List<TimelineShot>();
        var unresolved = new List<string>();
        var replays = new List<int>();
        double? lo = from?.Resolve(anchorTime), hi = to?.Resolve(anchorTime);
        double lastEnd = 0d;
        for (int i = 0; i < take.Shots.Count; i++)
        {
            var s = take.Shots[i];
            if (s.Replay != null) { replays.Add(i + 1); continue; }
            if (s.At.Resolve(anchorTime) is not { } at) { unresolved.Add($"shot {i + 1}: anchor '{s.At.Anchor}'"); continue; }
            double start = Math.Max(at, lastEnd);
            double end = start + s.Seconds;
            if (lo is { } a && start < a - 1e-9) continue;
            if (hi is { } b && start >= b) continue;
            if (hi is { } c) end = Math.Min(end, c);
            shots.Add(new TimelineShot(i + 1, s, start, end));
            lastEnd = end;
        }
        double takeEnd = take.EndAt?.Resolve(anchorTime) is { } e && (hi is null || e <= hi) ? Math.Max(e, 0d) : lastEnd;
        if (hi is { } h) takeEnd = Math.Min(Math.Max(takeEnd, lastEnd), h);
        return new TakeTimeline(shots, Math.Max(takeEnd, lastEnd), unresolved, replays);
    }

    /// <summary>The shot held at <paramref name="simTime"/>, or null in a gap.</summary>
    public TimelineShot? At(double simTime)
    {
        foreach (var s in Shots)
            if (simTime >= s.Start - 1e-9 && simTime < s.End - 1e-9) return s;
        return null;
    }

    /// <summary>The next shot to start after <paramref name="simTime"/>, or null.</summary>
    public TimelineShot? Next(double simTime) => Shots.FirstOrDefault(s => s.Start > simTime + 1e-9);
}
