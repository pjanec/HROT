using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐ rules V-1 / V-2 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — the rules view's data on the status: what a trigger's rule NAMES (DependsOn / ActsOn) and its timing.</summary>
    public sealed partial class ScenarioRulesTests
    {
        private static int[] Ids(NetIds8 ids, byte n) { var r = new int[n]; for (int i = 0; i < n; i++) r[i] = ids[i]; return r; }

        /// <summary>V-1 / V-2 — <c>when</c> = OnEntity(A) AND OnEntity(B), <c>do</c> = [SetObjectiveActive C, Message] ⇒ DependsOn {A,B}, ActsOn {C}; delay 2 ⇒ ExpectedAt while Waiting
        /// is the edge + delay, and the timing fields mirror the params. ✅ Red-proof: the Compound children not walked (<c>CompoundPredicateDto</c> arm of <c>RuleLinks.Walk</c> removed) ⇒ DependsOn empty ⇒ red.</summary>
        [Fact]
        public void ATriggersRule_NamesWhatItReadsAndActsOn_AndItsTimingOnTheStatus()
        {
            var w = new ObjWorld();
            var a = Source(w); var b = Source(w); var c = Cond(w);
            string when = "{\"$type\":\"Compound\",\"operator\":\"And\",\"conditions\":[" + WhenAchieved(w.Net(a)) + "," + WhenAchieved(w.Net(b)) + "]}";
            string doList = Do(Step("SetObjectiveActive", $"{{\"target\":{w.Net(c)},\"active\":true}}"), Step("Message", "{\"toKind\":\"All\",\"text\":\"go\"}"));
            var t = w.Trigger("{\"common\":{\"title\":\"T\",\"deadlineSeconds\":60},\"when\":" + when + ",\"do\":" + doList + ",\"delay\":2,\"holdFor\":0,\"cooldown\":4}");
            w.Run(1.5);
            var s = w.S(t);
            Assert.Equal(new[] { w.Net(a), w.Net(b) }, Ids(s.DependsOn, s.DependsOnCount));
            Assert.Equal(new[] { w.Net(c) }, Ids(s.ActsOn, s.ActsOnCount));
            Assert.Equal((0f, 2f, 4f, 60f), (s.HoldFor, s.Delay, s.Cooldown, s.Deadline));
            Assert.Equal(60d, s.ExpectedAt);                                   // Armed with a deadline: the deadline

            Flip(w, a, true); Flip(w, b, true); w.Run(0.5);                    // the edge tick
            double edge = w.Time;
            Assert.Equal(TriggerState.Waiting, Phase(w, t));
            Assert.Equal(edge + 2, w.S(t).ExpectedAt);                         // Waiting ⇒ edge + delay
        }

        /// <summary>V-2 — any objective kind: an objective with a deadline of 60 ⇒ Deadline 60 and ExpectedAt 60 while Active, 0 once final (the one place: ObjectiveHost / ObjectiveRules).
        /// ✅ Red-proof: <c>ObjectiveRules.Timing</c> not called from <c>ObjectiveHost.Enter</c> ⇒ red.</summary>
        [Fact]
        public void AnObjectivesDeadline_IsExpectedWhileActive_AndNothingOnceFinal()
        {
            var w = new ObjWorld();
            var man = w.Man(Outside, ForceId.Friend);
            var squad = w.Squad(ForceId.Friend, man);
            var o = w.Objective("ReachArea", $"{{{Common(w, squad, ",\"deadlineSeconds\":60")},\"area\":{Circle0}}}");
            w.Run(2);
            Assert.Equal((ObjectiveState.Active, 60f, 60d), (w.S(o).State, w.S(o).Deadline, w.S(o).ExpectedAt));
            w.Move(man, Inside);
            w.Run(1);
            Assert.Equal(ObjectiveState.Achieved, w.S(o).State);
            Assert.Equal((60f, 0d), (w.S(o).Deadline, w.S(o).ExpectedAt));
        }
    }
}
