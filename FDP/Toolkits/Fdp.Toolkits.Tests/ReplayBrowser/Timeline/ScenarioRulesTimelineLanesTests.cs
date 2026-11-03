using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Support;
using Fdp.Toolkit.ScenarioRules;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ SR-103 / RV-4 + rules S4 score history (handoff S4 Scores ④) — the rules lanes are value-change lanes on the recorded
    /// <see cref="ObjectiveStatus"/>: each holds exactly ITS field's changes, and a change of a field it does not project adds nothing.
    /// 🔴 Red-proof: the Score definition removed from <c>ScenarioRulesTimelineLanes</c> ⇒ the score history is empty.
    /// </summary>
    public class ScenarioRulesTimelineLanesTests
    {
        private const int Measure = 301, Trigger = 302;
        private static readonly TimelineLaneContext Recording = new(Recording: true);
        private static TimelineLaneCompiler Compiler() => new(new EventScannerCompiler(new ComponentEditServiceBuilder().Build()));

        private static List<(int Frame, int Subject, float X, float Value)> Of(TimelineIndex index, string lane)
            => index.Lane(lane).OrderBy(e => e.Frame).ThenBy(e => e.Subject).Select(e => (e.Frame, e.Subject, e.X, e.Value)).ToList();

        [Fact]
        public void TheRulesLanes_HoldTheirFieldsChanges_AndNothingElse()
        {
            using var harness = new FdpRecordingHarness();
            var repo = harness.Repository;
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<ObjectiveStatus>();

            var m = repo.CreateEntity();
            repo.AddComponent(m, new NetworkIdentity(Measure));
            repo.AddComponent(m, new ObjectiveStatus { Role = ObjectiveRole.Measure, State = ObjectiveState.Active, Score = 0.2f, Target = 1f });
            var t = repo.CreateEntity();
            repo.AddComponent(t, new NetworkIdentity(Trigger));
            repo.AddComponent(t, new ObjectiveStatus { State = ObjectiveState.Active });
            harness.Tick().RecordKeyframe(100_000L);                                                                  // frame 0

            repo.GetComponentRW<ObjectiveStatus>(m).ExpectedAt = 42;                                                    // not projected
            repo.GetComponentRW<ObjectiveStatus>(t).Reason = new Fdp.Core.FixedString64("waiting");                   // not projected
            harness.Tick().RecordDelta(200_000L);                                                                     // frame 1

            repo.GetComponentRW<ObjectiveStatus>(m).Score = 0.5f;
            repo.GetComponentRW<ObjectiveStatus>(t).FireCount = 1;
            harness.Tick().RecordDelta(300_000L);                                                                     // frame 2

            repo.GetComponentRW<ObjectiveStatus>(m).State = ObjectiveState.Achieved;
            repo.GetComponentRW<ObjectiveStatus>(m).Score = 1f;
            harness.Tick().RecordDelta(400_000L);                                                                     // frame 3
            string path = harness.BuildToTempFile();

            var lanes = new TimelineLaneCatalog().Register(new ScenarioRulesTimelineLanes()).Indexed(Recording);
            var index = TimelineIndexBuilder.Build(path, lanes, Compiler());

            Assert.Equal(new[] { (0, Measure, 1f, 0.2f), (0, Trigger, 0f, 0f), (2, Measure, 1f, 0.5f), (3, Measure, 1f, 1f) },
                         Of(index, ScenarioRulesTimelineLanes.Score.Id));
            Assert.Equal(new[] { (0, Measure, 0f, (float)ObjectiveState.Active), (0, Trigger, 0f, (float)ObjectiveState.Active), (3, Measure, 0f, (float)ObjectiveState.Achieved) },
                         Of(index, ScenarioRulesTimelineLanes.State.Id));
            Assert.Equal(new[] { (0, Measure, 0f, 0f), (0, Trigger, 0f, 0f), (2, Trigger, 0f, 1f) }, Of(index, ScenarioRulesTimelineLanes.Fired.Id));
        }

        /// <summary>⭐ CE-3330 — the rules provider is in the ONE shipped catalogue, so the editor, the Replay Browser and every node index it.</summary>
        [Fact]
        public void TheShippedCatalog_CarriesTheRulesLanes()
        {
            var ids = ShippedTimelineLanes.Catalog().Lanes(new TimelineLaneContext(Live: true)).Select(l => l.Id).ToList();
            Assert.Contains(ScenarioRulesTimelineLanes.Score.Id, ids);
            Assert.Contains(ScenarioRulesTimelineLanes.State.Id, ids);
            Assert.Contains(ScenarioRulesTimelineLanes.Fired.Id, ids);
        }
    }
}
