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
/// ⭐ <c>TE-2</c> / <c>TE-8</c> (docs/DESIGN_Director_Timeline_Editor.md §10 T1) — what the program shows at one sim time: <see cref="Key"/> is
/// the key on screen; while a fly INTO it is under way <see cref="From"/> is the key before it and <see cref="Blend"/> (0…1, eased) the way
/// from <see cref="From"/> to <see cref="Key"/> (otherwise <see cref="From"/> is null and Blend 1); <see cref="Fade"/> is the black over it
/// (0 = none, 1 = full) in a dip.
/// </summary>
public sealed record ProgramFrame(CameraKey Key, CameraKey? From, float Blend, float Fade);

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

    /// <summary>
    /// ⭐ <c>TE-2</c> / <c>TE-8</c> — the program at <paramref name="simTime"/>: the LAST key whose time (resolved through <paramref name="anchorTime"/>)
    /// is at or before it; null before the first. A key whose anchor has no time is ignored. A fly key blends from the key before it over its
    /// seconds; a dip key's black rises over its out-seconds BEFORE its time (the previous key still on screen) and falls over its in-seconds after.
    /// </summary>
    public static ProgramFrame? ProgramAt(IReadOnlyList<CameraKey> keys, Func<string, double?> anchorTime, double simTime)
    {
        var placed = new List<(double At, CameraKey Key)>(keys.Count);
        foreach (var k in keys)
            if (k.At.Resolve(anchorTime) is { } at) placed.Add((at, k));
        placed = placed.OrderBy(p => p.At).ToList();   // stable: keys at one time keep their order, the last one wins

        int cur = -1;
        for (int i = 0; i < placed.Count; i++)
            if (placed[i].At <= simTime + 1e-9) cur = i; else break;
        if (cur < 0) return null;

        double fade = 0d;
        // the black that rises BEFORE the next key, when that key is a dip
        if (cur + 1 < placed.Count && placed[cur + 1].Key.In is { Kind: KeyTransitionKind.Dip, OutSeconds: > 0 } next)
        {
            double begin = placed[cur + 1].At - next.OutSeconds;
            if (simTime > begin) fade = (simTime - begin) / next.OutSeconds;
        }

        var (at0, key) = placed[cur];
        double since = Math.Max(simTime - at0, 0d);
        CameraKey? from = null;
        float blend = 1f;
        switch (key.In.Kind)
        {
            case KeyTransitionKind.Fly when key.In.Seconds > 0 && since < key.In.Seconds && cur > 0:
                from = placed[cur - 1].Key;
                blend = ShotEvaluator.Eased(key.In.Ease, (float)(since / key.In.Seconds));
                break;
            case KeyTransitionKind.Dip when key.In.InSeconds > 0 && since < key.In.InSeconds:
                fade = Math.Max(fade, 1d - since / key.In.InSeconds);
                break;
        }
        return new ProgramFrame(key, from, blend, (float)Math.Clamp(fade, 0d, 1d));
    }

    /// <summary>The next shot to start after <paramref name="simTime"/>, or null.</summary>
    public TimelineShot? Next(double simTime) => Shots.FirstOrDefault(s => s.Start > simTime + 1e-9);
}
