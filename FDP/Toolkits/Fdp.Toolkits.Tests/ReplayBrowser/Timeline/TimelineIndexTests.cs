using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Utility;
using StructEdit.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> R1-a (docs/DESIGN_Director_Timeline_Editor.md §9) — the acceptance: a recorded run's shots, hits, blasts, spawns,
    /// deaths and a commander's orders come out of ONE pass in frame order; the second open reads the sidecar; a changed recording rebuilds.
    /// ⭐ <c>R-4079</c> (§9c): every lane here is a REGISTERED definition (the toolkits' providers) — what the R1-a extractors knew (a refused
    /// shot is not a shot; one event per new ring entry, a repeat included; a unit's first frame is its baseline) is now asserted against
    /// those definitions; a new or changed lane is the only one a later open scans; a user's lane indexes like a shipped one.
    /// </summary>
    public sealed class TimelineIndexTests
    {
        private const long Commander = 101, Soldier = 102, Late = 104;
        private readonly ITestOutputHelper _out;

        public TimelineIndexTests(ITestOutputHelper output) { _out = output; }

        private static readonly TimelineLaneContext Recording = new(Recording: true);

        private static TimelineLaneCatalog Shipped() => new TimelineLaneCatalog()
            .Register(new ReplicationTimelineLanes()).Register(new CombatTimelineLanes()).Register(new BehaviourTimelineLanes());

        private static IReadOnlyList<IndexedLaneDefinition> Lanes => Shipped().Indexed(Recording);

        private static TimelineLaneCompiler Compiler() => new(new EventScannerCompiler(new ComponentEditServiceBuilder().Build()));

        /// <summary>
        /// Four frames: 0 both units present · 1 a shot (and a REFUSED one), a hit on the commander, an order · 2 a blast, a decision
        /// switch, a journal entry · 3 the soldier is gone, a third unit arrives.
        /// </summary>
        private static string Record(FdpRecordingHarness harness)
        {
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<BrainJournal>();
            repo.RegisterComponent<CommandOrderHistory>();
            repo.RegisterComponent<UtilityDecisionLog>();

            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new NetworkIdentity(Commander));
            repo.AddComponent(commander, new SimTransform { Position = new Vector3(10, 20, 0) });
            repo.AddComponent(commander, new BrainJournal());
            repo.AddComponent(commander, default(CommandOrderHistory));
            var soldier = repo.CreateEntity();
            repo.AddComponent(soldier, new NetworkIdentity(Soldier));
            repo.AddComponent(soldier, new SimTransform { Position = new Vector3(30, 40, 0) });
            repo.AddComponent(soldier, default(UtilityDecisionLog));
            CommandHistoryOf.Order(repo, commander, soldier, 0x1111, 0f);                                     // before the recording: a baseline, not an event
            harness.Tick().RecordKeyframe(100_000L);                                                          // frame 0

            harness.FireUnmanagedEvent(new WeaponFireNotification { Shooter = soldier, Target = commander, Origin = new Vector3(30, 40, 1), Quantity = 3 });
            harness.FireUnmanagedEvent(new WeaponFireNotification { Shooter = soldier, Refusal = FireRefusal.TooFar });
            harness.FireUnmanagedEvent(new HitEvent { HitEntity = commander });
            CommandHistoryOf.Order(repo, commander, soldier, 0x5A11, 1f);
            CommandHistoryOf.Order(repo, commander, soldier, 0x7777, 1f);                                     // a second order the same frame: two events (TE-22 ②)
            harness.Tick().RecordDelta(200_000L);                                                             // frame 1

            harness.FireUnmanagedEvent(new DetonationNotification { Shooter = soldier, HitX = 5, HitY = 6, Damage = 0.5f });
            ref var slot = ref repo.GetComponentRW<UtilityDecisionLog>(soldier).SlotsRW()[0];
            slot.DecisionId = 0x2067; slot.SwitchCount = 1; slot.Winner = 7;
            ref var idle = ref repo.GetComponentRW<UtilityDecisionLog>(soldier).SlotsRW()[1];
            idle.DecisionId = 0x3001; idle.SwitchCount = 0; idle.Winner = 9;                                 // a decision that ran but never switched: not news
            CommandHistoryOf.Order(repo, commander, soldier, 0x7777, 2f);                                     // the same order REPEATED: counted, still news
            BrainJournalOf.Write(repo, commander, BrainJournalKind.TaskStarted, point: new Vector3(1, 2, 0));
            harness.Tick().RecordDelta(300_000L);                                                             // frame 2

            harness.DestroyEntity(soldier);
            var late = repo.CreateEntity();
            repo.AddComponent(late, new NetworkIdentity(Late));
            harness.Tick().RecordDelta(400_000L);                                                             // frame 3
            return harness.BuildToTempFile();
        }

        private static (int Frame, long Subject, long Other)[] Of(TimelineIndex index, TimelineLaneDescriptor lane)
            => index.Lane(lane.Id).Select(e => (e.Frame, e.Subject, e.Other)).ToArray();

        [Fact]
        public void CE1302_OnePass_YieldsEveryLane_InFrameOrder()
        {
            using var harness = new FdpRecordingHarness();
            string path = Record(harness);
            var chunks = new List<TimelineIndex>();

            var index = TimelineIndexBuilder.Build(path, Lanes, Compiler(), chunks.Add, chunkFrames: 1);

            Assert.Empty(index.LaneErrors);

            Assert.True(index.IsComplete);
            Assert.Equal(new long[] { 100_000, 200_000, 300_000, 400_000 }, index.FrameTicks);
            Assert.Equal(1, index.FrameAt(250_000));
            Assert.Equal(new[] { (0, Commander, 0L), (0, Soldier, 0L), (3, Late, 0L) }, Of(index, ReplicationTimelineLanes.Spawned).OrderBy(e => e.Frame).ThenBy(e => e.Subject));
            var died = Assert.Single(index.Lane(ReplicationTimelineLanes.Left.Id));
            Assert.Equal((3, Soldier, 30f, 40f), (died.Frame, died.Subject, died.X, died.Y));        // where it was last seen
            var shot = Assert.Single(index.Lane(CombatTimelineLanes.Shot.Id));                                    // ⛔ the refused one is not a shot
            Assert.Equal((1, Soldier, Commander, 30f, 3f), (shot.Frame, shot.Subject, shot.Other, shot.X, shot.Value));
            Assert.Equal(new[] { (1, Commander, 0L) }, Of(index, CombatTimelineLanes.Hit));
            var blast = Assert.Single(index.Lane(CombatTimelineLanes.Blast.Id));
            Assert.Equal((2, Soldier, 5f, 6f, 0.5f), (blast.Frame, blast.Subject, blast.X, blast.Y, blast.Value));
            Assert.Equal(new[] { (1, Commander, Soldier), (1, Commander, Soldier), (2, Commander, Soldier) }, Of(index, BehaviourTimelineLanes.Orders));   // to whom; the repeat too, not the baseline
            var decision = Assert.Single(index.Lane(BehaviourTimelineLanes.Decisions.Id));
            Assert.Equal((2, Soldier, 7L, (float)0x2067), (decision.Frame, decision.Subject, decision.Other, decision.Value));
            var change = Assert.Single(index.Lane(BehaviourTimelineLanes.Changes.Id));
            Assert.Equal((2, Commander, (float)BrainJournalKind.TaskStarted), (change.Frame, change.Subject, change.Value));
            Assert.Equal(1, index.Count(CombatTimelineLanes.Shot.Id, 0, 2));
            Assert.Equal(0, index.Count(CombatTimelineLanes.Shot.Id, 2, 4));

            Assert.Equal(new[] { 1, 2, 3, 4 }, chunks.Select(c => c.ScannedFrames));                    // progressive, the last complete
            Assert.True(chunks[^1].IsComplete && !chunks[0].IsComplete);
        }

        [Fact]
        public void CE1302_TheSecondOpen_ReadsTheSidecar_AndAChangedRecordingRebuilds()
        {
            using var harness = new FdpRecordingHarness();
            string path = Record(harness);
            try
            {
                using var first = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.False(first.FromSidecar);
                Assert.True(first.IsComplete);
                Assert.True(File.Exists(TimelineSidecar.PathFor(path)));

                using var second = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.True(second.FromSidecar);
                foreach (var lane in Lanes)
                    Assert.Equal(first.Index.Lane(lane.Lane.Id), second.Index.Lane(lane.Lane.Id));
                Assert.Equal(first.Index.FrameTicks, second.Index.FrameTicks);

                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));          // the recording changed
                using var third = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.False(third.FromSidecar);
                Assert.Single(third.Index.Lane(CombatTimelineLanes.Shot.Id));
            }
            finally
            {
                try { File.Delete(TimelineSidecar.PathFor(path)); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-1301</c> R0 ⑧ (§9 R1-c) — scan rate and index size on a REAL recording. Opt-in: set <c>HROT_TIMELINE_RECORDING</c> to an
        /// <c>.fdp</c>; without it the rail only says so (a measurement, not a gate).
        /// </summary>
        [Fact]
        public void CE1301_R0_8_ScanRate_AndIndexSize_OnARealRecording()
        {
            string? path = Environment.GetEnvironmentVariable("HROT_TIMELINE_RECORDING");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { _out.WriteLine("HROT_TIMELINE_RECORDING not set — nothing measured"); return; }
            // The recording's component types must be known to this process (as in the editor): HROT_TIMELINE_ASSEMBLY_DIR names a build
            // output whose Hrot.* / Fdp.* assemblies are loaded first (e.g. Hrot.ClusterRunner's bin).
            string? dir = Environment.GetEnvironmentVariable("HROT_TIMELINE_ASSEMBLY_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? ""));
                foreach (var dll in Directory.GetFiles(dir, "*.dll"))
                {
                    string name = Path.GetFileNameWithoutExtension(dll);
                    if (!(name.StartsWith("Hrot.") || name.StartsWith("Fdp.")) || loaded.Contains(name)) continue;
                    try { System.Reflection.Assembly.LoadFrom(dll); } catch (Exception ex) { _out.WriteLine($"skip {name}: {ex.GetType().Name}"); }
                }
            }
            string copy = Path.Combine(Path.GetTempPath(), $"r0-8-{Guid.NewGuid():N}.fdp");   // the sidecar lands beside the copy, never beside the original
            File.Copy(path, copy);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var index = TimelineIndexBuilder.Build(copy, Lanes, Compiler());
                double scan = sw.Elapsed.TotalSeconds;
                Assert.True(TimelineSidecar.Write(copy, index, Lanes.ToDictionary(l => l.Lane.Id, TimelineSidecar.HashOf)));
                long sidecar = new FileInfo(TimelineSidecar.PathFor(copy)).Length;
                sw.Restart();
                var reread = TimelineSidecar.TryRead(copy);
                double open = sw.Elapsed.TotalMilliseconds;
                Assert.NotNull(reread);
                double recorded = index.FrameTicks.Count > 1 ? TimeSpan.FromTicks(index.FrameTicks[^1] - index.FrameTicks[0]).TotalSeconds : 0;
                _out.WriteLine($"first ticks: {string.Join(", ", index.FrameTicks.Take(3))} … last {index.FrameTicks[^1]}");
                int jump = 1; long gap = 0, backwards = 0;
                for (int f = 1; f < index.FrameTicks.Count; f++)
                {
                    long d = index.FrameTicks[f] - index.FrameTicks[f - 1];
                    if (d > gap) { gap = d; jump = f; }
                    if (d < 0) backwards++;
                }
                _out.WriteLine($"largest step: frame {jump - 1} → {jump} ({index.FrameTicks[jump - 1]} → {index.FrameTicks[jump]}); steps backwards: {backwards}");
                if (index.FrameTicks.Count > jump + 1) recorded = TimeSpan.FromTicks(index.FrameTicks[^1] - index.FrameTicks[jump]).TotalSeconds;   // after the jump
                _out.WriteLine($"recording {new FileInfo(path).Length / 1e6:0.0} MB · {index.TotalFrames} frames · {recorded:0} s recorded");
                _out.WriteLine($"scan {scan:0.0} s = {index.TotalFrames / Math.Max(scan, 1e-6):0} frames/s ({recorded / Math.Max(scan, 1e-6):0.0}× real time)");
                _out.WriteLine($"sidecar {sidecar / 1e3:0.0} kB · reopen {open:0.0} ms");
                foreach (var lane in Lanes) _out.WriteLine($"  {lane.Lane.Id}: {index.Lane(lane.Lane.Id).Count}");
                foreach (var (lane, why) in index.LaneErrors) _out.WriteLine($"  ⚠ {lane}: {why}");
            }
            finally
            {
                try { File.Delete(copy); File.Delete(TimelineSidecar.PathFor(copy)); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void CE1302_AnUnreadableRecording_StopsTheBuildWithAReason_NotAHang()
        {
            string bad = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}.fdp");
            File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            try
            {
                using var source = RecordingTimelineSource.Open(bad, Lanes, Compiler());
                Assert.True(source.Completion.Wait(TimeSpan.FromSeconds(30)));
                Assert.False(source.Completion.IsFaulted);
                Assert.False(source.IsComplete && source.Index.TotalFrames > 0);
                Assert.False(string.IsNullOrEmpty(source.Error));
                Assert.False(File.Exists(TimelineSidecar.PathFor(bad)));
            }
            finally
            {
                try { File.Delete(bad); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// ⭐ <c>R-4079</c> (§9c ④ ⑤) — the cache is per lane: opening with MORE lanes scans only the new ones (the rest come from the sidecar
        /// unchanged), and a lane whose DEFINITION changed is rescanned on its own. The new lane here is a user's (a predicate built in the
        /// existing editor, registered like a shipped one): "who was hit", keyed by the hit entity.
        /// </summary>
        [Fact]
        public void CE1302_ANewOrChangedLane_IsTheOnlyOneScanned_AndAUsersLaneIndexesLikeAShippedOne()
        {
            using var harness = new FdpRecordingHarness();
            string path = Record(harness);
            try
            {
                var combatOnly = new TimelineLaneCatalog().Register(new CombatTimelineLanes()).Indexed(Recording);
                using var first = RecordingTimelineSource.Open(path, combatOnly, Compiler(), background: false);
                Assert.Equal(combatOnly.Select(l => l.Lane.Id), first.ScannedLanes);

                using var all = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.Equal(Lanes.Select(l => l.Lane.Id).Except(combatOnly.Select(l => l.Lane.Id)), all.ScannedLanes);
                Assert.Equal(first.Index.Lane(CombatTimelineLanes.Shot.Id), all.Index.Lane(CombatTimelineLanes.Shot.Id));   // reused, unchanged
                Assert.Single(all.Index.Lane(ReplicationTimelineLanes.Left.Id));

                var mine = new TimelineLaneDescriptor("user.who-was-hit", "Who was hit", Group: "Mine");
                var catalog = Shipped().Register(new IndexedLaneDefinition(mine,
                    new TransientEventPredicateDto { EventType = typeof(HitEvent), AnyOccurrence = true },
                    new ResultProjectionDto { SubjectPath = nameof(HitEvent.HitEntity) }));
                using var withMine = RecordingTimelineSource.Open(path, catalog.Indexed(Recording), Compiler(), background: false);
                Assert.Equal(new[] { mine.Id }, withMine.ScannedLanes);
                Assert.Equal(new[] { (1, Commander, 0L) }, Of(withMine.Index, mine));
                Assert.Contains(mine, catalog.Lanes(Recording));

                var changed = Shipped().Register(new IndexedLaneDefinition(mine,
                    new TransientEventPredicateDto { EventType = typeof(HitEvent), AnyOccurrence = true },
                    new ResultProjectionDto { SubjectPath = nameof(HitEvent.BulletEntity) }));                  // same id, another definition
                using var afterChange = RecordingTimelineSource.Open(path, changed.Indexed(Recording), Compiler(), background: false);
                Assert.Equal(new[] { mine.Id }, afterChange.ScannedLanes);
            }
            finally
            {
                try { File.Delete(TimelineSidecar.PathFor(path)); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void CE1302_ABackgroundBuild_PublishesTheFinalIndex()
        {
            using var harness = new FdpRecordingHarness();
            string path = Record(harness);
            try
            {
                using var source = RecordingTimelineSource.Open(path, Lanes, Compiler());
                Assert.True(source.Completion.Wait(TimeSpan.FromSeconds(30)));
                Assert.True(source.IsComplete);
                Assert.Equal(1f, source.Progress);
                Assert.Single(source.Index.Lane(CombatTimelineLanes.Blast.Id));
            }
            finally
            {
                try { File.Delete(TimelineSidecar.PathFor(path)); } catch { /* best effort */ }
            }
        }
    }
}
