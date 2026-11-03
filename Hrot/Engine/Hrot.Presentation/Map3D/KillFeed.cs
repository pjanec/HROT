using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ H2 / PH-5 (CE-4228; docs/DESIGN_Player_HUD_And_Debrief.md) — the KILL FEED: ONE pure model (no raylib), the last <see cref="Max"/> downings. Written by
/// <c>EffectSpawnSystem</c> from every <see cref="ManDownedEvent"/> on the bus (behaviors' ONE kill authority, CE-3364 — once per downing, on every node over
/// <c>dtManDowned</c>; ⛔ never from a damage event) while <c>hud.kill-feed</c> is on, read by <c>PossessionStation</c>. Names and sides are resolved when the
/// event arrives, so an entry outlives the entities. Presentation memory only: the events are recorded, so a replay refills it.
/// </summary>
public sealed class KillFeed
{
    /// <summary>How many entries the feed shows.</summary>
    public const int Max = 5;
    /// <summary>How long an entry stays (sim seconds).</summary>
    public const double ShowSeconds = 15.0;
    private const int RawCap = 64;

    /// <summary>One downing as the HUD needs it. <see cref="Rethrower"/> is set when the killing round was a rethrown one (the thrower named in <see cref="Killer"/> is the FIRST one).</summary>
    public readonly record struct Entry(double At, int KillerNet, string Killer, ForceId KillerSide, int VictimNet, string Victim, ForceId VictimSide,
                                        bool Dead, bool Head, string? Rethrower, float Distance);

    /// <summary>A piece of a feed line with the side it is coloured by (<see cref="ForceId.Neutral"/> = the plain text colour).</summary>
    public readonly record struct Segment(string Text, ForceId Side);

    private readonly List<Entry> _all = new();

    public IReadOnlyList<Entry> All => _all;

    /// <summary>Keeps one downing. The head mark is <c>HasBodyPart != 0</c> and the person plan's head part.</summary>
    public void Record(in Entry entry)
    {
        _all.Add(entry);
        if (_all.Count > RawCap) _all.RemoveRange(0, _all.Count - RawCap);
    }

    /// <summary>
    /// Resolves <paramref name="e"/>'s ids in <paramref name="world"/> NOW and keeps it: the killer is the FIRST thrower (<c>OriginalSource</c>) when it differs from
    /// <c>Source</c>, who is then the rethrower; a victim that is not named by the world is "#netId"; a hit from nobody is "?".
    /// </summary>
    public void Record(EntityRepository world, in ManDownedEvent e, double now)
    {
        bool rethrown = !e.OriginalSource.IsNull && e.OriginalSource != e.Source;
        var killer = rethrown ? e.OriginalSource : e.Source;
        var (kName, kSide, kNet) = Describe(world, killer, "?");
        var (vName, _, vNet) = Describe(world, e.Victim, "?");
        string? rethrower = rethrown ? Describe(world, e.Source, "?").Name : null;
        float dist = 0f;
        if (!killer.IsNull && !e.Victim.IsNull && world.IsAlive(killer) && world.IsAlive(e.Victim) && world.HasComponent<SimTransform>(killer) && world.HasComponent<SimTransform>(e.Victim))
            dist = System.Numerics.Vector3.Distance(world.GetComponentRO<SimTransform>(killer).Position, world.GetComponentRO<SimTransform>(e.Victim).Position);
        Record(new Entry(now, kNet, kName, kSide, vNet, vName, e.VictimSide, e.State == InjuryState.Dead, e.HasBodyPart != 0 && e.BodyPart == Fdp.Toolkit.Physics.BodyParts.PersonPart.Head, rethrower, dist));
    }

    private static (string Name, ForceId Side, int Net) Describe(EntityRepository w, Entity e, string unknown)
    {
        if (e.IsNull || !w.IsAlive(e)) return (unknown, ForceId.Neutral, 0);
        int net = NetworkIdResolver.RuntimeNetworkIdOf(w, e);
        string name = w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(e) && w.GetComponentRO<EntityInfo>(e).Name.ToString() is { Length: > 0 } n ? n : net != 0 ? $"#{net}" : unknown;
        var side = w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(e) ? w.GetComponentRO<EntityInfo>(e).ForceId : ForceId.Neutral;
        return (name, side, net);
    }

    /// <summary>The entries to show at <paramref name="now"/>: not from the future (a replay seeked back), not older than <see cref="ShowSeconds"/>; the last <see cref="Max"/>, newest first.</summary>
    public IReadOnlyList<Entry> Recent(double now)
        => _all.Where(e => e.At <= now && now - e.At <= ShowSeconds).OrderByDescending(e => e.At).Take(Max).ToList();

    /// <summary>
    /// The line of <paramref name="e"/> as coloured segments: <c>killer » victim</c>, then <c>- down</c> / <c>- killed</c>, <c>(head)</c>, <c>(rethrown by X)</c>, <c>· 212 m</c>.
    /// The viewer's own name reads <c>You</c>. The arrow is "»" (the HUD font has no arrow glyph).
    /// </summary>
    public static IReadOnlyList<Segment> Line(in Entry e, int viewerUnitNet)
    {
        var parts = new List<Segment>
        {
            new(viewerUnitNet != 0 && e.KillerNet == viewerUnitNet ? "You" : e.Killer, e.KillerSide),
            new(" » ", ForceId.Neutral),
            new(viewerUnitNet != 0 && e.VictimNet == viewerUnitNet ? "You" : e.Victim, e.VictimSide),
            new(e.Dead ? " - killed" : " - down", ForceId.Neutral),
        };
        if (e.Head) parts.Add(new(" (head)", ForceId.Neutral));
        if (e.Rethrower is { } r) parts.Add(new($" (rethrown by {r})", ForceId.Neutral));
        if (e.Distance >= 1f) parts.Add(new($" · {e.Distance:0} m", ForceId.Neutral));
        return parts;
    }

    /// <summary>The whole line as plain text (the rails and the log).</summary>
    public static string Text(in Entry e, int viewerUnitNet) => string.Concat(Line(e, viewerUnitNet).Select(s => s.Text));

    private static readonly ConditionalWeakTable<EntityRepository, KillFeed> ByWorld = new();

    /// <summary>The world's feed, or null when none was ever recorded.</summary>
    public static KillFeed? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var f) ? f : null;

    /// <summary>The world's feed, made on first use.</summary>
    public static KillFeed In(EntityRepository world) => ByWorld.GetValue(world, _ => new KillFeed());
}
