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
            Assert.Equal((0f, 2f, 4f, 60f), (s.HoldFor, (float)(s.Delay), (float)(s.Cooldown), s.Deadline));
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
        private static string TimeLeaf(double from) => "{\"$type\":\"ScenarioTime\",\"fromSeconds\":" + from.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        private static string Comp(string op, params string[] kids) => "{\"$type\":\"Compound\",\"operator\":\"" + op + "\",\"conditions\":[" + string.Join(",", kids) + "]}";

        /// <summary>An Armed trigger with the given <c>when</c> and no deadline, after <paramref name="run"/> s: its ExpectedAt.</summary>
        private static double ExpectedAfter(string when, double run)
        {
            var w = new ObjWorld();
            var t = w.Trigger("{\"common\":{\"title\":\"T\"},\"when\":" + when + ",\"do\":" + RecordSeven + "}");
            w.Run(run);
            Assert.Equal(TriggerState.Armed, Phase(w, t));
            return w.S(t).ExpectedAt;
        }

        /// <summary>CE-4238 (V-2) — a lone ScenarioTime leaf not reached yet ⇒ ExpectedAt = its fromSeconds. ✅ Red-proof: <c>TriggerWhenTime.ExpectedAt</c> returning 0 ⇒ red.</summary>
        [Fact] public void CE4238_ArmedTrigger_WaitingOnAScenarioTime_ExpectsItsFromSeconds() => Assert.Equal(900d, ExpectedAfter(TimeLeaf(900), 1.5));

        /// <summary>CE-4238 — through an Or the smallest time; a non-time sibling contributes nothing.</summary>
        [Fact]
        public void CE4238_Or_TakesTheSmallestTime_AndIgnoresNonTimeLeaves()
        {
            var w = new ObjWorld(); var src = Source(w);
            Assert.Equal(900d, ExpectedAfter(Comp("Or", TimeLeaf(900), WhenAchieved(w.Net(src))), 1.5));
            Assert.Equal(300d, ExpectedAfter(Comp("Or", TimeLeaf(300), TimeLeaf(900)), 1.5));
        }

        /// <summary>CE-4238 — through an And the largest time.</summary>
        [Fact] public void CE4238_And_TakesTheLargestTime() => Assert.Equal(900d, ExpectedAfter(Comp("And", TimeLeaf(300), TimeLeaf(900)), 1.5));

        /// <summary>CE-4238 — no time anywhere ⇒ 0 (as before).</summary>
        [Fact]
        public void CE4238_NoTimeLeaf_ExpectsNothing()
        {
            var w = new ObjWorld(); var src = Source(w);
            Assert.Equal(0d, ExpectedAfter(WhenAchieved(w.Net(src)), 1.5));
        }

        /// <summary>CE-4238 — only a time STILL IN THE FUTURE counts: past the first of two, the second; past both, 0.</summary>
        [Fact]
        public void CE4238_PastTimes_ContributeNothing()
        {
            var w = new ObjWorld(); var never = WhenAchieved(w.Net(Source(w)));
            // And(never, Or(3, 9)): stays Armed whatever the clock says
            string when = Comp("And", never, Comp("Or", TimeLeaf(3), TimeLeaf(9)));
            Assert.Equal(3d, ExpectedAfter(when, 1.5));
            Assert.Equal(9d, ExpectedAfter(when, 4.5));
            Assert.Equal(0d, ExpectedAfter(when, 10.5));
        }

        /// <summary>CE-4238 — a deadline still wins over the time the <c>when</c> waits on.</summary>
        [Fact]
        public void CE4238_ADeadline_StillWins()
        {
            var w = new ObjWorld();
            var t = w.Trigger("{\"common\":{\"title\":\"T\",\"deadlineSeconds\":60},\"when\":" + TimeLeaf(900) + ",\"do\":" + RecordSeven + "}");
            w.Run(1.5);
            Assert.Equal(60d, w.S(t).ExpectedAt);
        }
        /// <summary>CE-4238 follow-up (ui's ask) — a trigger whose <c>do</c> list holds an <c>EndExercise</c> step carries <c>EndsExercise</c> = 1 on its status (so a HUD on any node can read it); one without carries 0.
        /// ✅ Red-proof: <c>RuleLinks.Compute</c> not setting <c>EndsExercise</c> ⇒ the first assert reddens.</summary>
        [Fact]
        public void CE4238_ATriggerWhoseDoHoldsEndExercise_SaysSo_OnItsStatus()
        {
            var w = new ObjWorld(); var src = Source(w);
            var ends = w.Trigger(TrigJson(w.Net(src), "", Do(Step("TrgRecord", "{\"value\":1}"), Step("EndExercise", "{\"reason\":\"time is up\"}"))));
            var plain = w.Trigger(TrigJson(w.Net(src)));
            w.Run(1.5);
            Assert.Equal((byte)1, w.S(ends).EndsExercise);
            Assert.Equal((byte)0, w.S(plain).EndsExercise);
        }
    }
}
