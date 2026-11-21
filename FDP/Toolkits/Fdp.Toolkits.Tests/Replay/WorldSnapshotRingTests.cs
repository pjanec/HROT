using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration.Preview;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Fdp.Toolkits.Tests.Replay
{
    /// <summary>
    /// ⭐ Edit-mode undo B-13 (docs/UX/DESIGN_Edit_Mode_Undo.md §2.1, §2.3, U3) — the ring of before-states: a restore puts the world back exactly
    /// (entity handles included) through the dry run's exit sequence, the non-ECS participants with it; the budget and the depth evict the
    /// oldest; a new edit after an undo drops the redo branch; the caches that are not version-compared forget on a restore.
    /// </summary>
    public sealed class WorldSnapshotRingTests
    {
        private static EntityRepository NewWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<KeyframeTestPos>();
            return repo;
        }

        private sealed class Counter : IPreviewRewindable
        {
            public int Value;
            public string Name => "counter";
            public object? Capture() => Value;
            public void Restore(object snapshot) => Value = (int)snapshot;
        }

        /// <summary>✅ Red-proof: Restore without the bracket's restore ⇒ the participant keeps 7 ⇒ red.</summary>
        [Fact]
        public void B13_ARestore_PutsTheWorldAndTheParticipantsBack_AndAdvancesTheEpoch()
        {
            using var repo = NewWorld();
            var counter = new Counter { Value = 3 };
            var ring = new WorldSnapshotRing(new IPreviewRewindable[] { counter });
            var a = repo.CreateEntity(); repo.SetComponent(a, new KeyframeTestPos { X = 1f });
            repo.Tick();

            long before = ring.Capture(repo);
            int epoch = WorldEpoch.Of(repo), seen = 0;
            WorldEpoch.Moved(repo, ref seen);

            repo.SetComponent(a, new KeyframeTestPos { X = 99f });
            var made = repo.CreateEntity(); repo.SetComponent(made, new KeyframeTestPos { X = 5f });
            counter.Value = 7;
            uint version = repo.GlobalVersion;

            Assert.True(ring.Restore(repo, before));
            Assert.Equal(1f, repo.GetComponent<KeyframeTestPos>(a).X);
            Assert.False(repo.IsAlive(made));
            Assert.Equal(3, counter.Value);
            Assert.True(repo.GlobalVersion >= version);          // ⭐ D8 — versions only grow
            Assert.True(WorldEpoch.Moved(repo, ref seen));        // ⭐ U3 — every epoch-keyed cache sees the restore
            Assert.True(ring.Holds(before));                     // the slot stays (redo goes to another slot)
        }

        [Fact]
        public void B13_TheDepthAndTheBudget_EvictTheOldest_AndAnEvictedSlotRefusesToRestore()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new KeyframeTestPos { X = 0f });
            var ring = new WorldSnapshotRing(maxDepth: 3);
            long first = ring.Capture(repo);
            for (int i = 0; i < 3; i++) ring.Capture(repo);
            Assert.Equal(3, ring.Count);
            Assert.False(ring.Holds(first));
            Assert.False(ring.Restore(repo, first));

            var tiny = new WorldSnapshotRing(budgetBytes: 1);    // every capture is over the budget
            tiny.Capture(repo);
            long newest = tiny.Capture(repo);
            Assert.Equal(1, tiny.Count);                         // the newest is always kept
            Assert.True(tiny.Holds(newest));
            Assert.Equal(tiny.UsedBytes, WorldKeyframes.Capture(repo, CaptureOptions.Undo).SizeBytes);
        }

        [Fact]
        public void B13_DropFrom_ForgetsTheRedoBranch_AndClearForgetsAll()
        {
            using var repo = NewWorld();
            var ring = new WorldSnapshotRing();
            long s1 = ring.Capture(repo), s2 = ring.Capture(repo), s3 = ring.Capture(repo);
            ring.DropFrom(s2);
            Assert.True(ring.Holds(s1));
            Assert.False(ring.Holds(s2));
            Assert.False(ring.Holds(s3));
            ring.Clear();
            Assert.Equal(0, ring.Count);
            Assert.Equal(0, ring.UsedBytes);
        }

        /// <summary>⭐ U3 — a cache keyed by entity HANDLE that no version compare covers: the rules' pending request ids are another world's after a
        /// restore. ✅ Red-proof: RuleRequestIds without the epoch check ⇒ the id survives the restore ⇒ red.</summary>
        [Fact]
        public void U3_ThePendingRuleRequestIds_AreForgottenOnARestore()
        {
            using var repo = NewWorld();
            var rule = repo.CreateEntity();
            var ring = new WorldSnapshotRing();
            long before = ring.Capture(repo);
            var id = Guid.NewGuid();
            RuleRequestIds.Set(repo, rule, id);
            ring.Restore(repo, before);
            Assert.Equal(Guid.Empty, RuleRequestIds.Take(repo, rule));

            RuleRequestIds.Set(repo, rule, id);                  // without a restore it is kept
            Assert.Equal(id, RuleRequestIds.Take(repo, rule));
        }

        /// <summary>⭐ U3 — the editor's link scope (a 250 ms rebuild throttle) rebuilds at once after a restore, even with the entity count unchanged.
        /// ✅ Red-proof: LiveRefScope without the epoch check ⇒ no rebuild inside the throttle ⇒ red.</summary>
        [Fact]
        public void U3_TheEditorsLinkScope_RebuildsAtOnceAfterARestore()
        {
            using var repo = NewWorld();
            long clock = 0;
            var scope = new Fdp.Toolkit.Scenario.LiveRefScope(repo, () => clock, TimeSpan.FromSeconds(10));
            var ring = new WorldSnapshotRing();
            long before = ring.Capture(repo);
            ProbeQuery(scope);
            int rebuilds = scope.Rebuilds;
            ProbeQuery(scope);
            Assert.Equal(rebuilds, scope.Rebuilds);              // within the throttle, nothing changed: no rebuild
            ring.Restore(repo, before);
            ProbeQuery(scope);
            Assert.Equal(rebuilds + 1, scope.Rebuilds);
        }

        private static void ProbeQuery(Fdp.Toolkit.Scenario.LiveRefScope scope) => scope.TryPositionOf(new Fdp.Toolkit.Replication.EntityRef(1), out _);
    }
}
