using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>An anchor whose live time differs from its baked one — ⭐ on a stepped run that repeats, a DETERMINISM finding (§0, R-3038).</summary>
public sealed record AnchorMismatch(string Anchor, double Baked, double? Live)
{
    public override string ToString() => Live is { } t ? $"{Anchor}: baked {Baked:0.###} s, live {t:0.###} s" : $"{Anchor}: baked {Baked:0.###} s, not seen live";
}

/// <summary>
/// ⭐ <c>CE-4117</c> (docs/DESIGN_Director_Timeline_Editor.md §11s-1) — where a <c>near</c> cue is first met on a recording, from its position track: the take-time window
/// (<paramref name="From"/>, <paramref name="To"/>] it must lie in and the straight-line <paramref name="Estimate"/> inside it. All TAKE seconds (sim − t0).
/// </summary>
public sealed record NearWindow(string Anchor, double From, double To, double Estimate);

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, §0; B7) — the take's anchors in this run. ⭐⭐ <c>CE-1304</c> P4 — CR-5
/// (docs/DESIGN_Director_Timeline_Editor.md §11s): ONE resolver for a live run and a recording. EVENT cues (<c>shot</c> · <c>blast</c> · <c>hit</c>) are
/// queries on the run's timeline INDEX (<see cref="EventAnchors.TimeOf"/> — the naming switch K and the Drafter use, in reverse): the live index on a live
/// run, the recording's on a recording. STATE cues observe the world (live, or the replayed one) — ⛔ never <c>repo.Bus.Read</c> (CE-3293 S3).
/// ⛔ SUPERSEDED: the event cues read <c>ShotLog</c> / <c>DetonationLog</c>, side stores beside the world that a recording never carries, so a take played on a
/// recording reported <c>beat-missing</c> (§11r). The scout bakes what it finds; during a film it VERIFIES each baked time and names any that drift.
/// <para>Cues: <c>shot</c> (the n-th round, by a unit) · <c>blast</c> (the n-th burst) · <c>hit</c> (a unit's first strike) · <c>near</c> (a unit
/// within a radius of a point) · <c>task</c> (a unit's running behaviour) · <c>side</c> (its affiliation) · <c>health</c> (below a share) ·
/// <c>embark</c> (aboard or not) · <c>gesture</c>. A unit is a name or <c>"*part"</c> (any whose name contains it).</para>
/// </summary>
public sealed class AnchorBook
{
    /// <summary>How far a live time may differ from its baked one and still match (one 60 Hz step, rounded up).</summary>
    public const double Tolerance = 0.02;

    private readonly IReadOnlyDictionary<string, AnchorSpec> _specs;
    private readonly IReadOnlyDictionary<string, double> _baked;
    private readonly Func<int, string?> _behaviourName;
    private readonly Func<TimelineIndex?> _index;
    private readonly double _t0;
    private readonly bool _indexIsTheRun;
    private readonly Dictionary<string, double> _live = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolvedAhead = new(StringComparer.Ordinal);

    /// <param name="index">⭐ CR-5 — the run's timeline index (null, or none: the event cues stay unseen).</param>
    /// <param name="t0">The sim second the take's clock starts at: an index frame at sim <c>s</c> is take time <c>s − t0</c>; earlier frames are not this run's.</param>
    /// <param name="indexIsTheRun">⭐ CR-5 — the index is a RECORDING's (the whole run, future included): its time of an event wins over a bake, which may be
    /// another run's (a take adjusted on recording A plays on B at B's times). A live index knows only the past, so there the bake (exact on a repeating run,
    /// R-3038) leads.</param>
    public AnchorBook(IReadOnlyDictionary<string, AnchorSpec> anchors, IReadOnlyDictionary<string, double>? baked, Func<int, string?>? behaviourName = null,
                      Func<TimelineIndex?>? index = null, double t0 = 0d, bool indexIsTheRun = false)
    {
        _specs = anchors ?? throw new ArgumentNullException(nameof(anchors));
        _baked = baked ?? new Dictionary<string, double>();
        _behaviourName = behaviourName ?? (_ => null);
        _index = index ?? (() => null);
        _t0 = t0;
        _indexIsTheRun = indexIsTheRun;
    }

    /// <summary>Anchor times seen in THIS run.</summary>
    public IReadOnlyDictionary<string, double> Live => _live;

    /// <summary>
    /// The time a shot is placed by: the BAKED one when there is one (exact on a repeating run — R-3038), else the live one (a take not
    /// scouted yet: a cut BEFORE its cue then lands late). ⭐ CR-5 — on a recording, an event cue's time in ITS index comes first.
    /// </summary>
    public double? TimeOf(string anchor)
    {
        if (_indexIsTheRun && _live.TryGetValue(anchor, out var r) && _specs.TryGetValue(anchor, out var spec)
            && (EventAnchors.IsEventCue(spec.On) || _resolvedAhead.Contains(anchor))) return r;   // ⭐ CE-4117: a cue answered ahead of play leads like an event cue
        return _baked.TryGetValue(anchor, out var t) ? t : _live.TryGetValue(anchor, out t) ? t : null;
    }

    public double? TimeOf(AtExpr at) => at.Resolve(TimeOf);

    /// <summary>The anchors still unseen.</summary>
    public IEnumerable<string> Unseen => _specs.Keys.Where(k => !_live.ContainsKey(k));

    /// <summary>
    /// ⭐ CR-5 — an unseen EVENT anchor may still appear in a LIVE index that has not caught up to take time <paramref name="simTime"/> (it publishes a
    /// snapshot at most every quarter second of wall time): the run should not be judged yet. Never on a recording (its index is not behind the run).
    /// </summary>
    public bool AwaitsIndex(double simTime)
    {
        if (_indexIsTheRun || _index() is not { } index) return false;
        if (!_specs.Any(kv => EventAnchors.IsEventCue(kv.Value.On) && !_live.ContainsKey(kv.Key))) return false;
        var fs = index.FrameSim;
        int n = Math.Min(fs.Count, index.FrameTicks.Count);
        return n == 0 || double.IsNaN(fs[n - 1]) || fs[n - 1] - _t0 < simTime - 1e-6;
    }

    /// <summary>
    /// ⭐ <c>CE-1376</c> — the rounds fired from the take's start to take time <paramref name="rel"/>, counted on the run's INDEX (<c>combat.shot</c>, the lane the
    /// <c>shot</c> anchors read) — so a recording and a live run report the same figure. Null when the index does not cover <paramref name="rel"/> yet (none, or a
    /// LIVE one that has not caught up: it publishes a snapshot at most every quarter second of wall time) — the caller then has no index figure to give.
    /// </summary>
    public long? RoundsUntil(double rel)
    {
        if (_index() is not { } index) return null;
        var fs = index.FrameSim;
        int n = Math.Min(fs.Count, index.FrameTicks.Count);
        if (n == 0) return null;
        if (!_indexIsTheRun && (double.IsNaN(fs[n - 1]) || fs[n - 1] - _t0 < rel - 1e-6)) return null;
        return EventAnchors.CountBetween(index, "combat.shot", _t0, _t0 + rel);
    }

    /// <summary>Each baked anchor seen at another time, or (once <paramref name="simTime"/> is past it) not seen.</summary>
    public IReadOnlyList<AnchorMismatch> Mismatches(double simTime)
    {
        var list = new List<AnchorMismatch>();
        foreach (var (key, baked) in _baked)
        {
            if (!_specs.ContainsKey(key)) continue;
            if (_live.TryGetValue(key, out var live))
            {
                if (Math.Abs(live - baked) > Tolerance) list.Add(new AnchorMismatch(key, baked, live));
            }
            else if (simTime > baked + 1d) list.Add(new AnchorMismatch(key, baked, null));
        }
        return list;
    }

    /// <summary>
    /// ⭐ CR-5 — each unseen EVENT anchor the index now holds, at its event's take time. On a recording that is every one at once (its index is the whole
    /// run, read as it is scanned); live, an event once the live index has it. Cheap to call every frame.
    /// </summary>
    public void ObserveEvents(IShotWorld units)
    {
        if (_index() is not { } index) return;
        foreach (var (key, a) in _specs)
        {
            if (_live.ContainsKey(key) || !EventAnchors.IsEventCue(a.On)) continue;
            if (EventAnchors.TimeOf(index, a, (who, id) => IsUnit(who, id, units), _t0) is { } sim) _live[key] = sim - _t0;
        }
        if (_indexIsTheRun) ObserveStateCues(index, units);   // ⭐ CE-4117 N6 — a recording's state lanes answer the five state cues ahead of play
    }

    /// <summary>
    /// ⭐ <c>CE-4117</c> (§11s-1 N1) — for each unseen <c>near</c> anchor, the take-time window in which its unit first comes within the radius of the point on the
    /// RECORDING's position track (sampled every 0.5 s of sim, linear in between): the first sample at or after t0 inside gives (previous sample, that sample], and
    /// <see cref="NearWindow.Estimate"/> is where the straight line between the two crosses the circle. A unit already inside at its first sample at or after t0:
    /// the window is [max(t0, s − 0.5), s] and the estimate that sample. Several units (<c>"*part"</c>): the earliest window. No index, no sample inside: no window.
    /// </summary>
    public IReadOnlyList<NearWindow> NearWindows(IShotWorld units)
    {
        var list = new List<NearWindow>();
        if (_index() is not { } index) return list;
        var fs = index.FrameSim;
        foreach (var (key, a) in _specs)
        {
            if (a.On != "near" || a.Who == null || a.Point is not { } point || _live.ContainsKey(key)) continue;
            NearWindow? best = null;
            foreach (var id in UnitIdsNamed(a.Who, units))
            {
                if (NearWindowOf(key, a, point, id, index, fs) is { } w && (best is null || w.Estimate < best.Estimate)) best = w;
            }
            if (best != null) list.Add(best);
        }
        return list;
    }

    private NearWindow? NearWindowOf(string key, AnchorSpec a, Vector2 point, long id, TimelineIndex index, SharedList<double> fs)
    {
        var samples = index.Positions.Samples(id).Span;
        double r2 = (double)a.Within * a.Within;
        double? prevSim = null; double px = 0, py = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            if (s.Frame < 0 || s.Frame >= fs.Count || double.IsNaN(fs[s.Frame])) continue;
            double sim = fs[s.Frame];
            if (sim < _t0 - 1e-9) continue;   // before this run's start: not the take's
            double dx = s.X - point.X, dy = s.Y - point.Y;
            bool inside = dx * dx + dy * dy <= r2;
            if (inside)
            {
                if (prevSim is not { } t0s) return new NearWindow(key, Math.Max(0d, sim - _t0 - 0.5), sim - _t0, sim - _t0);   // inside at its first usable sample
                // the first crossing of the circle on the segment prev → s: solve |p + u (q − p) − c|² = r², the smaller root in [0, 1]
                double ex = s.X - px, ey = s.Y - py, fx = px - point.X, fy = py - point.Y;
                double qa = ex * ex + ey * ey, qb = 2 * (fx * ex + fy * ey), qc = fx * fx + fy * fy - r2;
                double u = 1d;
                if (qa > 1e-12)
                {
                    double disc = qb * qb - 4 * qa * qc;
                    if (disc >= 0) { u = (-qb - Math.Sqrt(disc)) / (2 * qa); u = Math.Clamp(u, 0d, 1d); }
                }
                return new NearWindow(key, t0s - _t0, sim - _t0, t0s + u * (sim - t0s) - _t0);
            }
            prevSim = sim; px = s.X; py = s.Y;
        }
        return null;
    }

    /// <summary>⭐ CE-4117 — the network ids of the units <paramref name="who"/> names (an exact name, or <c>"*part"</c>: every name containing it), as <see cref="IsUnit"/> reads them.</summary>
    private static IEnumerable<long> UnitIdsNamed(string who, IShotWorld units)
    {
        if (!who.StartsWith('*')) { if (units.Resolve(who) is { } id && id != 0) yield return id; yield break; }
        var part = who[1..];
        foreach (var n in units.Names.Where(n => n.Contains(part, StringComparison.Ordinal)).ToList())
            if (units.Resolve(n) is { } id && id != 0) yield return id;
    }

    /// <summary>
    /// ⭐ <c>CE-4125</c> — WHY <paramref name="anchor"/> has no time on this run, so an agent drafting on a recording can tell <i>"never"</i> from <i>"not yet"</i>
    /// from <i>"wrong name"</i>: no such unit (with the names that look alike) · no such event (how many the recording has) · the condition never met (a <c>near</c>'s
    /// closest approach) · awaits play (a state cue the index cannot answer ahead). Null when the anchor HAS a time.
    /// </summary>
    public string? WhyUnseen(string anchor, IShotWorld units)
    {
        if (TimeOf(anchor) is not null) return null;
        if (!_specs.TryGetValue(anchor, out var a)) return "the take defines no such anchor";
        foreach (var who in a.Names())
            if (!UnitIdsNamed(who, units).Any())
            {
                var key = (who.StartsWith('*') ? who[1..] : who).Split(' ')[0];
                var alike = units.Names.Where(n => n.Contains(key, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
                return $"no unit named '{who}' in the shown world" + (alike.Count > 0 ? $" (alike: {string.Join(", ", alike)})" : "");
            }
        if (_index() is not { } index) return "no timeline index is shown (open the recording)";
        bool Is(string who, long id) => IsUnit(who, id, units);
        double? SimOf(int f) => f >= 0 && f < index.FrameSim.Count && !double.IsNaN(index.FrameSim[f]) && index.FrameSim[f] >= _t0 - 1e-9 ? index.FrameSim[f] : null;
        switch (a.On)
        {
            case "shot":
            case "blast":
            {
                string lane = a.On == "shot" ? "combat.shot" : "combat.blast";
                int k = index.Has(lane) ? index.Lane(lane).Count(e => SimOf(e.Frame) is not null && (a.On != "shot" || a.By == null || Is(a.By, e.Subject))) : 0;
                string what = a.On == "shot" ? (a.By != null ? $"shots by '{a.By}'" : "shots") : "blasts";
                return k == 0 ? $"no such event in the recording: 0 {what}" : $"the recording has {k} {what}; #{Math.Max(1, a.N)} asked";
            }
            case "hit":
                return $"no such event in the recording: '{a.Target}' is never hit";
            case "near":
            {
                if (a.Point is not { } p) return "a near cue with no point";
                double best = double.PositiveInfinity;
                foreach (var id in UnitIdsNamed(a.Who!, units))
                    foreach (var s in index.Positions.Samples(id).Span)
                        if (SimOf(s.Frame) is not null) best = Math.Min(best, Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y)));
                return double.IsInfinity(best) ? $"the recording has no positions for '{a.Who}'"
                                               : $"never met in the recording: '{a.Who}' comes no closer than {best:0.#} m to ({p.X:0.#}, {p.Y:0.#}) — within {a.Within:0.#} asked";
            }
            default:
                if (!IndexAnswers(anchor)) return "awaits play (a state cue — observed as the playhead passes)";
                return $"never met in the recording: '{a.Who}' never has {a.On} {(a.On == "health" ? $"below {a.Below:0.##}" : Text(a.Is))}";
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4117</c> follow-up — a re-read take (a loop wrap, <c>DirectorRunner.RefreshOpenTake</c>) gets a NEW book; the times the previous one resolved AHEAD of play (a <c>near</c> refined by seeking) are carried over for
    /// every anchor whose spec is UNCHANGED, so the loop does not fall back to observing during play. A changed spec is not carried.
    /// </summary>
    public void CarryOverResolved(AnchorBook previous)
    {
        foreach (var key in previous._resolvedAhead)
        {
            if (_live.ContainsKey(key) || !_specs.TryGetValue(key, out var now) || !previous._specs.TryGetValue(key, out var was) || !previous._live.TryGetValue(key, out var rel)) continue;
            if (SameSpec(now, was)) Resolve(key, rel);
        }
    }

    private static bool SameSpec(AnchorSpec a, AnchorSpec b)
        => a.On == b.On && a.Who == b.Who && a.By == b.By && a.Target == b.Target && a.N == b.N && a.Point == b.Point && a.Within == b.Within && a.Below == b.Below
           && a.Is?.ToJsonString() == b.Is?.ToJsonString();

    /// <summary>⭐ CE-4117 — records that <paramref name="anchor"/> was answered AHEAD of play at take time <paramref name="rel"/> (the refined crossing, or the coarse estimate):
    /// on a recording it then leads over a bake (<see cref="TimeOf(string)"/>).</summary>
    public void Resolve(string anchor, double rel)
    {
        _live[anchor] = rel;
        _resolvedAhead.Add(anchor);
    }

    /// <summary>Looks at the run after a step: the event cues in its index (<see cref="ObserveEvents"/>), then each unseen STATE anchor whose cue holds now,
    /// recorded at <paramref name="simTime"/>.</summary>
    public void Observe(EntityRepository world, IShotWorld units, double simTime)
    {
        ObserveEvents(units);
        foreach (var (key, a) in _specs)
        {
            if (_live.ContainsKey(key) || EventAnchors.IsEventCue(a.On)) continue;
            if (AnyUnit(a, world, units, e => Holds(a, world, e, _behaviourName))) _live[key] = simTime;
        }
    }

    /// <summary>Whether <paramref name="who"/> (a name, or <c>"*part"</c>) names the unit with network id <paramref name="networkId"/> (an index's subject id).</summary>
    private static bool IsUnit(string who, long networkId, IShotWorld units)
    {
        if (networkId == 0) return false;
        if (!who.StartsWith('*')) return units.Resolve(who) == networkId;
        var part = who[1..];
        foreach (var n in units.Names)
            if (n.Contains(part, StringComparison.Ordinal) && units.Resolve(n) == networkId) return true;
        return false;
    }

    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §2) — whether a STATE cue (<c>near task side health embark gesture</c>) holds NOW
    /// for any unit it names: the take's own reading, shared with the scenario verifier so a check and a take judge a cue alike.
    /// </summary>
    public static bool HoldsFor(AnchorSpec a, EntityRepository world, IShotWorld units, Func<int, string?> behaviourName)
        => a.On is "near" or "task" or "side" or "health" or "embark" or "gesture"
           && AnyUnit(a, world, units, e => Holds(a, world, e, behaviourName));

    private static bool Holds(AnchorSpec a, EntityRepository world, Entity e, Func<int, string?> behaviourName)
    {
        switch (a.On)
        {
            case "near":
                if (a.Point is not { } p || !world.HasComponent<SimTransform>(e)) return false;
                var pos = world.GetComponentRO<SimTransform>(e).Position;
                return Vector2.Distance(new Vector2(pos.X, pos.Y), p) <= a.Within;
            case "task":
                if (!world.IsComponentTypeRegistered<BehaviorState>() || !world.HasComponent<BehaviorState>(e)) return false;
                return TaskHolds(a, world.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash, behaviourName);
            case "side":
                return world.HasComponent<EntityInfo>(e) && SideHolds(a, world.GetComponentRO<EntityInfo>(e).ForceId);
            case "health":
                if (!world.IsComponentTypeRegistered<Health>() || !world.HasComponent<Health>(e)) return false;
                var h = world.GetComponentRO<Health>(e);
                return HealthHolds(a, h.Current, h.Max);
            case "embark":
                return EmbarkHolds(a, world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(e));
            case "gesture":
                return GestureHolds(a, Gestures.Of(world, e));
            default:
                return false;
        }
    }


    // ── ⭐ CE-4117 N6 — ONE comparison per state cue, on VALUES: the world path reads the component and calls it, the recording's lane path calls it with the lane entry's value ──

    /// <summary>The unit's running behaviour (hash; 0 = Idle) is the one the cue names (by name through <paramref name="behaviourName"/>).</summary>
    public static bool TaskHolds(AnchorSpec a, int hash, Func<int, string?> behaviourName)
    {
        var name = hash == 0 ? "Idle" : behaviourName(hash) ?? hash.ToString();
        return string.Equals(name, Text(a.Is), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The unit's affiliation is the one the cue names.</summary>
    public static bool SideHolds(AnchorSpec a, ForceId force) => string.Equals(force.ToString(), Text(a.Is), StringComparison.OrdinalIgnoreCase);

    /// <summary>Health below the cue's share (<c>Below</c> ≤ 1: a fraction of <paramref name="max"/>; above 1: an absolute figure); a dead unit counts for any positive threshold.</summary>
    public static bool HealthHolds(AnchorSpec a, float current, float max)
        => a.Below <= 1f ? max > 0 && current / max < a.Below || current <= 0 && a.Below > 0 : current < a.Below;

    /// <summary>Aboard (or not) as the cue says (<c>is</c>: a bool, default aboard).</summary>
    public static bool EmbarkHolds(AnchorSpec a, bool aboard)
        => aboard == (a.Is is JsonValue v && v.TryGetValue<bool>(out var b) ? b : !string.Equals(Text(a.Is), "false", StringComparison.OrdinalIgnoreCase));

    /// <summary>The unit's gesture is the one the cue names.</summary>
    public static bool GestureHolds(AnchorSpec a, GestureKind kind) => string.Equals(kind.ToString(), Text(a.Is), StringComparison.OrdinalIgnoreCase);

    /// <summary>The lane that records a state cue's unit state (<c>state.task</c> …); null for a cue that has none.</summary>
    public static string? StateLaneOf(string on) => on switch
    {
        "task" => "state.task", "side" => "state.side", "health" => "state.health", "embark" => "state.embark", "gesture" => "state.gesture", _ => null,
    };

    /// <summary>
    /// ⭐ CE-4117 N6 — whether the cue holds for a unit whose state-lane entry is <paramref name="e"/> (<see langword="null"/>: no entry yet — only a gesture has a default, None). The lane carries
    /// task as <c>Other</c> (the exact hash), side / gesture / embark as <c>Value</c>, health as <c>Value</c> = Current and <c>X</c> = Max.
    /// </summary>
    public static bool HoldsOnLaneEntry(AnchorSpec a, TimelineEvent? e, Func<int, string?> behaviourName) => a.On switch
    {
        "task" => e is { } t && TaskHolds(a, (int)t.Other, behaviourName),
        "side" => e is { } si && SideHolds(a, (ForceId)(byte)si.Value),
        "health" => e is { } he && HealthHolds(a, he.Value, he.X),
        "embark" => e is { } em && EmbarkHolds(a, em.Value >= 0.5f),
        "gesture" => GestureHolds(a, e is { } g ? (GestureKind)(byte)g.Value : GestureKind.None),
        _ => false,
    };

    /// <summary>⭐ CE-4117 N6 — whether the recording's index answers this state cue (the run is a recording AND the index has the cue's lane): then it is known ahead of play, else it is observed as the playhead passes.</summary>
    public bool IndexAnswers(string anchor)
        => _indexIsTheRun && _specs.TryGetValue(anchor, out var a) && StateLaneOf(a.On) is { } lane && _index() is { } index && index.Has(lane);

    private void ObserveStateCues(TimelineIndex index, IShotWorld units)
    {
        var fs = index.FrameSim;
        foreach (var (key, a) in _specs)
        {
            if (_live.ContainsKey(key) || a.Who == null || StateLaneOf(a.On) is not { } lane || !index.Has(lane)) continue;
            double? best = null;
            foreach (var id in UnitIdsNamed(a.Who, units))
                if (StateTimeOf(a, lane, id, index, fs) is { } rel && (best is null || rel < best)) best = rel;
            if (best is { } t) { _live[key] = t; _resolvedAhead.Add(key); }
        }
    }

    /// <summary>The first take second the cue holds for the unit, walking its lane entries in frame order (the unit's state carries over between entries); a state already holding at t0 holds at 0.</summary>
    private double? StateTimeOf(AnchorSpec a, string lane, long id, TimelineIndex index, SharedList<double> fs)
    {
        double? SimOf(int frame) => frame >= 0 && frame < fs.Count && !double.IsNaN(fs[frame]) ? fs[frame] : null;
        TimelineEvent? cur = null;
        bool crossed = false;   // the state at t0 has been judged
        double? firstEntrySim = null;
        foreach (var e in index.Lane(lane))
        {
            if (e.Subject != id || SimOf(e.Frame) is not { } sim) continue;
            firstEntrySim ??= sim;
            if (sim < _t0 - 1e-9) { cur = e; continue; }
            if (!crossed)
            {
                crossed = true;
                if (cur is not null && HoldsOnLaneEntry(a, cur, _behaviourName)) return 0d;
            }
            cur = e;
            if (HoldsOnLaneEntry(a, cur, _behaviourName)) return sim - _t0;
        }
        if (!crossed && cur is not null && HoldsOnLaneEntry(a, cur, _behaviourName)) return 0d;
        // a gesture with no entry yet is None, from the unit's first appearance (its first life.spawned, or its first entry in any state lane) until its first gesture entry
        if (a.On == "gesture" && HoldsOnLaneEntry(a, null, _behaviourName) && FirstSeenSim(index, id, fs) is { } seen)
        {
            double at = Math.Max(seen, _t0);
            if (firstEntrySim is not { } fe || at < fe - 1e-9) return at - _t0;
        }
        return null;
    }

    private static double? FirstSeenSim(TimelineIndex index, long id, SharedList<double> fs)
    {
        double? best = null;
        foreach (var lane in new[] { "life.spawned", "state.task", "state.side", "state.health", "state.embark", "state.gesture" })
        {
            if (!index.Has(lane)) continue;
            foreach (var e in index.Lane(lane))
                if (e.Subject == id && e.Frame >= 0 && e.Frame < fs.Count && !double.IsNaN(fs[e.Frame])) { if (best is null || fs[e.Frame] < best) best = fs[e.Frame]; break; }
        }
        return best;
    }

    private static string Text(JsonNode? n) => n?.ToString() ?? "";

    /// <summary>Whether the cue holds for any unit the anchor names.</summary>
    private static bool AnyUnit(AnchorSpec a, EntityRepository world, IShotWorld units, Func<Entity, bool> holds)
    {
        if (a.Who == null) return false;
        foreach (var e in UnitsNamed(a.Who, world, units))
            if (holds(e)) return true;
        return false;
    }

    private static IEnumerable<Entity> UnitsNamed(string who, EntityRepository world, IShotWorld units)
    {
        if (who.StartsWith('*'))
        {
            var part = who[1..];
            foreach (var n in units.Names.Where(n => n.Contains(part, StringComparison.Ordinal)).ToList())
                if (units.Resolve(n) is { } id && NetworkIdResolver.ResolveNetworkId(world, id) is var e && !e.IsNull) yield return e;
        }
        else if (units.Resolve(who) is { } id && NetworkIdResolver.ResolveNetworkId(world, id) is var e && !e.IsNull)
            yield return e;
    }
}
