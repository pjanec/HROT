using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-T1 (MF-3, MR-1, KR-3) — writes a field's GROUND TRUTH: every <see cref="MineStruck"/> becomes a (row, col, struck) entry of the field's
    /// <see cref="MinefieldStrikes"/> on the deciding node, through the ONE keyed record writer (<see cref="RecordWriterCore{TRecord,TKey,TItem}"/>), keyed by the field's
    /// stable id. Serial; never in a replay.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class MinefieldStrikesSystem : RecordWriterCore<MinefieldStrikes, Guid, StrikeItem>
    {
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(Minefields.StrikesDescriptorOrdinal, 0);
        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => MinefieldStrikes.TkbType;
        protected override Guid KeyOf(in StrikeItem item) => item.Field;
        protected override MinefieldStrikes NewRecord(in Guid key, int existing) => new() { Field = key, Chunk = (ushort)existing };

        protected override void ReadItems(EntityRepository repo, List<StrikeItem> into)
        {
            if (!repo.Bus.IsRegistered<MineStruck>() || !repo.IsComponentTypeRegistered<EntityGuid>()) return;
            foreach (ref readonly var s in repo.Bus.Read<MineStruck>())
                if (MinefieldIds.TryGuid(repo, s.Field, out var g)) into.Add(new StrikeItem(g, s.Row, s.Col, MineGone.Struck));
        }

        /// <summary>True when mine (<paramref name="row"/>, <paramref name="col"/>) of <paramref name="field"/> is in any node's strikes record (the union — a mine goes off once).</summary>
        public static bool IsGone(ISimulationView view, Guid field, short row, short col)
        {
            foreach (var e in view.Query().With<MinefieldStrikes>().WithLifecycle(EntityLifecycle.AllButTearDown).Build())
            {
                var s = view.GetComponentRO<MinefieldStrikes>(e);
                if (s.Field == field && s.Has(row, col)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T2 (MF-13, MF-17, MR-2, MR-12, MR-13, KR-5, KR-6) — THE place-knowledge writer, on EVERY deciding node (the SimHost where strikes happen, the CGF
    /// node where recon brains run), writing only its OWN <see cref="MinefieldKnowledge"/> entities through the shared keyed writer; every reader takes the UNION (the lattice max,
    /// <see cref="PlaceKnowledgeTable"/>). It learns from events, never from a parallel phase (R-4212):
    /// <list type="bullet">
    /// <item>a <see cref="MineStruck"/> teaches every side with a LIVING member within <see cref="WitnessRadius"/> of the blast (the victim's side included) — the cells within
    /// <see cref="SuspectRadius"/> become SUSPECTED (doctrine: a unit that hits a mine assumes it is IN a minefield);</item>
    /// <item>a <see cref="MinefieldCellsSwept"/> (the recon brain) makes cells known-mined / known-clear for its side;</item>
    /// <item>a <see cref="MinefieldMarked"/> marks the field for its side; and once a second, a MARKED field is learned (marked) by any OTHER side whose living unit stands within
    /// <see cref="EdgeSightRadius"/> of its edge (MR-13: marking is physical).</item>
    /// </list>
    /// Known-at-start is NOT a record: the field's own <see cref="Minefield.KnownAtStartSides"/> answers it (lane decision R-5046).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class PlaceKnowledgeSystem : RecordWriterCore<MinefieldKnowledge, KnowledgeKey, KnowledgeItem>
    {
        public const float WitnessRadius = 300f, SuspectRadius = 50f, EdgeSightRadius = 30f;
        /// <summary>Ticks between the "a marked field's edge seen" checks.</summary>
        public const int EdgeCheckTicks = 30;

        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(Minefields.KnowledgeDescriptorOrdinal, 0);
        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => MinefieldKnowledge.TkbType;
        protected override KnowledgeKey KeyOf(in KnowledgeItem item) => item.Key;
        protected override MinefieldKnowledge NewRecord(in KnowledgeKey key, int existing)
        {
            var r = new MinefieldKnowledge();
            r.Header.Subject = key.Field; r.Header.SidePlus1 = key.SidePlus1; r.Header.SubjectKind = SubjectKind.Entity; r.Header.Kind = PlaceKnowledgeKind.Minefield;
            r.Header.ObjectIndex = -1; r.Header.Chunk = (ushort)existing;
            return r;
        }

        private readonly List<Vector2> _outline = new();
        private readonly List<int> _sides = new();
        private int _tick;

        // ⭐ a host that composes BOTH the SimHost and the CGF packs in one world (the editor) registers it twice: the first instance claims the world, the other stays idle
        //   (two writers would each keep their own pending set and could open two records for one key)
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, PlaceKnowledgeSystem> Claims = new();
        protected override bool ShouldRun(EntityRepository repo) => ReferenceEquals(Claims.GetValue(repo, _ => this), this);

        protected override void ReadItems(EntityRepository repo, List<KnowledgeItem> into)
        {
            var now = (SimInstant)Fdp.Toolkit.Combat.HitModel.Now(repo);
            if (repo.Bus.IsRegistered<MineStruck>())
                foreach (ref readonly var s in repo.Bus.Read<MineStruck>()) Strike(repo, s, now, into);
            if (repo.Bus.IsRegistered<MinefieldCellsSwept>())
                foreach (ref readonly var s in repo.Bus.Read<MinefieldCellsSwept>()) Swept(repo, s, now, into);
            if (repo.Bus.IsRegistered<MinefieldMarked>())
                foreach (ref readonly var m in repo.Bus.Read<MinefieldMarked>())
                    if (m.SidePlus1 != 0 && MinefieldIds.TryGuid(repo, m.Field, out var g))
                        into.Add(new KnowledgeItem(new KnowledgeKey(g, m.SidePlus1), KnowledgeItem.MarkOnly, 0, MineCellKnowledge.Unknown, 1, KnowledgeSource.Marking, now));
        }

        private void Strike(EntityRepository repo, in MineStruck s, SimInstant now, List<KnowledgeItem> into)
        {
            if (!MinefieldIds.TryGuid(repo, s.Field, out var g) || !repo.HasComponent<Minefield>(s.Field)) return;
            if (!MinefieldKnowledgeOps.TryOutline(repo, s.Field, _outline, out var anchor)) return;
            var f = repo.GetComponentRO<Minefield>(s.Field);
            var grid = MinefieldKnowledgeOps.Grid(f, anchor, _outline);
            var blast = new Vector2(s.X, s.Y);
            WitnessSides(repo, blast, s.By, _sides);
            int reach = (int)MathF.Ceiling(SuspectRadius / grid.Cell);
            if (!MinefieldKnowledgeOps.CellOf(grid, blast, out int bc, out int br))
            {
                // the blast's cell clamped onto the grid (a mine on the outline's edge)
                bc = Math.Clamp(bc, 0, grid.Cols - 1); br = Math.Clamp(br, 0, grid.Rows - 1);
            }
            for (int si = 0; si < _sides.Count; si++)
            {
                var key = new KnowledgeKey(g, (byte)(_sides[si] + 1));
                for (int r = Math.Max(0, br - reach); r <= Math.Min(grid.Rows - 1, br + reach); r++)
                    for (int c = Math.Max(0, bc - reach); c <= Math.Min(grid.Cols - 1, bc + reach); c++)
                        if (Vector2.Distance(MinefieldKnowledgeOps.CentreOf(grid, c, r), blast) <= SuspectRadius + grid.Cell * 0.5f)
                            into.Add(new KnowledgeItem(key, (byte)c, (byte)r, MineCellKnowledge.Suspected, 0, KnowledgeSource.Strike, now));
            }
        }

        private static unsafe void Swept(EntityRepository repo, in MinefieldCellsSwept s, SimInstant now, List<KnowledgeItem> into)
        {
            if (s.SidePlus1 == 0 || !MinefieldIds.TryGuid(repo, s.Field, out var g)) return;
            var key = new KnowledgeKey(g, s.SidePlus1);
            for (int i = 0; i < s.Count && i < MinefieldCellsSwept.MaxCells; i++)
            {
                var found = (MineCellKnowledge)s.Found[i];
                if (found is not (MineCellKnowledge.KnownMined or MineCellKnowledge.KnownClear)) continue;
                into.Add(new KnowledgeItem(key, s.Col[i], s.Row[i], found, 0, KnowledgeSource.Recon, now));
            }
        }

        /// <summary>The sides with a LIVING member within <see cref="WitnessRadius"/> of <paramref name="at"/>, the victim's side first (MR-13). Cold: a strike is rare.</summary>
        public static void WitnessSides(EntityRepository repo, Vector2 at, Entity victim, List<int> into)
        {
            into.Clear();
            if (!repo.IsComponentTypeRegistered<EntityInfo>()) return;
            if (repo.IsAlive(victim) && repo.HasComponent<EntityInfo>(victim)) into.Add((int)repo.GetComponentRO<EntityInfo>(victim).ForceId);
            bool health = repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>();
            float r2 = WitnessRadius * WitnessRadius;
            foreach (var e in repo.Query().With<EntityInfo>().With<SimTransform>().Build())
            {
                int side = (int)repo.GetComponentRO<EntityInfo>(e).ForceId;
                if (into.Contains(side)) continue;
                if (health && repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e) && repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current <= 0f) continue;
                if (!(health && repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e))) continue;   // a living MEMBER: a thing with health (not a graphic, not a field)
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                if (Vector2.DistanceSquared(new Vector2(p.X, p.Y), at) <= r2) into.Add(side);
            }
        }

        protected override void BeforeItems(EntityRepository repo, List<KnowledgeItem> into)
        {
            if (++_tick % EdgeCheckTicks != 0 || !repo.IsComponentTypeRegistered<Minefield>()) return;
            var table = PlaceKnowledgeTable.Of(repo);
            if (table.MarkedCount == 0) return;
            var now = (SimInstant)Fdp.Toolkit.Combat.HitModel.Now(repo);
            bool health = repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>();
            foreach (var field in repo.Query().With<Minefield>().With<SimTransform>().Build())
            {
                if (!table.MarkedByAnySide(field) || !MinefieldIds.TryGuid(repo, field, out var g)) continue;
                if (!MinefieldKnowledgeOps.TryOutline(repo, field, _outline, out _)) continue;
                foreach (var e in repo.Query().With<EntityInfo>().With<SimTransform>().Build())
                {
                    if (!(health && repo.HasComponent<Fdp.Toolkit.Combat.Components.Health>(e)) || repo.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current <= 0f) continue;
                    int side = (int)repo.GetComponentRO<EntityInfo>(e).ForceId;
                    if (table.Marked(side, field)) continue;
                    var p = repo.GetComponentRO<SimTransform>(e).Position;
                    if (DistanceToOutline(_outline, new Vector2(p.X, p.Y)) <= EdgeSightRadius)
                        into.Add(new KnowledgeItem(new KnowledgeKey(g, (byte)(side + 1)), KnowledgeItem.MarkOnly, 0, MineCellKnowledge.Unknown, 1, KnowledgeSource.Marking, now));
                }
            }
        }

        private static float DistanceToOutline(List<Vector2> poly, Vector2 p)
        {
            if (MinePattern.Inside(poly, p)) return 0f;
            float best = float.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[j]; var d = poly[i] - a; float len2 = d.LengthSquared();
                float t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, d) / len2, 0f, 1f);
                best = MathF.Min(best, Vector2.Distance(a + d * t, p));
            }
            return best;
        }
    }

    /// <summary>A field entity's stable id.</summary>
    public static class MinefieldIds
    {
        public static bool TryGuid(ISimulationView view, Entity field, out Guid guid)
        {
            guid = default;
            if (field.IsNull || !view.IsAlive(field) || !view.HasComponent<EntityGuid>(field)) return false;
            guid = view.GetComponentRO<EntityGuid>(field).Value;
            return guid != Guid.Empty;
        }
    }
}
