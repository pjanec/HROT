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
/// (0 = none, 1 = full) in a dip. <see cref="Since"/> is the sim time since the key the camera LEAVES (<see cref="From"/> while flying, else
/// <see cref="Key"/>) — the time an orbiting camera has turned for.
/// </summary>
public sealed record ProgramFrame(CameraKey Key, CameraKey? From, float Blend, float Fade, double Since = 0d);

/// <summary>
/// ⭐ <c>CE-1303</c> P2b — CR-7 (§11o) — one segment of the film placed in FILM time: source <see cref="From"/>…<see cref="To"/> played at
/// <see cref="Speed"/> sim seconds per film second from film second <see cref="FilmStart"/>. <see cref="Back"/>: it starts before where the segment
/// before it ended — an instant replay (a seekable source seeks back; a live one cannot).
/// </summary>
public sealed record FilmSegment(int Number, double From, double To, double Speed, SegmentGap Gap, double FilmStart, bool Back)
{
    public double FilmSeconds => (To - From) / Speed;
    public double FilmEnd => FilmStart + FilmSeconds;

    /// <summary>The film second of source second <paramref name="sourceSim"/> inside this segment (clamped to it).</summary>
    public double FilmOf(double sourceSim) => FilmStart + (Math.Clamp(sourceSim, From, To) - From) / Speed;
}

/// <summary>
/// ⭐⭐ <c>CE-1303</c> P2b — CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11o S1) — THE one place FILM time meets SOURCE time. A take's
/// <see cref="Take.Segments"/>, in LIST order, are the film: each plays its source stretch at its speed, one after the other; what lies between two is
/// skipped (a seek, or a live fast-forward) and a segment that goes BACK in source time is a replay. ⭐ A take with no segments is the IDENTITY (film
/// second = source second — every take before CR-7). A segment whose times do not resolve, or with <c>from ≥ to</c>, is left out and named.
/// <para>⛔ Not <c>ReplayTimeMap</c> (wall ↔ sim): that map is monotonic; a replay makes film → source many-to-one.</para>
/// </summary>
public sealed class FilmTimeMap
{
    /// <summary>The black on each side of a <c>dip</c> gap, in film seconds (the out-half before the segment, the in-half after its start).</summary>
    public const double DipSeconds = 0.4d;

    public static readonly FilmTimeMap Identity = new(Array.Empty<FilmSegment>(), Array.Empty<string>());

    private FilmTimeMap(IReadOnlyList<FilmSegment> segments, IReadOnlyList<string> unresolved) { Segments = segments; Unresolved = unresolved; }

    public IReadOnlyList<FilmSegment> Segments { get; }

    /// <summary>"segment 2: anchor 'fire'" · "segment 3: from 5 is not before to 4" — segments left out.</summary>
    public IReadOnlyList<string> Unresolved { get; }

    /// <summary>No segment: film time IS source time.</summary>
    public bool IsIdentity => Segments.Count == 0;

    /// <summary>The film's length (∞ for the identity: the take's own end decides).</summary>
    public double FilmEnd => IsIdentity ? double.PositiveInfinity : Segments[^1].FilmEnd;

    public static FilmTimeMap Of(Take take, Func<string, double?> anchorTime)
    {
        if (take.Segments.Count == 0) return Identity;
        var placed = new List<FilmSegment>();
        var unresolved = new List<string>();
        double film = 0d;
        double? lastTo = null;
        for (int i = 0; i < take.Segments.Count; i++)
        {
            var s = take.Segments[i];
            if (s.From.Resolve(anchorTime) is not { } from || s.To.Resolve(anchorTime) is not { } to)
            {
                unresolved.Add($"segment {i + 1}: anchor '{(s.From.Resolve(anchorTime) is null ? s.From.Anchor : s.To.Anchor)}'");
                continue;
            }
            if (!(to > from + 1e-9) || !(s.Speed > 0)) { unresolved.Add($"segment {i + 1}: from {from:0.###} is not before to {to:0.###}"); continue; }
            var seg = new FilmSegment(i + 1, from, to, s.Speed, s.Gap, film, lastTo is { } prev && from < prev - 1e-6);
            placed.Add(seg);
            film = seg.FilmEnd;
            lastTo = to;
        }
        return new FilmTimeMap(placed, unresolved);
    }

    /// <summary>The segment playing at film second <paramref name="filmTime"/> and its source second (the identity: segment −1, the same second);
    /// before the first: the first's start; at or past the end: null.</summary>
    public (int Segment, double Source)? At(double filmTime)
    {
        if (IsIdentity) return (-1, filmTime);
        if (filmTime < Segments[0].FilmStart) return (0, Segments[0].From);
        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            if (filmTime < s.FilmEnd - 1e-9) return (i, s.From + (filmTime - s.FilmStart) * s.Speed);
        }
        return null;
    }

    /// <summary>
    /// The film second that shows source second <paramref name="sourceSim"/> — the FIRST segment (from <paramref name="fromSegment"/> on) that holds it;
    /// in a skipped stretch, the start of the next segment after it; past every segment, the film's end. The identity: the same second.
    /// </summary>
    public double FilmOf(double sourceSim, int fromSegment = 0)
    {
        if (IsIdentity) return sourceSim;
        for (int i = Math.Max(fromSegment, 0); i < Segments.Count; i++)
            if (sourceSim >= Segments[i].From - 1e-9 && sourceSim < Segments[i].To - 1e-9) return Segments[i].FilmOf(sourceSim);
        for (int i = Math.Max(fromSegment, 0); i < Segments.Count; i++)
            if (Segments[i].From > sourceSim) return Segments[i].FilmStart;
        return FilmEnd;
    }

    /// <summary>The black of a <c>dip</c> gap at film second <paramref name="filmTime"/>: it rises over <see cref="DipSeconds"/> before a dip
    /// segment's start (the segment before it still on screen) and clears over the same after it.</summary>
    public float GapFade(double filmTime)
    {
        double fade = 0d;
        for (int i = 1; i < Segments.Count; i++)
        {
            var s = Segments[i];
            if (s.Gap != SegmentGap.Dip) continue;
            double d = filmTime - s.FilmStart;
            if (d < 0d && d > -DipSeconds) fade = Math.Max(fade, 1d + d / DipSeconds);
            else if (d >= 0d && d < DipSeconds) fade = Math.Max(fade, 1d - d / DipSeconds);
        }
        return (float)Math.Clamp(fade, 0d, 1d);
    }
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
        var specs = take.Shots.Count == 0 && take.Keys.Count > 0 ? Stretches(take, anchorTime, unresolved) : take.Shots;
        for (int i = 0; i < specs.Count; i++)
        {
            var s = specs[i];
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

    /// <summary>
    /// ⭐ <c>T4</c> — a take that has KEYS and no shots (format v2 written by the editor) still needs the stretches the film runs in (what gates
    /// captions, layers, speed and the capture): one per placed key, from its time to the next key's (the last to the take's end, else 3 s). The
    /// CAMERA never comes from these — that is <see cref="ProgramAt"/>'s; a key whose anchor has no time is named in <paramref name="unresolved"/>.
    /// </summary>
    private static List<ShotSpec> Stretches(Take take, Func<string, double?> anchorTime, List<string> unresolved)
    {
        var placed = new List<(double At, CameraKey Key)>();
        for (int i = 0; i < take.Keys.Count; i++)
        {
            if (take.Keys[i].At.Resolve(anchorTime) is { } at) placed.Add((at, take.Keys[i]));
            else unresolved.Add($"key {i + 1}: anchor '{take.Keys[i].At.Anchor}'");
        }
        placed = placed.OrderBy(p => p.At).ToList();
        double? end = take.EndAt?.Resolve(anchorTime);
        var list = new List<ShotSpec>();
        for (int j = 0; j < placed.Count; j++)
        {
            double until = j + 1 < placed.Count ? placed[j + 1].At : end is { } e && e > placed[j].At + 1e-9 ? e : placed[j].At + 3d;
            if (until - placed[j].At <= 1e-9) continue;
            list.Add(new ShotSpec { At = new AtExpr(placed[j].At), Seconds = until - placed[j].At, Camera = placed[j].Key.Camera });
        }
        return list;
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
    /// is at or before it; null before the first. A key whose anchor has no time is ignored. ⭐ A key is where the camera ARRIVES (TE-8): a fly key
    /// blends from the key before it over its seconds ENDING at its time (clamped to start no earlier than that key); a dip key's black rises over
    /// its out-seconds BEFORE its time (the previous key still on screen) and falls over its in-seconds after.
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
        var (at0, key) = placed[cur];
        CameraKey? from = null;
        CameraKey shown = key;
        float blend = 1f;
        if (cur + 1 < placed.Count)
        {
            var (nextAt, nextKey) = placed[cur + 1];
            switch (nextKey.In)
            {
                // the black that rises BEFORE the next key, when that key is a dip (the key on screen stays)
                case { Kind: KeyTransitionKind.Dip, OutSeconds: > 0 } dip:
                    double begin = nextAt - dip.OutSeconds;
                    if (simTime > begin) fade = (simTime - begin) / dip.OutSeconds;
                    break;
                // the fly INTO the next key: it arrives at its time, after at most its seconds (never before the key it leaves)
                case { Kind: KeyTransitionKind.Fly, Seconds: > 0 } fly:
                    double start = Math.Max(nextAt - fly.Seconds, at0);
                    if (simTime > start + 1e-9)
                    {
                        from = key;
                        shown = nextKey;
                        blend = ShotEvaluator.Eased(fly.Ease, (float)Math.Clamp((simTime - start) / (nextAt - start), 0d, 1d));
                    }
                    break;
            }
        }

        double since = Math.Max(simTime - at0, 0d);
        if (key.In is { Kind: KeyTransitionKind.Dip, InSeconds: > 0 } back && since < back.InSeconds)
            fade = Math.Max(fade, 1d - since / back.InSeconds);   // the black that clears AFTER a dip key
        return new ProgramFrame(shown, from, blend, (float)Math.Clamp(fade, 0d, 1d), since);
    }

    /// <summary>The next shot to start after <paramref name="simTime"/>, or null.</summary>
    public TimelineShot? Next(double simTime) => Shots.FirstOrDefault(s => s.Start > simTime + 1e-9);
}
