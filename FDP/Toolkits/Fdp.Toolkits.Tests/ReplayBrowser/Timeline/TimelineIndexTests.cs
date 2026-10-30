using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
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
        private static string Record(FdpRecordingHarness harness, Action<long>? afterFrame = null)
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
            harness.Tick().RecordKeyframe(100_000L); afterFrame?.Invoke(100_000L);                                                          // frame 0

            harness.FireUnmanagedEvent(new WeaponFireNotification { Shooter = soldier, Target = commander, Origin = new Vector3(30, 40, 1), Quantity = 3 });
            harness.FireUnmanagedEvent(new WeaponFireNotification { Shooter = soldier, Refusal = FireRefusal.TooFar });
            harness.FireUnmanagedEvent(new HitEvent { HitEntity = commander });
            CommandHistoryOf.Order(repo, commander, soldier, 0x5A11, 1f);
            CommandHistoryOf.Order(repo, commander, soldier, 0x7777, 1f);                                     // a second order the same frame: two events (TE-22 ②)
            harness.Tick().RecordDelta(200_000L); afterFrame?.Invoke(200_000L);                                                             // frame 1

            harness.FireUnmanagedEvent(new DetonationNotification { Shooter = soldier, HitX = 5, HitY = 6, Damage = 0.5f });
            ref var slot = ref repo.GetComponentRW<UtilityDecisionLog>(soldier).SlotsRW()[0];
            slot.DecisionId = 0x2067; slot.SwitchCount = 1; slot.Winner = 7;
            ref var idle = ref repo.GetComponentRW<UtilityDecisionLog>(soldier).SlotsRW()[1];
            idle.DecisionId = 0x3001; idle.SwitchCount = 0; idle.Winner = 9;                                 // a decision that ran but never switched: not news
            CommandHistoryOf.Order(repo, commander, soldier, 0x7777, 2f);                                     // the same order REPEATED: counted, still news
            BrainJournalOf.Write(repo, commander, BrainJournalKind.TaskStarted, point: new Vector3(1, 2, 0));
            harness.Tick().RecordDelta(300_000L); afterFrame?.Invoke(300_000L);                                                             // frame 2

            harness.DestroyEntity(soldier);
            var late = repo.CreateEntity();
            repo.AddComponent(late, new NetworkIdentity(Late));
            harness.Tick().RecordDelta(400_000L); afterFrame?.Invoke(400_000L);                                                             // frame 3
            return harness.BuildToTempFile();
        }

        private const long Unit = 101, Bystander = 102, Carrier = 200;

        /// <summary>
        /// ⭐ <c>CE-4117</c> — six frames of STATE: 0 the unit starts with a task (0x1111), a force (Friend), health 80/100 and a gesture
        /// (IdleStretch), a bystander with only a force (Hostile) · 1 nothing the lanes read changes (a field they do not project does) ·
        /// 2 the task becomes 0x2222 and the unit boards the carrier · 3 force → Hostile, health 30, gesture → IdleLookAbout · 4 it leaves the
        /// carrier, max health 120 · 5 the gesture component is removed.
        /// </summary>
        private static string RecordStates(FdpRecordingHarness harness, Action<long>? afterFrame = null)
        {
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<BehaviorState>();
            repo.RegisterComponent<EntityInfo>();
            repo.RegisterComponent<Health>();
            repo.RegisterComponent<IsEmbarkedTag>();
            repo.RegisterComponent<Gesture>();

            var carrier = repo.CreateEntity();
            repo.AddComponent(carrier, new NetworkIdentity(Carrier));
            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new NetworkIdentity(Unit));
            repo.AddComponent(unit, new BehaviorState { ActiveBehaviorHash = 0x1111, InstanceId = 1 });
            repo.AddComponent(unit, new EntityInfo { ForceId = ForceId.Friend });
            repo.AddComponent(unit, new Health { Current = 80, Max = 100 });
            repo.AddComponent(unit, new Gesture { Kind = GestureKind.IdleStretch });
            var bystander = repo.CreateEntity();
            repo.AddComponent(bystander, new NetworkIdentity(Bystander));
            repo.AddComponent(bystander, new EntityInfo { ForceId = ForceId.Hostile });
            harness.Tick().RecordKeyframe(100_000L); afterFrame?.Invoke(100_000L);                                    // frame 0

            repo.GetComponentRW<BehaviorState>(unit).InstanceId = 2;                                                  // not projected: no entry
            harness.Tick().RecordDelta(200_000L); afterFrame?.Invoke(200_000L);                                       // frame 1

            repo.GetComponentRW<BehaviorState>(unit).ActiveBehaviorHash = 0x2222;
            repo.AddComponent(unit, new IsEmbarkedTag { VehicleEntity = carrier });
            harness.Tick().RecordDelta(300_000L); afterFrame?.Invoke(300_000L);                                       // frame 2

            repo.GetComponentRW<EntityInfo>(unit).ForceId = ForceId.Hostile;
            repo.GetComponentRW<Health>(unit).Current = 30;
            repo.GetComponentRW<Gesture>(unit).Kind = GestureKind.IdleLookAbout;
            harness.Tick().RecordDelta(400_000L); afterFrame?.Invoke(400_000L);                                       // frame 3

            repo.RemoveComponent<IsEmbarkedTag>(unit);
            repo.GetComponentRW<Health>(unit).Max = 120;
            harness.Tick().RecordDelta(500_000L); afterFrame?.Invoke(500_000L);                                       // frame 4

            repo.RemoveComponent<Gesture>(unit);
            harness.Tick().RecordDelta(600_000L); afterFrame?.Invoke(600_000L);                                       // frame 5
            return harness.BuildToTempFile();
        }

        private static List<(int Frame, long Subject, long Other, float X, float Value)> Of(TimelineIndex index, string lane)
            => index.Lane(lane).OrderBy(e => e.Frame).ThenBy(e => e.Subject).Select(e => (e.Frame, e.Subject, e.Other, e.X, e.Value)).ToList();

        /// <summary>
        /// ⭐ <c>CE-4117</c> — each STATE lane (<c>state.task / side / health / embark / gesture</c>, value-change lanes declared as data by the
        /// provider that owns the component) holds EXACTLY the changes of the scripted world, at their frames, with their values; the first
        /// seen state of every unit is an entry at frame 0; an unchanged frame (and a change of a field the lane does not project) adds nothing;
        /// a component removed while the unit lives is one entry with Value 0 (embark: the tag, gesture: absent = None).
        /// 🔴 Red-proofs (measured): the <c>Task</c> definition removed from <c>BehaviourTimelineLanes.Definitions</c> ⇒ the task list is empty;
        /// the lane skipping the first-seen frame ⇒ every frame-0 entry missing.
        /// </summary>
        [Fact]
        public void CE4117_EachStateLane_HoldsExactlyItsChanges_AtTheirFrames_WithTheirValues_AndFrameZero()
        {
            using var harness = new FdpRecordingHarness();
            string path = RecordStates(harness);
            var index = TimelineIndexBuilder.Build(path, Lanes, Compiler());

            Assert.Equal(new[] { (0, Unit, 0x1111L, 0f, (float)0x1111), (2, Unit, 0x2222L, 0f, (float)0x2222) }, Of(index, BehaviourTimelineLanes.Task.Id));
            Assert.Equal(new[] { (0, Unit, 0L, 0f, (float)ForceId.Friend), (0, Bystander, 0L, 0f, (float)ForceId.Hostile), (3, Unit, 0L, 0f, (float)ForceId.Hostile) },
                         Of(index, ReplicationTimelineLanes.Side.Id));
            Assert.Equal(new[] { (0, Unit, 0L, 100f, 80f), (3, Unit, 0L, 100f, 30f), (4, Unit, 0L, 120f, 30f) }, Of(index, CombatTimelineLanes.HealthState.Id));
            Assert.Equal(new[] { (2, Unit, Carrier, 0f, 1f), (4, Unit, 0L, 0f, 0f) }, Of(index, BehaviourTimelineLanes.Embark.Id));
            Assert.Equal(new[] { (0, Unit, 0L, 0f, (float)GestureKind.IdleStretch), (3, Unit, 0L, 0f, (float)GestureKind.IdleLookAbout), (5, Unit, 0L, 0f, 0f) },
                         Of(index, CombatTimelineLanes.GestureState.Id));
        }

        /// <summary>
        /// ⭐ <c>CE-4117</c> — the value-change lane reads a component only when its table's chunk version moved, and still sees every write of the
        /// normal path (<c>GetComponentRW</c>) when OTHER entities in the same chunk are unchanged: three units share a chunk; frame 1 writes only
        /// B, frame 2 only A, frame 3 nothing — the lane holds exactly (0: A, B, C), (1: B), (2: A) and the quiet frame adds nothing.
        /// (The CE4117 test above covers the same path too: frame 3 writes the unit's health while the bystander's chunk-mate is unchanged.)
        /// 🔴 Red-proof: the version compare made always-equal ⇒ every change after frame 0 is missing.
        /// </summary>
        [Fact]
        public void CE4117_AWriteByTheNormalPath_IsSeen_AmongUnchangedNeighboursInTheChunk_AndAQuietFrameAddsNothing()
        {
            using var harness = new FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<Health>();
            Entity[] units = new Entity[3];
            for (int k = 0; k < 3; k++)
            {
                units[k] = repo.CreateEntity();
                repo.AddComponent(units[k], new NetworkIdentity(301 + k));
                repo.AddComponent(units[k], new Health { Current = 10, Max = 10 });
            }
            harness.Tick().RecordKeyframe(100_000L);
            repo.GetComponentRW<Health>(units[1]).Current = 5;
            harness.Tick().RecordDelta(200_000L);
            repo.GetComponentRW<Health>(units[0]).Current = 7;
            harness.Tick().RecordDelta(300_000L);
            harness.Tick().RecordDelta(400_000L);
            var index = TimelineIndexBuilder.Build(harness.BuildToTempFile(), Lanes, Compiler());

            Assert.Equal(new[] { (0, 301L, 10f), (0, 302L, 10f), (0, 303L, 10f), (1, 302L, 5f), (2, 301L, 7f) },
                         Of(index, CombatTimelineLanes.HealthState.Id).Select(e => (e.Frame, e.Subject, e.Value)).ToArray());
        }

        /// <summary>⭐ <c>CE-4117</c> — the state lanes are LIVE = RECORDED too (CE-3330): the same run indexed frame by frame and rebuilt from its
        /// recording gives the same entries in every state lane (a lane the catalog holds that the scripted world never feeds would pass vacuously,
        /// so the lanes are asserted non-empty).</summary>
        [Fact]
        public void CE4117_TheStateLanes_LiveEqualRebuilt()
        {
            using var harness = new FdpRecordingHarness();
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            string path = RecordStates(harness, ticks => live.Step(harness.Repository, ticks, force: true));
            var rebuilt = TimelineIndexBuilder.Build(path, Lanes, Compiler());
            foreach (var id in new[] { "state.task", "state.side", "state.health", "state.embark", "state.gesture" })
            {
                Assert.NotEmpty(rebuilt.Lane(id));
                Assert.Equal(Of(rebuilt, id), Of(live.Index, id));
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3330</c> (R-4087 addendum 6; DESIGN_Director_Timeline_Editor.md §2c rule 7) — LIVE LANES = RECORDED LANES: the same run, indexed
        /// live frame by frame (<see cref="LiveTimelineSource"/>, the world as it ticks — its recorder clearing the destruction log inside the
        /// frame, as a live host's does) and rebuilt from its recording, gives the same events in every lane and the same frame table.
        /// 🔴 Red-proof: the structural scan's alive-sweep removed ⇒ the live index misses the soldier's death (the recorder cleared the log).
        /// </summary>
        [Fact]
        public void CE3330_ALiveIndex_EqualsTheIndexRebuiltFromItsRecording_LaneByLane()
        {
            using var harness = new FdpRecordingHarness();
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            // CE-3330 E1 - the world carries a sim clock (the recorder stamps it into the v7 frame index): 0.5 s, 1.0 s, 1.5 s, 2.0 s
            int n = 1;
            harness.Repository.SetSingleton(new GlobalTime { FrameNumber = n, TotalTime = n * 0.5 });
            string path = Record(harness, ticks =>
            {
                live.Step(harness.Repository, ticks, force: true);
                n++;
                harness.Repository.SetSingleton(new GlobalTime { FrameNumber = n, TotalTime = n * 0.5 });   // the NEXT frame's clock
            });
            var rebuilt = TimelineIndexBuilder.Build(path, Lanes, Compiler());

            Assert.Equal(rebuilt.FrameTicks, live.Index.FrameTicks);
            Assert.Equal(new[] { 0.5, 1.0, 1.5, 2.0 }, live.Index.FrameSim.ToArray());
            Assert.Equal(live.Index.FrameSim, rebuilt.FrameSim);   // E1-c: the recording's v7 sim time = what the live index stamped
            Assert.Equal(4, live.Frames);
            foreach (var lane in Shipped().Lanes(new TimelineLaneContext(Live: true)))
                Assert.Equal(rebuilt.Lane(lane.Id).OrderBy(e => e.Frame).ThenBy(e => e.Subject), live.Index.Lane(lane.Id).OrderBy(e => e.Frame).ThenBy(e => e.Subject));
            Assert.Single(live.Index.Lane(ReplicationTimelineLanes.Left.Id));   // the death — though the recorder cleared the log first
        }

        /// <summary>⭐ <c>CE-3330</c> — the live source starts afresh for a NEW run (another world, or its frame counter going back), adds nothing
        /// while the world's frame does not advance (paused), and publishes its snapshot at most every quarter second.</summary>
        [Fact]
        public void CE3330_TheLiveSource_ANewRunResets_APauseAddsNothing_SnapshotsAreThrottled()
        {
            var catalog = Shipped();
            var live = new LiveTimelineSource(catalog, new TimelineLaneContext(Live: true), Compiler());
            using var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.SetSingleton(new GlobalTime { FrameNumber = 1 });
            var unit = world.CreateEntity();
            world.AddComponent(unit, new NetworkIdentity(Commander));

            live.Step(world, 1_000, force: true);
            Assert.Equal(1, live.Frames);
            Assert.Single(live.Index.Lane(ReplicationTimelineLanes.Spawned.Id));
            live.Step(world, 2_000);                                        // the same sim frame (paused): nothing
            Assert.Equal(1, live.Frames);

            world.SetSingleton(new GlobalTime { FrameNumber = 2 });
            live.Step(world, 1_000 + LiveTimelineSource.PublishTicks / 2);  // indexed, not yet published
            Assert.Equal(2, live.Frames);
            Assert.Equal(1, live.Index.FrameTicks.Count);
            world.SetSingleton(new GlobalTime { FrameNumber = 3 });
            live.Step(world, 1_000 + LiveTimelineSource.PublishTicks);      // a quarter second on: published
            Assert.Equal(3, live.Index.FrameTicks.Count);

            var fresh = new LiveTimelineSource(catalog, new TimelineLaneContext(Live: true), Compiler());
            fresh.Step(world, DateTime.UtcNow.Ticks);                       // ⭐ never forced, real wall ticks: the first frame publishes
            Assert.Equal(1, fresh.Index.FrameTicks.Count);                  // (⛔ measured live: an overflow kept a whole run unpublished)

            world.SetSingleton(new GlobalTime { FrameNumber = 1 });         // rewound — a new run
            live.Step(world, 9_000, force: true);
            Assert.Equal(1, live.Frames);
        }

        /// <summary>
        /// ⭐ <c>CE-3330</c> E1 (§2e) — the live index's clock is the world's own: a frame is stamped with <see cref="GlobalTime.TotalWallTicks"/>
        /// (the frame-locked wall the flight recorder writes) when the world has one, and the passed wall ticks only as the FALLBACK (a world without
        /// a clock, or a zero <c>TotalWallTicks</c>). The publish throttle runs off the same value.
        /// 🔴 Red-proof: the GlobalTime wall ignored (behind a runtime condition) ⇒ the frame ticks are the passed values.
        /// </summary>
        [Fact]
        public void CE3330_TheLiveSource_StampsFramesWithTheWorldsFrameLockedWallClock_AndFallsBackToThePassedTicks()
        {
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            using var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();

            world.SetSingleton(new GlobalTime { FrameNumber = 1, TotalWallTicks = 5_000 });
            live.Step(world, 1_000, force: true);                                   // passed 1 000, the world says 5 000
            world.SetSingleton(new GlobalTime { FrameNumber = 2, TotalWallTicks = 7_000 });
            live.Step(world, 2_000, force: true);
            world.SetSingleton(new GlobalTime { FrameNumber = 3 });                 // no wall clock on this frame: the passed ticks
            live.Step(world, 9_000, force: true);

            Assert.Equal(new long[] { 5_000, 7_000, 9_000 }, live.Index.FrameTicks.ToArray());

            // the throttle works off the world's clock too: 5 000 -> +PublishTicks/2 is not published, +PublishTicks is
            var throttled = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            world.SetSingleton(new GlobalTime { FrameNumber = 1, TotalWallTicks = 5_000 });
            throttled.Step(world, 0);
            world.SetSingleton(new GlobalTime { FrameNumber = 2, TotalWallTicks = 5_000 + LiveTimelineSource.PublishTicks / 2 });
            throttled.Step(world, 0);
            Assert.Equal(1, throttled.Index.FrameTicks.Count);
            world.SetSingleton(new GlobalTime { FrameNumber = 3, TotalWallTicks = 5_000 + LiveTimelineSource.PublishTicks });
            throttled.Step(world, 0);
            Assert.Equal(3, throttled.Index.FrameTicks.Count);
        }

        /// <summary>
        /// ⭐ <c>CE-3330</c> E1 (§2e) — the live index records each frame's SIM time (<see cref="GlobalTime.TotalTime"/>) in <c>FrameSim</c>, and the
        /// column is SHARED like the rest (CE-1361 ②): a snapshot taken earlier keeps its count and values while hundreds more frames grow the
        /// buffer. A world with no <see cref="GlobalTime"/> records none (<c>FrameSim</c> empty).
        /// 🔴 Red-proofs: the sim argument dropped (behind a runtime condition) ⇒ FrameSim empty; the column snapshot sharing the buffer's full length ⇒ the old one grows.
        /// </summary>
        [Fact]
        public void CE3330_TheLiveSource_RecordsEachFramesSimTime_AndAnOldSnapshotKeepsItsColumn()
        {
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            using var world = new EntityRepository();
            void Frame(int n)
            {
                world.SetSingleton(new GlobalTime { FrameNumber = n, TotalTime = n * 0.25 });
                live.Step(world, 1_000 + n * 10L, force: true);
            }
            for (int n = 1; n <= 5; n++) Frame(n);
            var old = live.Index;
            Assert.Equal(new[] { 0.25, 0.5, 0.75, 1.0, 1.25 }, old.FrameSim.ToArray());

            for (int n = 6; n <= 700; n++) Frame(n);                                // several buffer growths
            var now = live.Index;

            Assert.Equal(5, old.FrameSim.Count);                                    // the old snapshot did not grow...
            Assert.Equal(new[] { 0.25, 0.5, 0.75, 1.0, 1.25 }, old.FrameSim.ToArray());   // ...nor change
            Assert.Equal(700, now.FrameSim.Count);
            Assert.Equal(700 * 0.25, now.FrameSim[699]);
            Assert.Throws<ArgumentOutOfRangeException>(() => old.FrameSim[5]);

            var bare = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            using var noClock = new EntityRepository();
            bare.Step(noClock, 1_000, force: true);
            Assert.Equal(1, bare.Index.FrameTicks.Count);
            Assert.Empty(bare.Index.FrameSim);                                      // nothing recorded a sim time
        }

        /// <summary>
        /// ⭐ <c>CE-3330</c> — ONE shipped catalogue: the editor, the Replay Browser and every ECS node compose their lanes from
        /// <see cref="ShippedTimelineLanes.Catalog"/>; it registers the three toolkits' providers in the order the hosts used to (Replication,
        /// Combat, Behaviour), and each call is a NEW catalogue (a user's lane registered into one host's never leaks into another's).
        /// 🔴 Red-proof: the Combat provider skipped (behind a runtime-false guard) ⇒ the lane ids lose the combat group.
        /// </summary>
        [Fact]
        public void CE3330_Node_TheShippedCatalog_IsTheThreeToolkitProviders_InOrder_AndNewEachCall()
        {
            var ctx = new TimelineLaneContext(Live: true);
            var shipped = ShippedTimelineLanes.Catalog();

            Assert.Equal(Shipped().Lanes(ctx).Select(l => l.Id), shipped.Lanes(ctx).Select(l => l.Id));
            Assert.Equal(14, shipped.Lanes(ctx).Count);                    // the three toolkits' nine + the five hidden state lanes (CE-4117)
            Assert.Contains(CombatTimelineLanes.Shot.Id, shipped.Lanes(ctx).Select(l => l.Id));
            Assert.NotSame(shipped, ShippedTimelineLanes.Catalog());
            shipped.Register(new IndexedLaneDefinition(new TimelineLaneDescriptor("mine", "Mine"),
                new TransientEventPredicateDto { EventType = typeof(HitEvent), AnyOccurrence = true }));
            Assert.Null(ShippedTimelineLanes.Catalog().Find("mine"));
        }

        /// <summary>
        /// ⭐ <c>CE-1361</c> ② — a published snapshot is IMMUTABLE and publishing is O(1) in the events indexed: after a snapshot, hundreds more
        /// frames are appended (the lane and the frame table outgrow their buffers several times, each growth a copy), and the OLD snapshot still
        /// holds exactly its own events and frame ticks — same count, same values — while the NEW one holds old + new.
        /// 🔴 Red-proofs: a snapshot that reports its buffer's capacity instead of its count (compiles) ⇒ the old snapshot grows;
        /// a publish that shares the buffer's full length ⇒ the same.
        /// </summary>
        [Fact]
        public void CE3330_Node_AnOldSnapshotIsUnchanged_WhileTheLiveIndexGrowsPastSeveralBufferGrowths()
        {
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            using var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            long next = 1;
            void Frame(int n)
            {
                world.SetSingleton(new GlobalTime { FrameNumber = n });
                var e = world.CreateEntity();
                world.AddComponent(e, new NetworkIdentity(next++));
                live.Step(world, 1_000 + n * 10L, force: true);
            }
            for (int n = 1; n <= 5; n++) Frame(n);
            var old = live.Index;
            var oldEvents = old.Lane(ReplicationTimelineLanes.Spawned.Id).ToArray();
            var oldTicks = old.FrameTicks.ToArray();
            Assert.Equal(5, oldTicks.Length);
            Assert.NotEmpty(oldEvents);

            for (int n = 6; n <= 700; n++) Frame(n);                         // 16 → 32 → … → 1024: six growths of each buffer
            var now = live.Index;

            Assert.Equal(oldEvents.Length, old.Lane(ReplicationTimelineLanes.Spawned.Id).Count);       // the old snapshot did not grow...
            Assert.Equal(oldEvents, old.Lane(ReplicationTimelineLanes.Spawned.Id).ToArray());           // ...nor change
            Assert.Equal(oldTicks, old.FrameTicks.ToArray());
            Assert.Equal(oldEvents.Length, old.Count(ReplicationTimelineLanes.Spawned.Id, 0, int.MaxValue));
            Assert.Equal(700, now.FrameTicks.Count);
            Assert.True(now.Lane(ReplicationTimelineLanes.Spawned.Id).Count > oldEvents.Length + 600);
            Assert.Equal(oldEvents, now.Lane(ReplicationTimelineLanes.Spawned.Id).Take(oldEvents.Length).ToArray());   // the new one holds old + new
            Assert.Equal(oldTicks, now.FrameTicks.Take(oldTicks.Length).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => old.FrameTicks[oldTicks.Length]);          // a reader cannot reach past its snapshot
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

        // ───────────────────────── CE-4059 — the position track ─────────────────────────

        private const long Mover = 201, Leaver = 202, Arriver = 203;

        /// <summary>
        /// ⭐ <c>CE-4059</c> — nine frames of a sim clock at 0.5 + 0.25·n s (so a 0.5 s sample interval lands on every SECOND frame: 0 2 4 6 8):
        /// the <see cref="Mover"/> runs east 10 m a frame (heading 0.1·n rad), the <see cref="Leaver"/> stands at (100, 100) and is destroyed
        /// before frame 4, the <see cref="Arriver"/> appears at frame 6. <paramref name="afterFrame"/> runs after each frame is recorded.
        /// </summary>
        private static string RecordMovers(FdpRecordingHarness harness, Action<long>? afterFrame = null)
        {
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<SimTransform>();
            var mover = repo.CreateEntity();
            repo.AddComponent(mover, new NetworkIdentity(Mover));
            repo.AddComponent(mover, default(SimTransform));
            var leaver = repo.CreateEntity();
            repo.AddComponent(leaver, new NetworkIdentity(Leaver));
            repo.AddComponent(leaver, new SimTransform { Position = new Vector3(100, 100, 0), Rotation = Quaternion.Identity });
            for (int n = 0; n < 9; n++)
            {
                harness.MutateComponent<SimTransform>(mover, _ => new SimTransform
                {
                    Position = new Vector3(10 * n, 5, 1),
                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.1f * n),
                });
                if (n == 4) harness.DestroyEntity(leaver);
                if (n == 6)
                {
                    var arriver = repo.CreateEntity();
                    repo.AddComponent(arriver, new NetworkIdentity(Arriver));
                    repo.AddComponent(arriver, new SimTransform { Position = new Vector3(0, 50, 0), Rotation = Quaternion.Identity });
                }
                repo.SetSingleton(new GlobalTime { FrameNumber = n + 1, TotalTime = 0.5 + 0.25 * n });
                long ticks = 100_000L * (n + 1);
                harness.Tick();
                if (n == 0) harness.RecordKeyframe(ticks); else harness.RecordDelta(ticks);
                afterFrame?.Invoke(ticks);
            }
            return harness.BuildToTempFile();
        }

        private static int[] FramesOf(PositionTrack track, long unit) => track.Samples(unit).Select(s => s.Frame).ToArray();

        /// <summary>
        /// ⭐ <c>CE-4059</c> — the position track of a recording: one sample per networked unit every 0.5 s of SIM time (the frames of a 0.25 s clock
        /// that start a new bucket), carrying the unit's <see cref="SimTransform"/> place; <c>PositionAt</c> interpolates linearly between two
        /// samples and answers nothing outside a unit's span; a unit that is destroyed is not sampled again, one that arrives starts late;
        /// <c>PositionsOf</c> slices a frame window; <c>UnitsWithin</c> finds who is near a point at a frame.
        /// 🔴 Red-proofs (each compiles): the bucket check never skipping (`&amp;&amp; DateTime.UtcNow.Ticks &lt; 0`) ⇒ a sample every frame;
        /// the interpolation fraction forced to 0 ⇒ frame 3 reads x=20; the writer re-adding a departed unit's last sample ⇒ the Leaver's frames grow.
        /// </summary>
        [Fact]
        public void CE4059_TheTrack_SamplesEachUnitAtTheRate_InterpolatesBetweenSamples_AndStopsForAUnitThatLeft()
        {
            using var harness = new FdpRecordingHarness();
            string path = RecordMovers(harness);
            var index = TimelineIndexBuilder.Build(path, Lanes, Compiler());
            var track = index.Positions;

            Assert.Equal(new[] { 0, 2, 4, 6, 8 }, FramesOf(track, Mover));                        // the rate: every second frame (0.5 s of sim)
            Assert.Equal(new[] { 0, 2 }, FramesOf(track, Leaver));                                 // destroyed before frame 4: never sampled again
            Assert.Equal(new[] { 6, 8 }, FramesOf(track, Arriver));                                // arrived at frame 6
            Assert.Equal(3, track.UnitCount);
            foreach (var s in track.Samples(Mover))                                                // the right positions
            {
                Assert.Equal(10f * s.Frame, s.X); Assert.Equal(5f, s.Y); Assert.Equal(1f, s.Z);
                Assert.Equal(0.1f * s.Frame, s.Heading, 4);
            }
            Assert.Equal((100f, 100f), (track.Samples(Leaver)[1].X, track.Samples(Leaver)[1].Y));

            var at3 = index.PositionAt(Mover, 3)!.Value;                                            // between the samples of frames 2 and 4
            Assert.Equal(30f, at3.X, 3); Assert.Equal(5f, at3.Y, 3); Assert.Equal(0.3f, at3.Heading, 4); Assert.Equal(3, at3.Frame);
            Assert.Equal(40f, index.PositionAt(Mover, 4)!.Value.X);                                 // on a sample: the sample
            Assert.Null(index.PositionAt(Leaver, 4));                                               // after its last sample: it is gone, not extrapolated
            Assert.Null(index.PositionAt(Arriver, 5));                                              // before its first
            Assert.Null(index.PositionAt(999, 3));                                                  // never seen
            Assert.Equal(new[] { 2, 4, 6 }, index.PositionsOf(Mover, 2, 7).ToArray().Select(s => s.Frame).ToArray());   // [from, to)
            Assert.True(index.PositionsOf(Mover, 100, 200).IsEmpty);

            var near = new List<long>();
            track.UnitsWithin(3, 30, 5, 2, near);                                                   // the mover is at (30, 5) at frame 3
            Assert.Equal(new[] { Mover }, near.ToArray());
            near.Clear();
            track.UnitsWithin(1, 100, 100, 1, near);                                                // the leaver stood there until it left
            Assert.Equal(new[] { Leaver }, near.ToArray());
            near.Clear();
            track.UnitsWithin(7, 0, 0, 1000, near);                                                 // mover + arriver (ascending); the leaver is gone
            Assert.Equal(new[] { Mover, Arriver }, near.ToArray());
        }

        /// <summary>⭐ <c>CE-4059</c> — a heading interpolates along the SHORTER arc (π → −π is a hair, not a full turn).
        /// 🔴 Red-proof: the arc normalisation skipped (`DateTime.UtcNow.Ticks &gt; 0`) ⇒ the midpoint reads 0.</summary>
        [Fact]
        public void CE4059_AHeading_InterpolatesAlongTheShorterArc()
        {
            var track = new PositionTrack(new Dictionary<long, PositionSample[]>
            {
                [1] = new[] { new PositionSample(0, 0, 0, 0, 3.0f), new PositionSample(2, 0, 0, 0, -3.0f) },
            });
            float mid = track.At(1, 1)!.Value.Heading;
            Assert.True(Math.Abs(Math.Abs(mid) - MathF.PI) < 0.2f, $"heading at the midpoint {mid}");   // near ±π, not 0
        }

        /// <summary>
        /// ⭐ <c>CE-4059</c> (R-4089: ONE per-frame step) — the live index and the index rebuilt from its recording carry the SAME position samples,
        /// unit by unit and in the same order: the track is sampled by <see cref="TimelineFrameScanner"/>, which both run.
        /// 🔴 Red-proof: the live path's sim time shifted (`clearDestructionLog ? sim : sim + 0.3` in the scanner's sample call) ⇒ other buckets.
        /// </summary>
        [Fact]
        public void CE4059_ALiveIndex_CarriesTheSamePositionSamples_AsTheIndexRebuiltFromItsRecording()
        {
            using var harness = new FdpRecordingHarness();
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            string path = RecordMovers(harness, ticks => live.Step(harness.Repository, ticks, force: true));
            var rebuilt = TimelineIndexBuilder.Build(path, Lanes, Compiler());

            Assert.Equal(9, live.Frames);
            Assert.Equal(3, live.Index.Positions.UnitCount);
            Assert.Equal(rebuilt.Positions.UnitIds.OrderBy(i => i), live.Index.Positions.UnitIds.OrderBy(i => i));
            foreach (var unit in rebuilt.Positions.UnitIds)
                Assert.Equal(rebuilt.Positions.Samples(unit).ToArray(), live.Index.Positions.Samples(unit).ToArray());
        }

        /// <summary>
        /// ⭐ <c>CE-4059</c> — the track shares its buffers like the lanes (CE-1361): a snapshot taken early keeps its samples while hundreds more
        /// frames grow the per-unit buffers (several doublings); a publish between sample rounds hands out the SAME track object (no copy).
        /// 🔴 Red-proof: the buffer's snapshot reporting its capacity instead of its count (`DateTime.UtcNow.Ticks &gt; 0 ? _items.Length : Count`) ⇒ the old one grows.
        /// </summary>
        [Fact]
        public void CE4059_AnOldSnapshotOfTheTrack_IsUnchanged_WhileTheLiveIndexKeepsSampling()
        {
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            using var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<SimTransform>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new NetworkIdentity(7));
            world.AddComponent(unit, new SimTransform { Rotation = Quaternion.Identity });
            void Frame(int n)
            {
                world.SetComponent(unit, new SimTransform { Position = new Vector3(n, 0, 0), Rotation = Quaternion.Identity });
                world.SetSingleton(new GlobalTime { FrameNumber = n, TotalTime = n * 0.5 });   // one sample a frame
                live.Step(world, 1_000 + n * 10L, force: true);
            }
            for (int n = 1; n <= 5; n++) Frame(n);
            var old = live.Index.Positions;
            var oldSamples = old.Samples(7).ToArray();
            Assert.Equal(5, oldSamples.Length);

            for (int n = 6; n <= 700; n++) Frame(n);                                              // 16 → 32 → … → 1024
            var now = live.Index.Positions;

            Assert.Equal(5, old.Samples(7).Count);                                                // the old snapshot did not grow...
            Assert.Equal(oldSamples, old.Samples(7).ToArray());                                   // ...nor change
            Assert.Null(old.At(7, 6));                                                            // it knows nothing of the later frames
            Assert.Equal(700, now.Samples(7).Count);
            Assert.Equal(oldSamples, now.Samples(7).Take(5).ToArray());                           // the new one holds old + new
            Assert.Throws<ArgumentOutOfRangeException>(() => old.Samples(7)[5]);

            // two forced publishes in the SAME sample interval: nothing was sampled between them, so the track object is reused
            world.SetSingleton(new GlobalTime { FrameNumber = 701, TotalTime = 700 * 0.5 + 0.1 });
            live.Step(world, 99_000, force: true);
            var a = live.Index.Positions;
            world.SetSingleton(new GlobalTime { FrameNumber = 702, TotalTime = 700 * 0.5 + 0.2 });
            live.Step(world, 99_010, force: true);
            Assert.Same(a, live.Index.Positions);
        }

        /// <summary>⭐ <c>CE-4059</c> — a world with no sim clock (a v6 recording, a bare host) is sampled every
        /// <see cref="PositionTrackWriter.FallbackFrameStride"/> frames instead. 🔴 Red-proof: the stride forced to 1 (`DateTime.UtcNow.Ticks &gt; 0`) ⇒ every frame.</summary>
        [Fact]
        public void CE4059_WithoutASimClock_TheTrackSamplesEveryFallbackStride()
        {
            var live = new LiveTimelineSource(Shipped(), new TimelineLaneContext(Live: true), Compiler());
            using var world = new EntityRepository();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<SimTransform>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new NetworkIdentity(9));
            world.AddComponent(unit, new SimTransform { Rotation = Quaternion.Identity });
            for (int n = 0; n < 31; n++) live.Step(world, 1_000 + n * 10L, force: true);          // no GlobalTime: the source counts the frames itself
            Assert.Equal(new[] { 0, 15, 30 }, FramesOf(live.Index.Positions, 9));
        }

        /// <summary>
        /// ⭐ <c>CE-4059</c> — the sidecar keeps the track: a second open reads it back (no pass) sample for sample; a sidecar of the OLD schema
        /// (the version int patched to 2) is not reused and the open rebuilds the track.
        /// 🔴 Red-proofs: the writer's unit list cleared (`DateTime.UtcNow.Ticks &gt; 0`) ⇒ the round trip is empty; the schema check bypassed ⇒ the patched file is reused.
        /// </summary>
        [Fact]
        public void CE4059_TheSidecar_RoundTripsTheTrack_AndAnOldSchemaSidecarRebuilds()
        {
            using var harness = new FdpRecordingHarness();
            string path = RecordMovers(harness);
            try
            {
                using var first = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.False(first.FromSidecar);
                Assert.Equal(3, first.Index.Positions.UnitCount);

                using var second = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.True(second.FromSidecar);
                foreach (var unit in first.Index.Positions.UnitIds)
                    Assert.Equal(first.Index.Positions.Samples(unit).ToArray(), second.Index.Positions.Samples(unit).ToArray());
                Assert.Equal(first.Index.Positions.UnitCount, second.Index.Positions.UnitCount);

                string sidecar = TimelineSidecar.PathFor(path);
                var bytes = File.ReadAllBytes(sidecar);
                BitConverter.GetBytes(2).CopyTo(bytes, 4);                                         // magic (4 bytes), then the schema version
                File.WriteAllBytes(sidecar, bytes);
                using var third = RecordingTimelineSource.Open(path, Lanes, Compiler(), background: false);
                Assert.False(third.FromSidecar);                                                   // an old schema is not reused...
                Assert.Equal(first.Index.Positions.Samples(Mover).ToArray(), third.Index.Positions.Samples(Mover).ToArray());   // ...and the rebuild has the track
            }
            finally
            {
                try { File.Delete(TimelineSidecar.PathFor(path)); } catch { /* best effort */ }
            }
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

        /// <summary>
        /// ⭐ <c>CE-3330</c> E6 — the ONE reopen rule (<see cref="RecordingTimelineSlot"/>, shared by the Replay Browser and every ECS node in a
        /// cluster replay): a new path reopens and disposes the old source; the same path and catalogue version keeps the SAME source; a catalogue
        /// change (a lane registered) reopens; <c>Sync(null)</c> leaves no source and disposes the old one.
        /// 🔴 Red-proofs (each compiles): the keep-check ANDed with <c>DateTime.UtcNow.Ticks &lt; 0</c> (always reopen) ⇒ <c>Assert.Same</c> fails; the version
        /// compare ORed with <c>DateTime.UtcNow.Ticks &gt; 0</c> (ignored) ⇒ the catalogue <c>Assert.NotSame</c> fails; <c>_source?.Dispose()</c> behind
        /// <c>DateTime.UtcNow.Ticks &lt; 0</c> ⇒ the <c>IsDisposed</c> assert fails.
        /// </summary>
        [Fact]
        public void CE3330_E6_TheRecordingSlot_ReopensOnANewPathOrCatalogue_KeepsOtherwise_AndDisposesTheOld()
        {
            using var harnessA = new FdpRecordingHarness();
            using var harnessB = new FdpRecordingHarness();
            string a = Record(harnessA), b = Record(harnessB);
            try
            {
                var catalog = Shipped();
                using var slot = new RecordingTimelineSlot(catalog, Recording, Compiler, background: false);
                Assert.Null(slot.Source);

                slot.Sync(a);
                var first = slot.Source!;
                Assert.Equal(a, first.Path);
                Assert.True(first.IsComplete);

                slot.Sync(a);                                                       // same path, same catalogue version
                Assert.Same(first, slot.Source);
                Assert.False(first.IsDisposed);

                slot.Sync(b);                                                       // a new path
                var second = slot.Source!;
                Assert.NotSame(first, second);
                Assert.Equal(b, second.Path);
                Assert.True(first.IsDisposed);
                Assert.False(second.IsDisposed);

                catalog.Register(new IndexedLaneDefinition(new TimelineLaneDescriptor("user.e6", "E6", Group: "Mine"),
                    new TransientEventPredicateDto { EventType = typeof(HitEvent), AnyOccurrence = true },
                    new ResultProjectionDto { SubjectPath = nameof(HitEvent.HitEntity) }));
                slot.Sync(b);                                                       // the catalogue changed
                var third = slot.Source!;
                Assert.NotSame(second, third);
                Assert.True(second.IsDisposed);
                Assert.Equal(new[] { "user.e6" }, third.ScannedLanes);              // the new lane is the only one scanned

                slot.Sync(null);
                Assert.Null(slot.Source);
                Assert.True(third.IsDisposed);
            }
            finally
            {
                foreach (var p in new[] { a, b }) { try { File.Delete(TimelineSidecar.PathFor(p)); } catch { /* best effort */ } }
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
