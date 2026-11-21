using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkits.Tests.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ W-1 / P-8 8c (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8 E4, §5.12) — the ONE Tasks lane: a unit's plan moving is an entry (Value = the
    /// current task, X = how the latest ended — the read-only <see cref="MissionPlanQueue.LatestOutcome"/>, Y = the task count), read as markers
    /// (started · Done · Failed, naming the unit and the task) and, for the selection, as bars. ⭐ A thousand units stay ONE lane.
    /// </summary>
    public sealed class TheTasksLaneTests
    {
        private static TimelineLaneCompiler Compiler() => new(new EventScannerCompiler(new ComponentEditServiceBuilder().Build()));

        private static void Set(EntityRepository repo, Entity e, int current, int count, int endedIndex = -1, MissionPhaseOutcome ended = MissionPhaseOutcome.None, bool halted = false)
        {
            var q = repo.GetComponentRO<MissionPlanQueue>(e);
            q.CurrentTask = (byte)current; q.TaskCount = (byte)count; q.Halted = (byte)(halted ? 1 : 0);
            Span<MissionPhaseOutcome> o = q.Outcomes;
            if (endedIndex >= 0) o[endedIndex] = ended;
            repo.SetComponent(e, q);
        }

        /// <summary>
        /// ⭐ A plan of three: task 1 starts, ends Done (task 2 starts), task 2 FAILS (gave up — task 3 starts), task 3 ends Done (the plan finishes).
        /// The live index holds one Tasks entry per move, and the reader names each start and end. 🔴 Red-proof: <c>LatestOutcome</c> reading the
        /// CURRENT task instead of the one before ⇒ task 2 reads Done ⇒ red.
        /// </summary>
        [Fact]
        public void P8c_APlanReadsAsStartsAndEnds_WithHowEachEnded_AndTheSelectionAsBars()
        {
            using var harness = new FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<MissionPlanQueue>();
            var live = new LiveTimelineSource(new TimelineLaneCatalog().Register(new BehaviourTimelineLanes()), new TimelineLaneContext(Live: true), Compiler());
            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new NetworkIdentity(501));
            repo.AddComponent(unit, new MissionPlanQueue { TaskCount = 3 });
            long t = 100_000L;
            void Frame() { harness.Tick(); live.Step(repo, t, force: true); t += 100_000L; }

            Frame();                                                                          // frame 0: task 1 runs
            Frame();
            Set(repo, unit, 1, 3, 0, MissionPhaseOutcome.Done); Frame();                    // frame 2: 1 Done, 2 starts
            Frame();
            Set(repo, unit, 2, 3, 1, MissionPhaseOutcome.Failed); Frame();                  // frame 4: 2 Failed, 3 starts
            Set(repo, unit, 3, 3, 2, MissionPhaseOutcome.Done); Frame();                    // frame 5: 3 Done — finished
            Frame();

            var markers = new List<TaskMarker>();
            TasksLane.Markers(live.Index, markers);
            Assert.Equal(new[]
            {
                new TaskMarker(0, 501, 1, TaskMarkerKind.Started),
                new TaskMarker(2, 501, 1, TaskMarkerKind.Done), new TaskMarker(2, 501, 2, TaskMarkerKind.Started),
                new TaskMarker(4, 501, 2, TaskMarkerKind.Failed), new TaskMarker(4, 501, 3, TaskMarkerKind.Started),
                new TaskMarker(5, 501, 3, TaskMarkerKind.Done),
            }, markers);

            var bars = new List<TaskBar>();
            TasksLane.Bars(markers, 501, 6, bars);
            Assert.Equal(new[] { new TaskBar(1, 0, 2, TaskMarkerKind.Done), new TaskBar(2, 2, 4, TaskMarkerKind.Failed), new TaskBar(3, 4, 5, TaskMarkerKind.Done) }, bars);
        }

        /// <summary>⭐ A plan HALTED on a fault (the task stays current, its outcome Failed) reads Failed once; a new plan (task back to 1) starts again.</summary>
        [Fact]
        public void P8c_AHaltReadsFailedOnce_AndANewPlanStartsAgain()
        {
            using var harness = new FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<MissionPlanQueue>();
            var live = new LiveTimelineSource(new TimelineLaneCatalog().Register(new BehaviourTimelineLanes()), new TimelineLaneContext(Live: true), Compiler());
            var unit = repo.CreateEntity();
            repo.AddComponent(unit, new NetworkIdentity(7));
            repo.AddComponent(unit, new MissionPlanQueue { TaskCount = 2 });
            long t = 100_000L;
            void Frame() { harness.Tick(); live.Step(repo, t, force: true); t += 100_000L; }
            Frame();
            Set(repo, unit, 0, 2, 0, MissionPhaseOutcome.Failed, halted: true); Frame();   // frame 1: faulted on task 1
            Frame();
            Set(repo, unit, 0, 4, 0, MissionPhaseOutcome.None); Frame();                   // frame 3: a new plan of four
            var markers = new List<TaskMarker>();
            TasksLane.Markers(live.Index, markers);
            Assert.Equal(new[] { TaskMarkerKind.Started, TaskMarkerKind.Failed, TaskMarkerKind.Started }, markers.Select(m => m.Kind));
            Assert.Equal(new[] { 0, 1, 3 }, markers.Select(m => m.Frame));
        }

        /// <summary>⭐ R-4161 — a thousand units with plans: the Tasks group is ONE lane, and every unit's start is a marker in it naming the unit; the
        /// entity filter keeps one unit's.</summary>
        [Fact]
        public void P8c_AThousandUnits_StayOneLane()
        {
            Assert.Single(new BehaviourTimelineLanes().Lanes, l => l.Group == BehaviourTimelineLanes.TasksGroup);
            using var harness = new FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<MissionPlanQueue>();
            var live = new LiveTimelineSource(new TimelineLaneCatalog().Register(new BehaviourTimelineLanes()), new TimelineLaneContext(Live: true), Compiler());
            for (int k = 0; k < 1000; k++)
            {
                var u = repo.CreateEntity();
                repo.AddComponent(u, new NetworkIdentity(10_000 + k));
                repo.AddComponent(u, new MissionPlanQueue { TaskCount = 2 });
            }
            harness.Tick(); live.Step(repo, 100_000L, force: true);
            var markers = new List<TaskMarker>();
            TasksLane.Markers(live.Index, markers);
            Assert.Equal(1000, markers.Count);
            Assert.Equal(1000, markers.Select(m => m.Unit).Distinct().Count());
            TasksLane.Markers(live.Index, markers, unitFilter: 10_042);
            Assert.Equal(new TaskMarker(0, 10_042, 1, TaskMarkerKind.Started), Assert.Single(markers));
        }
    }
}
