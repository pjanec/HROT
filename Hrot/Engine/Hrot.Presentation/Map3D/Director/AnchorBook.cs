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
        if (_indexIsTheRun && _live.TryGetValue(anchor, out var r) && _specs.TryGetValue(anchor, out var spec) && EventAnchors.IsEventCue(spec.On)) return r;
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
                int hash = world.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash;
                var name = hash == 0 ? "Idle" : behaviourName(hash) ?? hash.ToString();
                return string.Equals(name, Text(a.Is), StringComparison.OrdinalIgnoreCase);
            case "side":
                return world.HasComponent<EntityInfo>(e)
                    && string.Equals(world.GetComponentRO<EntityInfo>(e).ForceId.ToString(), Text(a.Is), StringComparison.OrdinalIgnoreCase);
            case "health":
                if (!world.IsComponentTypeRegistered<Health>() || !world.HasComponent<Health>(e)) return false;
                var h = world.GetComponentRO<Health>(e);
                return a.Below <= 1f ? h.Max > 0 && h.Current / h.Max < a.Below || h.Current <= 0 && a.Below > 0 : h.Current < a.Below;
            case "embark":
                bool aboard = world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(e);
                return aboard == (a.Is is JsonValue v && v.TryGetValue<bool>(out var b) ? b : !string.Equals(Text(a.Is), "false", StringComparison.OrdinalIgnoreCase));
            case "gesture":
                return string.Equals(Gestures.Of(world, e).ToString(), Text(a.Is), StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
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
