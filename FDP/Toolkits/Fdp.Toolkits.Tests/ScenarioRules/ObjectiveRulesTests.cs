using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Fdp.Toolkits.Tests.ScenarioRules
{
    /// <summary>⭐ rules S1 (📄 docs/DESIGN_Scenario_Rules.md SR-113 … SR-117) — the pure rule set: lifecycle, score, OnClose, the params block.</summary>
    public sealed class ObjectiveRulesTests
    {
        private static ObjectiveStatus In(ObjectiveState st) => new() { State = st };

        [Fact]
        public void Begin_StartsActiveOrInactive_AndCopiesTheDisplayAndTheLinks()
        {
            var c = ObjectiveCommonBlock.From(new ObjectiveCommon { Title = "Hold the bridge", Required = true, Weight = 2f, Points = 10f, StartActive = false, VisibleSides = 0b10 });
            var s = new ObjectiveStatus(); var l = new ObjectiveLinks();
            ObjectiveRules.Begin(ref s, ref l, in c, "HoldArea", ObjectiveRole.Objective, now: 3.0);
            Assert.Equal(ObjectiveState.Inactive, s.State);
            Assert.Equal(("Hold the bridge", "HoldArea"), (s.Title.ToString(), s.Kind.ToString()));
            Assert.Equal((1, 2f, 10f, 0b10, 3.0), ((int)s.Required, s.Weight, s.Points, (int)s.VisibleSides, s.ChangedAt));
            ObjectiveRules.Begin(ref s, ref l, ObjectiveCommonBlock.From(default), "X", ObjectiveRole.Objective, 0);
            Assert.Equal(ObjectiveState.Active, s.State);   // the defaults: StartActive true
        }

        [Fact]
        public void TheCommonBlock_AppliesItsDefaults()
        {
            var c = ObjectiveCommonBlock.From(default);
            Assert.Equal((1, 1f, 0xFF, 1), ((int)c.Scored, c.Weight, (int)c.VisibleSides, (int)c.StartActive));
            Assert.Equal(0, c.Required);
        }

        /// <summary>✅ Red-proof: letting a final state be left ⇒ red.</summary>
        [Fact]
        public void Settle_NeverLeavesAFinalState_NorChangesAClosedOne_AndStampsChangedAtOnlyOnAChange()
        {
            var s = In(ObjectiveState.Active);
            Assert.True(ObjectiveRules.Settle(ref s, ObjectiveState.Achieved, "done", 5));
            Assert.Equal(5, s.ChangedAt);
            Assert.False(ObjectiveRules.Settle(ref s, ObjectiveState.Failed, "late", 9));
            Assert.Equal((ObjectiveState.Achieved, 5.0), (s.State, s.ChangedAt));

            var a = In(ObjectiveState.Active);
            Assert.False(ObjectiveRules.Settle(ref a, ObjectiveState.Active, "3/4", 2));   // same state: the reason only
            Assert.Equal(("3/4", 0.0), (a.Reason.ToString(), a.ChangedAt));

            var closed = new ObjectiveStatus { State = ObjectiveState.Active, Closed = 1 };
            Assert.False(ObjectiveRules.Settle(ref closed, ObjectiveState.Failed, "x", 1));
            Assert.Equal(ObjectiveState.Active, closed.State);
        }

        [Fact]
        public void TheDeadline_IsNoneAtZero_AndPassesAtItsTime()
        {
            Assert.False(ObjectiveRules.DeadlinePassed(ObjectiveCommonBlock.From(default), 1e9));
            var c = ObjectiveCommonBlock.From(new ObjectiveCommon { DeadlineSeconds = 5f });
            Assert.False(ObjectiveRules.DeadlinePassed(in c, 4.9));
            Assert.True(ObjectiveRules.DeadlinePassed(in c, 5.0));
        }

        [Fact]
        public void Score_AchievedOne_FailedZero_SkippedExcluded_ConstraintsOneWhileWithin_MeasuresValueOverTarget()
        {
            Assert.Equal(1f, ObjectiveRules.Score(In(ObjectiveState.Achieved)));
            Assert.Equal(0f, ObjectiveRules.Score(In(ObjectiveState.Failed)));
            Assert.True(float.IsNaN(ObjectiveRules.Score(In(ObjectiveState.Skipped))));
            Assert.Equal(0f, ObjectiveRules.Score(In(ObjectiveState.Active)));
            Assert.Equal(1f, ObjectiveRules.Score(In(ObjectiveState.Active), judgedAtEnd: true));
            Assert.Equal(0f, ObjectiveRules.Score(In(ObjectiveState.Failed), judgedAtEnd: true));
            Assert.Equal(0.5f, ObjectiveRules.Score(new ObjectiveStatus { Role = ObjectiveRole.Measure, State = ObjectiveState.Active, Value = 0.15f, Target = 0.30f }));
            Assert.Equal(1f, ObjectiveRules.Score(new ObjectiveStatus { Role = ObjectiveRole.Measure, Value = 9f, Target = 3f }));        // clamped
            Assert.Equal(1f, ObjectiveRules.Score(new ObjectiveStatus { Role = ObjectiveRole.Measure, Value = 0f, Target = 0f }));        // fratricide = 0
            // ⭐ rules S4 E-2 — lowerIsBetter: 1 within the target, else target / value; a zero target = any value over it scores ~0. ✅ Red-proof: the flag ignored ⇒ the 4-of-2 case reads 1 ⇒ red.
            ObjectiveStatus Low(float v, float t) => new() { Role = ObjectiveRole.Measure, Value = v, Target = t };
            Assert.Equal(1f, ObjectiveRules.Score(Low(30f, 60f), lowerIsBetter: true));
            Assert.Equal(1f, ObjectiveRules.Score(Low(60f, 60f), lowerIsBetter: true));
            Assert.Equal(0.5f, ObjectiveRules.Score(Low(4f, 2f), lowerIsBetter: true));
            Assert.Equal(1f, ObjectiveRules.Score(Low(0f, 0f), lowerIsBetter: true));
            Assert.Equal(0f, ObjectiveRules.Score(Low(1f, 0f), lowerIsBetter: true));
            Assert.Equal(1f, ObjectiveRules.Score(Low(4f, 1f)));                                       // higher-is-better default: value over target clamps to 1
            var s = In(ObjectiveState.Skipped); ObjectiveRules.WriteScore(ref s);
            Assert.Equal(0f, s.Score);
        }

        /// <summary>SR-117 ✅ Red-proof: JudgedAtEnd Active → Failed ⇒ red.</summary>
        [Fact]
        public void OnClose_FollowsSr117_ForEveryRuleAndState()
        {
            var within = In(ObjectiveState.Active); ObjectiveRules.OnClose(ref within, CloseRule.JudgedAtEnd, 10);
            Assert.Equal((ObjectiveState.Achieved, (byte)1), (within.State, within.Closed));

            var ordinary = In(ObjectiveState.Active); ObjectiveRules.OnClose(ref ordinary, CloseRule.Ordinary, 10);
            Assert.Equal((ObjectiveState.Failed, "not achieved by the end"), (ordinary.State, ordinary.Reason.ToString()));

            var failedConstraint = In(ObjectiveState.Failed); ObjectiveRules.OnClose(ref failedConstraint, CloseRule.JudgedAtEnd, 10);
            Assert.Equal((ObjectiveState.Failed, (byte)1), (failedConstraint.State, failedConstraint.Closed));

            foreach (var st in new[] { ObjectiveState.Pending, ObjectiveState.Inactive })
            {
                var s = In(st); ObjectiveRules.OnClose(ref s, CloseRule.Ordinary, 10);
                Assert.Equal(ObjectiveState.Skipped, s.State);
            }

            var capture = new ObjectiveStatus { State = ObjectiveState.Active, Owner = ForceId.Hostile };
            ObjectiveRules.OnClose(ref capture, CloseRule.Capture, 10);
            Assert.Equal((ObjectiveState.Active, ForceId.Hostile, (byte)1), (capture.State, capture.Owner, capture.Closed));

            var measure = new ObjectiveStatus { Role = ObjectiveRole.Measure, State = ObjectiveState.Active, Value = 2f };
            ObjectiveRules.OnClose(ref measure, CloseRule.Ordinary, 10);
            Assert.Equal((ObjectiveState.Active, 2f, (byte)1), (measure.State, measure.Value, measure.Closed));

            ObjectiveRules.OnClose(ref ordinary, CloseRule.JudgedAtEnd, 20);   // closed twice: nothing changes
            Assert.Equal(ObjectiveState.Failed, ordinary.State);
        }

        [Fact]
        public void CloseRuleOf_JudgesTheConstraintsAtTheEnd()
        {
            Assert.Equal(CloseRule.JudgedAtEnd, ObjectiveRules.CloseRuleOf("Protect"));
            Assert.Equal(CloseRule.JudgedAtEnd, ObjectiveRules.CloseRuleOf("NoHarm"));
            Assert.Equal(CloseRule.Ordinary, ObjectiveRules.CloseRuleOf("ReachArea"));
            Assert.Equal(CloseRule.Capture, ObjectiveRules.CloseRuleOf("HoldArea", capture: true));
        }

        /// <summary>⭐ rules S4 ② — the pass mark per kind, from ONE function: a measure / objective / constraint / trigger 1, a weighted composite 0.6, a Counter 0 (no verdict); an authored
        /// <c>common.pass</c> wins (capped at 1). ✅ Red-proof: the measure default changed to 0.5 ⇒ red.</summary>
        [Theory]
        [InlineData("HitRatio", ObjectiveRole.Measure, 0f, 1f)]
        [InlineData("Count", ObjectiveRole.Measure, 0f, 1f)]
        [InlineData("ReachArea", ObjectiveRole.Objective, 0f, 1f)]
        [InlineData("NoHarm", ObjectiveRole.Objective, 0f, 1f)]
        [InlineData("AllMustPass", ObjectiveRole.Objective, 0f, 1f)]
        [InlineData("Trigger", ObjectiveRole.Trigger, 0f, 1f)]
        [InlineData("WeightedMean", ObjectiveRole.Objective, 0f, 0.6f)]
        [InlineData("MemberEvaluation", ObjectiveRole.Objective, 0f, 0.6f)]
        [InlineData("SelectionMean", ObjectiveRole.Objective, 0f, 0.6f)]
        [InlineData("Counter", ObjectiveRole.Measure, 0f, 0f)]
        [InlineData("Points", ObjectiveRole.Measure, 0f, 0f)]
        [InlineData("Best", ObjectiveRole.Objective, 0f, 1f)]
        [InlineData("HitRatio", ObjectiveRole.Measure, 0.7f, 0.7f)]
        [InlineData("Counter", ObjectiveRole.Measure, 0.4f, 0.4f)]
        [InlineData("WeightedMean", ObjectiveRole.Objective, 0.8f, 0.8f)]
        [InlineData("HitRatio", ObjectiveRole.Measure, 5f, 1f)]
        public void PassOf_IsOneDefaultPerKind_AndTheAuthoredMarkWins(string kind, ObjectiveRole role, float authored, float want)
            => Assert.Equal(want, ObjectiveRules.PassOf(kind, role, authored));

        /// <summary>⭐ rules S4 ② — <c>common.pass</c> reaches the status through <c>Display</c>, and GO / NO-GO is the read <c>Score ≥ Pass</c>.</summary>
        [Fact]
        public void Display_WritesThePass_AndGoIsScoreAtLeastPass()
        {
            var c = ObjectiveCommonBlock.From(new ObjectiveCommon { Pass = 0.7f });
            var s = new ObjectiveStatus { Role = ObjectiveRole.Measure, Value = 0.75f, Target = 1f };
            ObjectiveRules.Display(ref s, in c, "HitRatio", ObjectiveRole.Measure);
            ObjectiveRules.WriteScore(ref s);
            Assert.Equal(0.7f, s.Pass);
            Assert.True(s.Score >= s.Pass);
            var def = ObjectiveCommonBlock.From(default);
            ObjectiveRules.Display(ref s, in def, "HitRatio", ObjectiveRole.Measure);
            Assert.Equal(1f, s.Pass);
            Assert.False(s.Score >= s.Pass);   // 0.75 of target 1 is NO-GO at the default pass 1
        }
    }
}
