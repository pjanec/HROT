using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Jobs;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ CE-2468 (DESIGN_Minefields.md §9b) — why a place is under fire: shelling (<see cref="Indirect"/>) or ground a known enemy covers (<see cref="Direct"/>).</summary>
    public enum UnderFireCause : byte { Indirect = 1, Direct = 2 }

    /// <summary>
    /// ⭐⭐ CE-2468 (DESIGN_Minefields.md §9b, §9b.1 K1–K13, R-2140) — ONE side's knowledge of ONE place under fire, written by ONE deciding node (readers take the union:
    /// <see cref="UnderFireTable"/>): the shared <see cref="PlaceKnowledgeHeader"/> (<see cref="SubjectKind.Place"/>, <see cref="PlaceKnowledgeKind.UnderFire"/>,
    /// <see cref="PlaceKnowledgeHeader.State"/> = <see cref="StateActive"/> or <see cref="StateCooled"/>, <see cref="PlaceKnowledgeHeader.LearnedAt"/> = the last EVIDENCE) +
    /// the cause, the point FROZEN at the first learn (K7), the range and the enemy strength seen. ⭐ Nothing lasts forever: a record is ignored
    /// <see cref="UnderFireRules.CoolSeconds"/> after its last evidence (the writer marks it Cooled — a change, so the tables drop it, K4) and RETIRED at twice that.
    /// Unmanaged, recorded, replicated (<c>dtUnderFireKnowledge</c>); saved by AGE (K1), never as an entity.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.UnderFireKnowledge)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct UnderFireKnowledge : IKeyedRecord<KnowledgeKey, UnderFireItem>
    {
        /// <summary>⭐ TKB 7211 <c>UnderFireKnowledgeRecord</c> (the record block; 7202–7210 taken — measured on every branch 2026-11-21).</summary>
        public const long TkbType = 7211;
        public const byte StateActive = 0, StateCooled = 1;
        /// <summary>⭐ CE-2468 UF-3 (K1) — made by a scenario LOAD: <see cref="PlaceKnowledgeHeader.LearnedAt"/> holds the saved AGE (seconds), not a time, until the owning node's writer
        /// rebases it on its first tick (learned-at = now − age) — whatever the clock did between the load and the first tick. No reader treats it as fresh meanwhile.</summary>
        public const byte StateLoaded = 2;

        public PlaceKnowledgeHeader Header;
        public UnderFireCause Cause;
        /// <summary>Where it is under fire — FROZEN at the first learn; later evidence within <see cref="UnderFireRules.MergeM"/> updates this record (K7).</summary>
        public Vector3 Point;
        /// <summary><see cref="UnderFireCause.Indirect"/>: the shelled radius · <see cref="UnderFireCause.Direct"/>: the enemy's covering range.</summary>
        public float RangeM;
        /// <summary><see cref="UnderFireCause.Direct"/>: the enemy danger seen at the break (Squad Tactics R4); 0 for <see cref="UnderFireCause.Indirect"/>.</summary>
        public float EnemyStrength;

        KnowledgeKey IKeyedRecord<KnowledgeKey, UnderFireItem>.RecordKey
        {
            readonly get => new(Header.Subject, Header.SidePlus1);
            set { Header.Subject = value.Field; Header.SidePlus1 = value.SidePlus1; Header.SubjectKind = SubjectKind.Place; Header.Kind = PlaceKnowledgeKind.UnderFire; Header.ObjectIndex = -1; }
        }

        public readonly bool IsFull => false;   // one record per (place, side, node)

        /// <summary>True when <paramref name="item"/> is no newer than the evidence already held (and the record is not Cooled) — a refresh with the same time changes nothing (K6).</summary>
        public readonly bool Holds(in UnderFireItem item) => Cause != 0 && item.At <= Header.LearnedAt;

        /// <summary>Merge: NEWEST wins — the time, the range, the strength, the source; the point and the cause stay those of the first learn; new evidence wakes a Cooled record.</summary>
        public bool TryAdd(in UnderFireItem item)
        {
            if (Cause == 0) { Cause = item.Cause; Point = item.Point; }
            if (item.At > Header.LearnedAt || Header.LearnedAt == default)
            {
                Header.LearnedAt = item.At;
                RangeM = item.RangeM;
                EnemyStrength = item.EnemyStrength;
                Header.Source = item.Source;
                Header.State = StateActive;
            }
            return true;
        }

        /// <summary>Is this record fresh at <paramref name="now"/> — not Cooled, and inside its cause's cool time?</summary>
        public readonly bool FreshAt(double now) => Header.State == StateActive && now - Header.LearnedAt.Seconds < UnderFireRules.CoolSeconds(Cause);
    }

    /// <summary>One piece of evidence for a side: the writer's item (see <see cref="UnderFireEvents"/>).</summary>
    public readonly record struct UnderFireItem(KnowledgeKey Key, UnderFireCause Cause, Vector3 Point, float RangeM, float EnemyStrength, KnowledgeSource Source, SimInstant At);

    /// <summary>
    /// ⭐ CE-2468 — a side LEARNED a place under fire (<see cref="TerrainEventIds.UnderFireLearned"/>): raised by the detectors (<see cref="UnderFireEvents"/>), read by the
    /// serial <see cref="UnderFireWriter"/> on the node that raised it. Local; never on the wire (the record is).
    /// </summary>
    [EventId(TerrainEventIds.UnderFireLearned)]
    [StructLayout(LayoutKind.Sequential)]
    public struct UnderFireLearned
    {
        public byte SidePlus1;
        public UnderFireCause Cause;
        public Vector3 Point;
        public float RangeM;
        public float EnemyStrength;
        public SimInstant At;
    }

    /// <summary>
    /// ⭐⭐ CE-2468 (§9b, the contract behaviors builds against — NOTE_Backend_To_Behaviors_UnderFire_Contract.md) — how a detector says a side learned a place under fire.
    /// Every form raises <see cref="UnderFireLearned"/>; the serial writer applies it next tick (R-4212), so all of them are callable from the parallel brain phase.
    /// <paramref name="side"/> is the <c>ForceId</c> (a negative side is ignored); <paramref name="at"/> is the sim instant of the EVIDENCE (never "now" for a refresh, K6).
    /// </summary>
    public static class UnderFireEvents
    {
        /// <summary>A won break contact (Squad Tactics R1): the enemy at <paramref name="point"/> covers <paramref name="rangeM"/> around it.</summary>
        public static void LearnDirect(BatchContext ctx, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at)
            => LearnDirect(ctx.Commands, side, point, rangeM, enemyStrength, at);

        /// <summary>The same through a command buffer (merged in batch order after the phase).</summary>
        public static void LearnDirect(EntityCommandBuffer buffer, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at)
        {
            if (side < 0 || side > 254) return;
            buffer.PublishEvent(Make(side, UnderFireCause.Direct, point, rangeM, enemyStrength, at));
        }

        /// <summary>The same on a world's bus: serial, or inside a phase (the bus defers a phase's publish into its batch by itself).</summary>
        public static void LearnDirect(EntityRepository world, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at)
        {
            if (side < 0 || side > 254 || !world.Bus.IsRegistered<UnderFireLearned>()) return;
            world.Bus.Publish(Make(side, UnderFireCause.Direct, point, rangeM, enemyStrength, at));
        }

        /// <summary>A witnessed shelling (<c>ShelledAreas</c>, UF-7): bursts keep landing within <paramref name="radiusM"/> of <paramref name="point"/>; <paramref name="at"/> = the last BURST.</summary>
        public static void LearnIndirect(EntityRepository world, int side, Vector3 point, float radiusM, SimInstant at)
        {
            if (side < 0 || side > 254 || !world.Bus.IsRegistered<UnderFireLearned>()) return;
            world.Bus.Publish(Make(side, UnderFireCause.Indirect, point, radiusM, 0f, at));
        }

        private static UnderFireLearned Make(int side, UnderFireCause cause, Vector3 point, float rangeM, float strength, SimInstant at)
            => new() { SidePlus1 = (byte)(side + 1), Cause = cause, Point = point, RangeM = rangeM, EnemyStrength = strength, At = at };
    }

    /// <summary>⭐ CE-2468 — the kind's fixed rules (tuning values named in DESIGN_Minefields.md §9b).</summary>
    public static class UnderFireRules
    {
        /// <summary>⭐ §9b — an <see cref="UnderFireCause.Indirect"/> record is ignored this long after its last evidence (the shelled area's own 120 s).</summary>
        public const double IndirectCoolSeconds = 120.0;
        /// <summary>⭐ §9b — a <see cref="UnderFireCause.Direct"/> record's cool time.</summary>
        public const double DirectCoolSeconds = 600.0;
        /// <summary>⭐ K7 — evidence within this distance of a record's frozen point updates that record (the subject grid's own size — lane decision D-UF2a).</summary>
        public const float MergeM = 25f;
        /// <summary>⭐ K7 — the grid the subject key is derived on (from the FIRST centre).</summary>
        public const float SubjectGridM = 25f;

        public static double CoolSeconds(UnderFireCause cause) => cause == UnderFireCause.Direct ? DirectCoolSeconds : IndirectCoolSeconds;

        /// <summary>
        /// ⭐ K7 — the stable subject of a NEW record: name-based, from the cause and the 25 m cell of the first centre — the same on every node and every run, so two
        /// nodes that learn the same place first in the same cell write the same key (and the union merges them).
        /// </summary>
        public static Guid SubjectOf(UnderFireCause cause, Vector3 firstPoint)
        {
            int cx = (int)MathF.Floor(firstPoint.X / SubjectGridM), cy = (int)MathF.Floor(firstPoint.Y / SubjectGridM);
            return new Guid(unchecked((int)0x55464B30) | (byte)cause, 0x4E44, 0x4B4E,
                (byte)cx, (byte)(cx >> 8), (byte)(cx >> 16), (byte)(cx >> 24), (byte)cy, (byte)(cy >> 8), (byte)(cy >> 16), (byte)(cy >> 24));
        }
    }

    /// <summary>⭐⭐ CE-2468 (the contract) — a side's fresh knowledge of one place under fire, as a reader sees it (<see cref="UnderFireTable.NearestFresh"/>).</summary>
    public readonly struct UnderFireRecord
    {
        public UnderFireRecord(UnderFireCause cause, Vector3 point, float rangeM, float enemyStrength, SimInstant learnedAt)
        { Cause = cause; Point = point; RangeM = rangeM; EnemyStrength = enemyStrength; LearnedAt = learnedAt; }

        public UnderFireCause Cause { get; }
        public Vector3 Point { get; }
        public float RangeM { get; }
        public float EnemyStrength { get; }
        public SimInstant LearnedAt { get; }
    }
}
