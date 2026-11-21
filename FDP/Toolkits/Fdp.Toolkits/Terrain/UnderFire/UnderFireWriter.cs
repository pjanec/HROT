using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ CE-2468 UF-2 (DESIGN_Minefields.md §9b, §9b.1 K2, K4, K6, K7, K13) — THE writer of <see cref="UnderFireKnowledge"/>, on every deciding node (the SimHost where the
    /// bursts land, the CGF node where the brains break contact), through the shared keyed writer: one record per (place, side) per node, the readers take the union.
    /// <list type="bullet">
    /// <item>⭐ it LEARNS from <see cref="UnderFireLearned"/> events only (never from a parallel phase, R-4212): evidence within <see cref="UnderFireRules.MergeM"/> of an
    /// existing record of the same side and cause updates THAT record (its frozen point stays); otherwise a new record keyed by <see cref="UnderFireRules.SubjectOf"/>.
    /// Merge: newest (<see cref="UnderFireKnowledge.TryAdd"/>).</item>
    /// <item>⭐ it AGES them — nothing lasts forever: every <see cref="CoolCheckTicks"/> ticks, by SIM time, a record past its cause's cool time is marked
    /// <see cref="UnderFireKnowledge.StateCooled"/> (a record change: every table rebuilt on a change drops it, K4), and one past TWICE that is retired (K2). Only this
    /// node's own records; an orphan is aged by the node that inherits its ownership.</item>
    /// </list>
    /// Serial; never in a replay (the recording carries the records).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class UnderFireWriter : RecordWriterCore<UnderFireKnowledge, KnowledgeKey, UnderFireItem>
    {
        /// <summary>The DDS descriptor of the record (<c>dtUnderFireKnowledge</c>, measured free on every branch 2026-11-21).</summary>
        public const long DescriptorOrdinal = 210;
        public static readonly long OwnershipKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
        /// <summary>Ticks between the ageing passes (½ s at 60 Hz: a cool time is minutes).</summary>
        public const int CoolCheckTicks = 30;

        protected override long DescriptorKey => OwnershipKey;
        protected override long TkbType => UnderFireKnowledge.TkbType;
        protected override UnderFireKnowledge NewRecord(in KnowledgeKey key, int existing)
        {
            var r = new UnderFireKnowledge();
            r.Header.Subject = key.Field; r.Header.SidePlus1 = key.SidePlus1; r.Header.SubjectKind = SubjectKind.Place; r.Header.Kind = PlaceKnowledgeKind.UnderFire;
            r.Header.ObjectIndex = -1; r.Header.Chunk = (ushort)existing;
            return r;
        }
        protected override Vector3 AnchorOf(EntityRepository repo, in KnowledgeKey key) => _anchors.TryGetValue(key, out var p) ? p : Vector3.Zero;

        [Fdp.Core.FrameScratch("this tick's first point per key — a new record stands at its frozen point; cleared at the top of ReadItems")]
        private readonly Dictionary<KnowledgeKey, Vector3> _anchors = new();
        [Fdp.Core.WorldDerived("tick counter for the ageing cadence")]
        private int _tick;
        [Fdp.Core.FrameScratch("keys to retire this ageing pass: cleared at the top of each pass")]
        private readonly List<KnowledgeKey> _retire = new();
        private EntityQuery? _all;
        private EntityRepository? _allRepo;

        // ⭐ a host that composes BOTH the SimHost and the CGF packs in one world (the editor) registers it twice: the first instance claims the world (PlaceKnowledgeSystem's rule)
        [Fdp.Core.WorldDerived("per-world claim table choosing which instance runs; recomputed on demand")]
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, UnderFireWriter> Claims = new();
        protected override bool ShouldRun(EntityRepository repo) => ReferenceEquals(Claims.GetValue(repo, _ => this), this);

        /// <summary>⭐ K7 — the record this evidence belongs to: the nearest record (any node's: the union) of the same side and cause whose frozen point is within
        /// <see cref="UnderFireRules.MergeM"/>, else a new subject from this point.</summary>
        protected override KnowledgeKey KeyOf(in UnderFireItem item) => item.Key;

        protected override void ReadItems(EntityRepository repo, List<UnderFireItem> into)
        {
            _anchors.Clear();
            if (!repo.Bus.IsRegistered<UnderFireLearned>()) return;
            foreach (ref readonly var e in repo.ReadEvents<UnderFireLearned>())
            {
                if (e.SidePlus1 == 0 || e.Cause is not (UnderFireCause.Indirect or UnderFireCause.Direct)) continue;
                // ⭐ K7 — a record within MergeM (any node's), else an item of THIS tick within MergeM (its record is not made yet), else a new subject
                var subject = Existing(repo, e.SidePlus1, e.Cause, e.Point, out var found) ? found
                    : EarlierThisTick(into, e.SidePlus1, e.Cause, e.Point, out found) ? found
                    : UnderFireRules.SubjectOf(e.Cause, e.Point);
                var key = new KnowledgeKey(subject, e.SidePlus1);
                if (!_anchors.ContainsKey(key)) _anchors[key] = e.Point;
                into.Add(new UnderFireItem(key, e.Cause, e.Point, e.RangeM, e.EnemyStrength,
                    e.Cause == UnderFireCause.Direct ? KnowledgeSource.Contact : KnowledgeSource.Burst, e.At));
            }
        }

        private static bool EarlierThisTick(List<UnderFireItem> items, byte sidePlus1, UnderFireCause cause, Vector3 point, out Guid subject)
        {
            subject = default;
            float best = UnderFireRules.MergeM * UnderFireRules.MergeM;
            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Key.SidePlus1 != sidePlus1 || it.Cause != cause) continue;
                float d = Vector2.DistanceSquared(new Vector2(it.Point.X, it.Point.Y), new Vector2(point.X, point.Y));
                if (d <= best) { best = d; subject = it.Key.Field; any = true; }
            }
            return any;
        }

        /// <summary>The subject of the nearest record of (side, cause) whose frozen point is within <see cref="UnderFireRules.MergeM"/> of <paramref name="point"/> (2-D).</summary>
        private bool Existing(EntityRepository repo, byte sidePlus1, UnderFireCause cause, Vector3 point, out Guid subject)
        {
            subject = default;
            float best = UnderFireRules.MergeM * UnderFireRules.MergeM;
            bool any = false;
            foreach (var e in All(repo))
            {
                ref readonly var r = ref repo.GetComponentRO<UnderFireKnowledge>(e);
                if (r.Header.SidePlus1 != sidePlus1 || r.Cause != cause) continue;
                float d = Vector2.DistanceSquared(new Vector2(r.Point.X, r.Point.Y), new Vector2(point.X, point.Y));
                if (d <= best) { best = d; subject = r.Header.Subject; any = true; }
            }
            return any;
        }

        /// <summary>⭐ K2 / K4 — the ageing pass over this node's OWN records, by sim time.</summary>
        protected override void BeforeItems(EntityRepository repo, List<UnderFireItem> into)
        {
            double now = Fdp.Toolkit.Combat.HitModel.Now(repo);
            Rebase(repo, now);   // a walk over this world's few under-fire records (0 B): a load can make some at any time
            if (++_tick % CoolCheckTicks != 0) return;
            _retire.Clear();
            foreach (var e in All(repo))
            {
                if (!Owns(repo, e)) continue;
                ref readonly var r = ref repo.GetComponentRO<UnderFireKnowledge>(e);
                double age = now - r.Header.LearnedAt.Seconds, cool = UnderFireRules.CoolSeconds(r.Cause);
                if (age >= 2 * cool) { _retire.Add(new KnowledgeKey(r.Header.Subject, r.Header.SidePlus1)); continue; }
                if (age >= cool && r.Header.State != UnderFireKnowledge.StateCooled)
                {
                    var cooled = r;
                    cooled.Header.State = UnderFireKnowledge.StateCooled;
                    repo.SetComponent(e, cooled);   // ⭐ K4 — a change: the per-side tables rebuild and drop it
                }
            }
            for (int i = 0; i < _retire.Count; i++) Retire(repo, _retire[i]);
        }

        /// <summary>⭐ CE-2468 UF-3 (K1) — a record a scenario load made holds its AGE: rebase it on this node's clock now (learned-at = now − age), then age it as any other.</summary>
        private void Rebase(EntityRepository repo, double now)
        {
            foreach (var e in All(repo))
            {
                ref readonly var r = ref repo.GetComponentRO<UnderFireKnowledge>(e);
                if (r.Header.State != UnderFireKnowledge.StateLoaded || !Owns(repo, e)) continue;   // another node's: its owner rebases it
                var rebased = r;
                rebased.Header.LearnedAt = (SimInstant)(now - r.Header.LearnedAt.Seconds);
                rebased.Header.State = UnderFireKnowledge.StateActive;   // the ageing pass cools / retires it by its rebased age
                repo.SetComponent(e, rebased);
            }
        }

        private EntityQuery All(EntityRepository repo)
        {
            if (!ReferenceEquals(repo, _allRepo)) { _all = repo.Query().With<UnderFireKnowledge>().WithLifecycle(EntityLifecycle.AllButTearDown).Build(); _allRepo = repo; }
            return _all!;
        }
    }
}
