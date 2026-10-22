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

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>An anchor whose live time differs from its baked one — ⭐ on a stepped run that repeats, a DETERMINISM finding (§0, R-3038).</summary>
public sealed record AnchorMismatch(string Anchor, double Baked, double? Live)
{
    public override string ToString() => Live is { } t ? $"{Anchor}: baked {Baked:0.###} s, live {t:0.###} s" : $"{Anchor}: baked {Baked:0.###} s, not seen live";
}

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, §0; B7) — the take's anchors in this run. It reads the run's beats each step from
/// COMPONENT STATE and the combat logs kept beside the world (<see cref="ShotLog"/>, <see cref="DetonationLog"/>) — ⛔ never
/// <c>repo.Bus.Read</c> (CE-3293 S3). The scout bakes what it records; during a film it VERIFIES each baked time and names any that drift.
/// <para>Cues: <c>shot</c> (the n-th round, by a unit) · <c>blast</c> (the n-th burst) · <c>hit</c> (a unit struck) · <c>near</c> (a unit
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
    private readonly Dictionary<string, double> _live = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private long _shotSeq, _blastSeq, _firstShot;
    private bool _primed;

    public AnchorBook(IReadOnlyDictionary<string, AnchorSpec> anchors, IReadOnlyDictionary<string, double>? baked, Func<int, string?>? behaviourName = null)
    {
        _specs = anchors ?? throw new ArgumentNullException(nameof(anchors));
        _baked = baked ?? new Dictionary<string, double>();
        _behaviourName = behaviourName ?? (_ => null);
    }

    /// <summary>Anchor times seen in THIS run.</summary>
    public IReadOnlyDictionary<string, double> Live => _live;

    /// <summary>
    /// The time a shot is placed by: the BAKED one when there is one (exact on a repeating run — R-3038), else the live one (a take not
    /// scouted yet: a cut BEFORE its cue then lands late).
    /// </summary>
    public double? TimeOf(string anchor) => _baked.TryGetValue(anchor, out var t) ? t : _live.TryGetValue(anchor, out t) ? t : null;

    public double? TimeOf(AtExpr at) => at.Resolve(TimeOf);

    /// <summary>The anchors still unseen.</summary>
    public IEnumerable<string> Unseen => _specs.Keys.Where(k => !_live.ContainsKey(k));

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

    /// <summary>Looks at the world after a step: each unseen anchor whose cue holds now is recorded at <paramref name="simTime"/> (a shot at its own firing time).</summary>
    public void Observe(EntityRepository world, IShotWorld units, double simTime)
    {
        var shots = ShotLog.Peek(world);
        var blasts = DetonationLog.Peek(world);
        if (!_primed)
        {
            // ⚠ the logs live beside the world and outlast a load: only what happens from now on counts
            _shotSeq = _firstShot = shots?.Count ?? 0;
            _blastSeq = blasts?.Count ?? 0;
            _primed = true;
        }

        var newShots = new List<ShotRecord>();
        if (shots != null)
        {
            for (long s = Math.Max(_shotSeq, shots.Count - ShotLog.Capacity); s < shots.Count; s++)
                if (shots.At(s) is { } r) newShots.Add(r);
            _shotSeq = shots.Count;
        }
        var newBlasts = new List<DetonationRecord>();
        if (blasts != null)
        {
            for (long s = Math.Max(_blastSeq, blasts.Count - DetonationLog.Capacity); s < blasts.Count; s++)
                if (blasts.At(s) is { } r) newBlasts.Add(r);
            _blastSeq = blasts.Count;
        }

        foreach (var (key, a) in _specs)
        {
            if (_live.ContainsKey(key)) continue;
            double? at = a.On switch
            {
                "shot" => CountShots(key, a, newShots, world, units),
                "blast" => Count(key, a.N, newBlasts.Count) ? simTime : null,
                "hit" => Hit(a, newBlasts, world, units) ? simTime : null,
                _ => AnyUnit(a, world, units, e => Holds(a, world, e, _behaviourName)) ? simTime : null,
            };
            if (at is { } t) _live[key] = t;
        }
    }

    private double? CountShots(string key, AnchorSpec a, List<ShotRecord> shots, EntityRepository world, IShotWorld units)
    {
        foreach (var s in shots)
        {
            if (a.By != null && !Matches(a.By, s.Shooter, world, units)) continue;
            if (Count(key, a.N, 1)) return s.FiredAt;
        }
        return null;
    }

    /// <summary>Adds <paramref name="seen"/> to the anchor's count; true when it reaches <paramref name="n"/>.</summary>
    private bool Count(string key, int n, int seen)
    {
        if (seen <= 0) return false;
        int c = (_counts.TryGetValue(key, out var k) ? k : 0) + seen;
        _counts[key] = c;
        return c >= Math.Max(1, n);
    }

    private bool Hit(AnchorSpec a, List<DetonationRecord> blasts, EntityRepository world, IShotWorld units)
    {
        if (a.Target == null) return false;
        // ⚠ a round is logged when FIRED and ends later in place: look at every round of this run, not only the new ones
        if (ShotLog.Peek(world) is { } log)
            foreach (var s in log.Recent(ShotLog.Capacity))
            {
                if (s.Seq < _firstShot) break;
                if (s.Outcome == ShotOutcome.Hit && Matches(a.Target, s.HitEntity, world, units)) return true;
            }
        foreach (var b in blasts)
            if (Matches(a.Target, b.Struck, world, units) || b.Effects.Any(x => x.TotalDamage > 0 && Matches(a.Target, x.Entity, world, units))) return true;
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

    private static bool Matches(string who, Entity e, EntityRepository world, IShotWorld units)
    {
        if (e.IsNull) return false;
        long id = NetworkIdResolver.RuntimeNetworkIdOf(world, e);
        if (id == 0) return false;
        if (!who.StartsWith('*')) return units.Resolve(who) == id;
        return world.HasComponent<EntityInfo>(e) && world.GetComponentRO<EntityInfo>(e).Name.ToString().Contains(who[1..], StringComparison.Ordinal);
    }
}
