using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>What the ACTOR of a moment is, for the camera's height (a person is shot at eye level, a group from above): the caller knows, the index does not.</summary>
public enum SubjectKind { Person, Vehicle, Aircraft, Group, Unknown }

/// <summary>
/// ⭐ <c>CE-4059</c> / <c>CR-8</c> (docs/DESIGN_Director_Timeline_Editor.md §11n) — one candidate moment of a recording: the index event
/// (<see cref="Lane"/>, <see cref="EventIndex"/>, <see cref="Frame"/>, <see cref="Sim"/>), who acted and at whom (network ids; <see cref="Subject"/> is null
/// when the moment has no second party) and how film-worthy it is (<see cref="Score"/>).
/// </summary>
public sealed record DraftMoment(string Lane, int EventIndex, int Frame, double Sim, long Actor, long? Subject, double Score);

/// <summary>⭐ <c>CE-4059</c> / <c>CR-8</c> — what the drafted take is called and how it is framed.</summary>
public sealed record DraftOptions
{
    public string Scenario { get; init; } = "";
    public string Name { get; init; } = "draft";
    public string? Caption { get; init; }
    /// <summary>The actor's kind, for the camera's height; null (or a kind it does not know) = <see cref="SubjectKind.Unknown"/>.</summary>
    public Func<long, SubjectKind>? Kind { get; init; }
    /// <summary>Add a slow-motion replay from the reverse angle (a fast beat only: shot, hit, blast).</summary>
    public bool Replay { get; init; }
    public double AspectRatio { get; init; } = 16d / 9d;
    /// <summary>The (vertical) lens of every key but a telephoto.</summary>
    public float FovDeg { get; init; } = 45f;
    public int TickHz { get; init; } = 60;
}

/// <summary>
/// ⭐ <c>CE-4059</c> / <c>CR-8</c> (docs/DESIGN_Director_Timeline_Editor.md §11n; the grammar is docs/PLAN_Feature_Films.md §1a) — ONE pure function from a
/// MOMENT of a recording (an event of its <see cref="TimelineIndex"/>) to a <see cref="Take"/> that films it: establish · set-up · the moment ·
/// consequence (· a slow replay from the reverse angle). No world, no studio, no I/O: it RETURNS a take, which the take session opens like any other.
/// <para>Every key's time is an <see cref="AtExpr"/> on the MOMENT's anchor (<see cref="EventAnchors"/> names it — the one naming scheme), so the take holds
/// when the run's timing moves; the moment's index time is baked. A moment <see cref="EventAnchors"/> cannot express falls back to FIXED times.</para>
/// <para>No key holds longer than 6 s of source time <b>by construction</b> (the longest is the establish shot of a late moment, &lt; 5.3 s).</para>
/// </summary>
public static class Drafter
{
    private const string Shot = "combat.shot", Hit = "combat.hit", Blast = "combat.blast";

    private const double EstablishBefore = 8d, SetUpBefore = 4d, MomentBefore = 0.7d, MomentAfter = 2d;
    private const double ConsequenceAfter = 2d, ConsequenceHold = 4d, ConsequenceSearch = 5d, ConsequenceLead = 0.3d, ConsequenceMinAfterMoment = 0.5d;
    private const double MinEstablish = 2d, MinSetUp = 2d;
    private const double ReplayFrom = -1d, ReplayTo = 1.5d, ReplaySpeed = 0.4d;
    private const double MergeWithin = 2d, ShooterLookback = 5d;
    private const double TelephotoMetres = 100d;
    private const float TelephotoFov = 18f, EstablishPitch = -40f, ConsequencePitch = -35f;
    private const float AcrossEstablish = 20f, AcrossMoment = 20f, AcrossTelephoto = 60f, AcrossConsequence = 45f;
    private const double Margin = 1.3d, MinDistance = 8d, MaxDistance = 600d;
    /// <summary>The half-width (m) a lone unit takes in the frame, so a unit that does not move still gets a sensible distance.</summary>
    private const double MinHalfWidth = 6d;
    private const double Rad = Math.PI / 180d;

    /// <summary>
    /// The candidates, scored, in time order: every EXPRESSIBLE event of <c>combat.shot</c> (the FIRST round of each shooter at each target, 1) ·
    /// <c>combat.hit</c> (2) · <c>combat.blast</c> (3 + damage / 100). A moment within 2 s AFTER another merges into it (the earlier one is the cause and
    /// anchors the take; the merged score is the better of the two). ⏭ Behaviour lanes join when CR-5 grows the anchor cues.
    /// </summary>
    public static IReadOnlyList<DraftMoment> Moments(TimelineIndex index, Func<long, string?> unitName)
    {
        var found = new List<DraftMoment>();

        var shots = index.Lane(Shot);
        var seen = new HashSet<(long, long)>();
        for (int i = 0; i < shots.Count; i++)
        {
            var e = shots[i];
            if (!seen.Add((e.Subject, e.Other))) continue;
            if (EventAnchors.Of(index, Shot, i, unitName) is not { } anchor) continue;
            found.Add(new DraftMoment(Shot, i, e.Frame, anchor.EventSim, e.Subject, e.Other > 0 ? e.Other : null, 1d));
        }

        var hits = index.Lane(Hit);
        for (int i = 0; i < hits.Count; i++)
        {
            var e = hits[i];
            if (EventAnchors.Of(index, Hit, i, unitName) is not { } anchor) continue;
            long shooter = ShooterOf(index, e.Subject, e.Frame, anchor.EventSim);
            found.Add(shooter > 0
                ? new DraftMoment(Hit, i, e.Frame, anchor.EventSim, shooter, e.Subject, 2d)
                : new DraftMoment(Hit, i, e.Frame, anchor.EventSim, e.Subject, null, 2d));
        }

        var blasts = index.Lane(Blast);
        for (int i = 0; i < blasts.Count; i++)
        {
            var e = blasts[i];
            if (EventAnchors.Of(index, Blast, i, unitName) is not { } anchor) continue;
            double score = 3d + e.Value / 100d;
            found.Add(e.Subject > 0
                ? new DraftMoment(Blast, i, e.Frame, anchor.EventSim, e.Subject, e.Other > 0 ? e.Other : null, score)
                : new DraftMoment(Blast, i, e.Frame, anchor.EventSim, Math.Max(e.Other, 0L), null, score));
        }

        var merged = new List<DraftMoment>();
        foreach (var moment in found.OrderBy(m => m.Sim))   // stable: events at one time keep their lane order
        {
            if (merged.Count > 0 && moment.Sim - merged[^1].Sim <= MergeWithin + 1e-9)
                merged[^1] = merged[^1] with { Score = Math.Max(merged[^1].Score, moment.Score) };
            else
                merged.Add(moment);
        }
        return merged;
    }

    /// <summary>The unit that last shot at <paramref name="target"/> within 5 s before <paramref name="sim"/> (0 when none): who a hit was by.</summary>
    private static long ShooterOf(TimelineIndex index, long target, int frame, double sim)
    {
        var shots = index.Lane(Shot);
        for (int i = index.Before(Shot, frame + 1) - 1; i >= 0; i--)
        {
            double at = SimOf(index, shots[i].Frame);
            if (!double.IsNaN(at) && sim - at > ShooterLookback) break;
            if (shots[i].Other == target && shots[i].Subject > 0) return shots[i].Subject;
        }
        return 0;
    }

    /// <summary>
    /// The take that films <paramref name="moment"/>: ① establish (<c>t−8</c>, 4 s, wide and high) · ② set-up (<c>t−4</c>, over the actor's shoulder) ·
    /// ③ the moment (<c>t−0.7</c>, framed across the line — a telephoto follow past 100 m) · ④ consequence (<c>t+2</c>, or 0.3 s before the next hit / blast on
    /// the subject within 5 s, from above) · ⑤ with <see cref="DraftOptions.Replay"/> a slow replay from the other side of the line.
    /// </summary>
    public static Take Draft(TimelineIndex index, DraftMoment moment, Func<long, string?> unitName, DraftOptions options)
    {
        double t = moment.Sim;
        var anchor = EventAnchors.Of(index, moment.Lane, moment.EventIndex, unitName);
        var scene = new Scene(index, moment);
        long focusId = moment.Subject ?? moment.Actor;
        string? actorName = unitName(moment.Actor), subjectName = moment.Subject is { } sj ? unitName(sj) : null;
        string? focusName = subjectName ?? (moment.Subject is null ? actorName : null);
        var kind = options.Kind?.Invoke(moment.Actor) ?? SubjectKind.Unknown;
        float momentPitch = PitchOf(kind);

        // time: a key is `t + offset` on the moment's anchor; ① and ③ that would fall before the run's start are a FIXED time 0 (nothing earlier to hang on)
        AtExpr Key(double offset) => t + offset < 0d || anchor is null ? new AtExpr(Math.Max(t + offset, 0d)) : new AtExpr(offset, anchor.Name);
        double off1 = -Math.Min(EstablishBefore, t), off2 = -SetUpBefore, off3 = -Math.Min(MomentBefore, t);
        bool hasSetUp = off2 - off1 >= MinSetUp - 1e-9;
        bool hasEstablish = hasSetUp || off3 - off1 >= MinEstablish - 1e-9;
        double off4 = ConsequenceOffset(index, moment, focusId, t) is { } next
            ? Math.Max(next - ConsequenceLead, off3 + ConsequenceMinAfterMoment)
            : ConsequenceAfter;
        double end4 = off4 + ConsequenceHold;

        // the 180° line: actor → subject (or the way the actor faces); the side is chosen ONCE, ①③④ stay on it, only ⑤ crosses
        var a0 = scene.Actor(t);
        var s0 = scene.HasSubject ? scene.Focus(t) : a0;
        var line = new Vector2(s0.X - a0.X, s0.Y - a0.Y);
        if (line.Length() < 0.5f)
        {
            float heading = scene.HeadingOf(moment.Actor, t);
            line = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        }
        double lineDeg = Math.Atan2(line.Y, line.X) / Rad;
        int side = Math.Cos(lineDeg * Rad) >= 0d ? 1 : -1;   // the camera looks north-ish: the map stays the way up
        float Across(float alphaDeg) => Wrap((float)(side > 0 ? lineDeg + 90d - alphaDeg : lineDeg - 90d + alphaDeg));

        var keys = new List<CameraKey>();
        CameraKey Cut(AtExpr at, CameraSpec camera) => new() { At = at, Camera = camera with { Fov = camera.Fov ?? options.FovDeg }, Fov = camera.Fov ?? options.FovDeg, In = KeyTransition.Cut };

        if (hasEstablish)
        {
            double until = hasSetUp ? off2 : off3;
            keys.Add(Cut(t - EstablishBefore < 0d ? new AtExpr(0d) : Key(off1), Midpoint(scene, t + off1, t + until, Across(AcrossEstablish), EstablishPitch, options)));
        }

        if (hasSetUp)
        {
            keys.Add(Cut(Key(off2), subjectName is not null && actorName is not null
                ? new CameraSpec { Over = actorName, Toward = subjectName }
                : actorName is not null
                    ? new CameraSpec { Follow = actorName, Distance = 12f, Yaw = Across(AcrossMoment), Pitch = momentPitch }
                    : Midpoint(scene, t + off2, t + off3, Across(AcrossMoment), momentPitch, options)));
        }

        // ③ the moment — the same framing is the replay's (from the other side)
        CameraSpec TheMoment(double from, double to, float yaw)
        {
            bool far = scene.HasSubject && Vector2.Distance(new Vector2(a0.X, a0.Y), new Vector2(s0.X, s0.Y)) > TelephotoMetres;
            if (!far) return Midpoint(scene, from, to, yaw, momentPitch, options);
            yaw = Wrap(yaw + (side > 0 ? -1 : 1) * (AcrossTelephoto - AcrossMoment));   // more along the line: from the actor's side, over his shoulder
            return subjectName is not null
                ? Followed(scene, subjectName, from, to, yaw, momentPitch, TelephotoFov, options)
                : Midpoint(scene, from, to, yaw, momentPitch, options) with { Fov = TelephotoFov };
        }
        keys.Add(Cut(t - MomentBefore < 0d ? new AtExpr(0d) : Key(off3), TheMoment(t + off3, t + MomentAfter, Across(AcrossMoment))));

        // ④ the consequence — the subject from above, on the same side
        keys.Add(Cut(Key(off4), focusName is not null
            ? Followed(scene, focusName, t + off4, t + end4, Across(AcrossConsequence), ConsequencePitch, null, options)
            : Midpoint(scene, t + off4, t + end4, Across(AcrossConsequence), ConsequencePitch, options)));

        var segments = new List<TimeSegment>();
        if (options.Replay && moment.Lane is Shot or Hit or Blast)
        {
            // ⚠ a key is placed by SOURCE time, so the replay's key (the reverse angle over [t−1, t+1.5]) cannot sit at its own source time — it would cut ③ and ④
            // short; it is parked at ④'s end (after ④ in the film). Segments PLAY ONLY when listed, so the main pass is listed too.
            keys.Add(Cut(Key(end4), TheMoment(t + ReplayFrom, t + ReplayTo, Wrap(Across(AcrossMoment) + 180f))));
            segments.Add(new TimeSegment { From = keys[0].At, To = Key(end4) });
            segments.Add(new TimeSegment { From = Key(ReplayFrom), To = Key(ReplayTo), Speed = ReplaySpeed });
        }

        return new Take
        {
            Scenario = options.Scenario,
            Name = options.Name,
            Caption = options.Caption,
            TickHz = options.TickHz,
            EndAt = Key(end4),
            Anchors = anchor is null
                ? new Dictionary<string, AnchorSpec>(StringComparer.Ordinal)
                : new Dictionary<string, AnchorSpec>(StringComparer.Ordinal) { [anchor.Name] = anchor.Spec },
            Baked = anchor is null
                ? null
                : new BakedAnchors { Times = new Dictionary<string, double>(StringComparer.Ordinal) { [anchor.Name] = t }, TickHz = options.TickHz },
            Keys = keys,
            Segments = segments,
        };
    }

    /// <summary>Height by the actor's kind (degrees): a person at eye level, a vehicle low, a group from above, an aircraft from below.</summary>
    private static float PitchOf(SubjectKind kind) => kind switch
    {
        SubjectKind.Person => -8f,
        SubjectKind.Vehicle => -6f,
        SubjectKind.Group => -55f,
        SubjectKind.Aircraft => 10f,
        _ => -20f,
    };

    /// <summary>Seconds after the moment of the NEXT hit / blast on the focus unit within 5 s (null when none).</summary>
    private static double? ConsequenceOffset(TimelineIndex index, DraftMoment moment, long focus, double t)
    {
        double? best = null;
        foreach (var lane in new[] { Hit, Blast })
        {
            var events = index.Lane(lane);
            for (int i = index.Before(lane, moment.Frame + 1); i < events.Count; i++)
            {
                double after = SimOf(index, events[i].Frame) - t;
                if (double.IsNaN(after)) continue;
                if (after > ConsequenceSearch) break;
                bool onFocus = lane == Hit ? events[i].Subject == focus : events[i].Other == focus;
                if (onFocus && after > 1e-9) { best = best is { } b ? Math.Min(b, after) : after; break; }
            }
        }
        return best;
    }

    /// <summary>
    /// A camera that LOOKS AT the centre of both units' paths over [<paramref name="from"/>, <paramref name="to"/>], far enough to hold them all.
    /// </summary>
    private static CameraSpec Midpoint(Scene scene, double from, double to, float yaw, float pitch, DraftOptions o)
    {
        var points = scene.Paths(from, to);
        var lo = new Vector3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        var hi = new Vector3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        var centre = (lo + hi) * 0.5f;
        float distance = FitDistance(points.Select(p => (p, centre)), yaw, pitch, o.FovDeg, o.AspectRatio);
        return new CameraSpec { LookAt = centre, Distance = distance, Yaw = yaw, Pitch = pitch };
    }

    /// <summary>
    /// A camera that FOLLOWS <paramref name="name"/> (the focus), far enough to hold the other unit too at every moment of [<paramref name="from"/>,
    /// <paramref name="to"/>]; <paramref name="fov"/> a telephoto's lens (null = the options').
    /// </summary>
    private static CameraSpec Followed(Scene scene, string name, double from, double to, float yaw, float pitch, float? fov, DraftOptions o)
    {
        var pairs = scene.Times(from, to).SelectMany(s => scene.HasSubject
            ? new[] { (scene.Actor(s), scene.Focus(s)), (scene.Focus(s), scene.Focus(s)) }
            : new[] { (scene.Actor(s), scene.Actor(s)) });
        float distance = FitDistance(pairs, yaw, pitch, fov ?? o.FovDeg, o.AspectRatio);
        return new CameraSpec { Follow = name, Distance = distance, Yaw = yaw, Pitch = pitch, Fov = fov };
    }

    /// <summary>
    /// The distance at which every (point, camera-centre) pair stays inside the horizontal half-angle with the 1.3 margin: the half-width across the view × 1.3,
    /// <c>d = halfWidth / tan(hfov / 2)</c> (hfov from the vertical lens and the aspect), moved out further where a point lies NEARER the camera than the centre
    /// (it is wider in the view); clamped to [8, 600] m.
    /// </summary>
    private static float FitDistance(IEnumerable<(Vector3 Point, Vector3 Centre)> samples, float yawDeg, float pitchDeg, float fovDeg, double aspect)
    {
        double yaw = yawDeg * Rad, pitch = pitchDeg * Rad;
        var forward = new Vector3((float)(Math.Cos(pitch) * Math.Cos(yaw)), (float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch));
        var right = new Vector3((float)Math.Sin(yaw), (float)-Math.Cos(yaw), 0f);
        double tanHalf = Math.Tan(fovDeg * Rad / 2d) * aspect;
        double distance = 0d;
        foreach (var (point, centre) in samples)
        {
            var r = point - centre;
            double half = Math.Max(Math.Abs(Vector3.Dot(r, right)), MinHalfWidth) * Margin;
            distance = Math.Max(distance, half / tanHalf - Vector3.Dot(r, forward));
        }
        return (float)Math.Clamp(distance, MinDistance, MaxDistance);
    }

    private static float Wrap(float deg)
    {
        deg %= 360f;
        return deg > 180f ? deg - 360f : deg <= -180f ? deg + 360f : deg;
    }

    private static double SimOf(TimelineIndex index, int frame) => (uint)frame < (uint)index.FrameSim.Count ? index.FrameSim[frame] : double.NaN;

    /// <summary>Where the two parties of a moment are, at any sim time, from the index's position track.</summary>
    private sealed class Scene
    {
        private readonly TimelineIndex _index;
        private readonly long _actor;
        private readonly long? _subject;
        private readonly Vector3 _fallback;

        public Scene(TimelineIndex index, DraftMoment moment)
        {
            _index = index;
            _actor = moment.Actor;
            _subject = moment.Subject;
            var events = index.Lane(moment.Lane);
            // a unit with no samples is where its event says (the muzzle of a shot, the point of a blast)
            _fallback = (uint)moment.EventIndex < (uint)events.Count ? new Vector3(events[moment.EventIndex].X, events[moment.EventIndex].Y, 0f) : Vector3.Zero;
        }

        public bool HasSubject => _subject.HasValue;

        public Vector3 Actor(double sim) => PositionOf(_actor, sim);

        /// <summary>The unit the camera is about: the subject, else the actor.</summary>
        public Vector3 Focus(double sim) => PositionOf(_subject ?? _actor, sim);

        public float HeadingOf(long unit, double sim)
            => _index.PositionAt(unit, Math.Max(0, EventAnchors.FrameAt(_index, sim))) is { } p ? p.Heading : 0f;

        /// <summary>Every sim time of [from, to] that matters: both ends and each sample of either unit between them (the paths are straight between samples).</summary>
        public List<double> Times(double from, double to)
        {
            var times = new List<double> { from, to };
            AddSamples(_actor, from, to, times);
            if (_subject is { } s) AddSamples(s, from, to, times);
            times.Sort();
            return times;
        }

        /// <summary>Both units' places at every <see cref="Times"/> of the window.</summary>
        public List<Vector3> Paths(double from, double to)
        {
            var points = new List<Vector3>();
            foreach (var s in Times(from, to))
            {
                points.Add(Actor(s));
                if (_subject.HasValue) points.Add(Focus(s));
            }
            return points;
        }

        private void AddSamples(long unit, double from, double to, List<double> times)
        {
            int lo = EventAnchors.FrameAt(_index, from), hi = EventAnchors.FrameAt(_index, to);
            foreach (var sample in _index.PositionsOf(unit, lo + 1, hi + 1))
            {
                double s = SimOf(_index, sample.Frame);
                if (s > from && s < to) times.Add(s);
            }
        }

        private Vector3 PositionOf(long unit, double sim)
        {
            var samples = _index.Positions.Samples(unit);
            if (samples.Count == 0) return _fallback;
            int frame = Math.Max(0, EventAnchors.FrameAt(_index, sim));
            // outside a unit's sampled span (not there yet / gone): its nearest end — a framing never extrapolates
            var p = _index.PositionAt(unit, frame) ?? (frame < samples[0].Frame ? samples[0] : samples[samples.Count - 1]);
            return new Vector3(p.X, p.Y, p.Z);
        }
    }
}
