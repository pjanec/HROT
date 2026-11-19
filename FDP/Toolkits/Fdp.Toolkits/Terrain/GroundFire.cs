using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-3487</c> (flame FL-3 N-1; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §7b) — "a ground fire spot starts here": published by the napalm decider
    /// (<c>NapalmSystem</c>) on the detonation's node, read by <see cref="GroundFireSystem"/> the same frame. The spot in WORLD metres. LOCAL (the record replicates,
    /// the event does not).
    /// </summary>
    [EventId(TerrainEventIds.GroundFire)]
    [StructLayout(LayoutKind.Sequential)]
    public struct GroundFireEvent
    {
        public float X, Y, Z, Radius;
        public SimInstant Since;
        public float Seconds, Intensity;

        public readonly FireSpot Spot => new(X, Y, Z, Radius, Since, Seconds, Intensity);

        public static GroundFireEvent Of(in FireSpot s) => new()
        {
            X = s.X, Y = s.Y, Z = s.Z, Radius = s.Radius, Since = s.Since, Seconds = s.Seconds, Intensity = s.Intensity,
        };
    }

    /// <summary>⭐ <c>CE-3487</c> — the rules of a terrain cell's ground fire: its record's TKB type, its cell.</summary>
    public static class GroundFireRules
    {
        /// <summary>⭐ The TKB type of a ground-fire record entity (bare: the spots travel on <c>FireState</c> / <c>dtFireState</c>; 7210 free on every lane,
        /// <c>2026-11-19</c>).</summary>
        public const long TkbType = 7210;

        /// <summary>The ground cell a spot belongs to (the crater record's cells, <see cref="GroundDamage.CellSize"/> m).</summary>
        public static (int X, int Y) CellOf(in FireSpot s) => GroundDamages.CellOf(new Vector2(s.X, s.Y));

        /// <summary>Is <paramref name="e"/> a ground-fire record (not a burning vehicle, which holds a <c>FireState</c> too)?</summary>
        public static bool IsRecord(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<TkbIdentity>() && repo.HasComponent<TkbIdentity>(e) && (long)repo.GetComponentRO<TkbIdentity>(e).TkbType == TkbType;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3487</c> (flame FL-3 N-1, N-6; thermal drivers TD-6: "a terrain cell's record entity" is the third FireState holder) — turns
    /// <see cref="GroundFireEvent"/>s into ground-fire records: THE cell-record writer (<see cref="CellRecordSystem{TRecord, TItem}"/>, CE-5000) with
    /// <see cref="FireState"/> as its record. Runs where the detonations are decided (SimHost's <c>DamageAssessmentModule</c>, beside the craters). Each spot burns
    /// by itself (<c>BurnSystem</c> burns the bodies in it on their Health owner); ⭐ a spot that has GONE OUT leaves a <see cref="HeatMarkKind.BurnedArea"/> heat mark
    /// once, and its slot is freed (its seconds set to 0). ⛔ Never in a replay; writes only records this node owns.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class GroundFireSystem : CellRecordSystem<FireState, FireSpot>
    {
        /// <summary>The wire ordinal of <c>EDescriptorType.dtFireState</c> (NED). ⚠ A rail pins the two equal.</summary>
        public const long DescriptorOrdinal = 241;
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

        /// <summary>Spots that went out and left their scorch (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of scorches left")]
        public long Scorched { get; private set; }

        private EntityQuery? _records;
        private EntityRepository? _recordsRepo;

        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => GroundFireRules.TkbType;
        protected override (int X, int Y) CellOf(in FireSpot item) => GroundFireRules.CellOf(in item);
        protected override bool IsRecord(EntityRepository repo, Entity e) => GroundFireRules.IsRecord(repo, e);

        protected override void ReadItems(EntityRepository repo, List<FireSpot> into)
        {
            if (!repo.Bus.IsRegistered<GroundFireEvent>()) return;
            var events = repo.ReadEvents<GroundFireEvent>();
            for (int i = 0; i < events.Length; i++) { var s = events[i].Spot; if (s.Radius > 0f && s.Seconds > 0f) into.Add(s); }
        }

        /// <summary>⭐ N-6 — each owned record's spots that have gone out: a scorch where it burned, then the slot freed.</summary>
        protected override void AfterApply(EntityRepository repo)
        {
            if (!repo.HasSingletonUnmanaged<GlobalTime>()) return;
            double now = repo.GetSingletonUnmanagedRO<GlobalTime>().TotalTime;
            bool marks = repo.Bus.IsRegistered<GroundHeatMarkEvent>();
            if (!ReferenceEquals(repo, _recordsRepo)) { _records = repo.Query().With<FireState>().With<TkbIdentity>().Build(); _recordsRepo = repo; }
            foreach (var e in _records!)
            {
                if (!GroundFireRules.IsRecord(repo, e) || !Owns(repo, e)) continue;
                ref readonly var fire = ref repo.GetComponentRO<FireState>(e);
                int outAt = -1;
                for (int i = 0; i < fire.Count; i++)
                {
                    var s = fire.Get(i);
                    if (s.Seconds > 0f && s.IsOut(now)) { outAt = i; break; }
                }
                if (outAt < 0) continue;
                var next = fire;   // a new value, set whole (an unmanaged record: Set bumps its version, so the recorder sees it)
                var anchor = GroundDamages.Anchor(next.RecordCellX, next.RecordCellY, 0f);
                for (int i = 0; i < next.Count; i++)
                {
                    var s = next.Get(i);
                    if (!(s.Seconds > 0f && s.IsOut(now))) continue;
                    if (marks)
                    {
                        var at = new Vector2(anchor.X + s.X, anchor.Y + s.Y);
                        var mark = new HeatMark(HeatMarkKind.BurnedArea, at, 0f, s.Radius * 2f, s.Radius * 2f, HeatMarkEnd.NoHotEnd,
                                                s.Since, (SimInstant)(s.Since.Seconds + s.Seconds), s.Seconds, default);
                        repo.Bus.Publish(GroundHeatMarkEvent.Of(in mark));
                    }
                    next.Set(i, new FireSpot(s.X, s.Y, s.Z, s.Radius, s.Since, 0f, s.Intensity));   // out for good: its slot may take a new spot
                    Scorched++;
                }
                repo.SetComponent(e, next);
            }
        }
    }
}
