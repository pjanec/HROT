using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ CE-2383 (T2; 📄 docs/DESIGN_Evaluation_Cost_And_Cadence.md §3.2–§3.4, R-2083) — THE RULES TICK AT A CADENCE: 5 Hz staggered, woken by what matters, each
    /// reading the bus through its own window. The world here keeps the kernel's event history (<c>ObjWorld.WithHistory</c>) and runs at 60 Hz, so a rule
    /// really skips frames. ⭐ What must not change: every kill / shot counted ONCE, a trigger's delay ending on its exact time, a parent following its
    /// children within a frame, the exercise close settling every rule at once.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        private const double Frame60 = 1.0 / 60.0;

        /// <summary>60 shots, one on EVERY frame for a second, and two kills on odd frames: the measures (ticking ~5 Hz, asleep between) count all 60 and both kills,
        /// and the Points parent sums them. ✅ Red-proof: the gate not opening the rule's window (it reads only its own frame) ⇒ shots lost ⇒ red.</summary>
        [Fact]
        public void CE2383_AtTheCadence_EveryShotAndKillIsCountedOnce()
        {
            var (w, squad, m, foe, pal, other) = ScoreWorld();
            w.WithHistory();
            var f2 = w.Man(Inside, ForceId.Hostile, health: 10f);
            var root = w.Objective("Points", $"{{{Cm("Score")},\"target\":1000}}");
            var kills = w.Objective("Count", $"{{{Cm("Kills", w.Net(squad), $",\"parent\":{w.Net(root)},\"points\":100")},\"what\":\"kills\"}}");
            var shots = w.Objective("Count", $"{{{Cm("Shots", w.Net(squad), $",\"parent\":{w.Net(root)},\"points\":1")},\"what\":\"shots\"}}");
            w.Run(0.5, Frame60);
            Assert.Equal((CadenceMode.Asleep, Hrot.AI.Behaviors.Brains.Objectives.RuleCadence.PolicyId), (w.Brain(shots).Cadence, w.Brain(shots).CadencePolicy));   // cadenced, by the rules' policy

            for (int i = 0; i < 60; i++)
            {
                Shoot(w, m, 1);
                if (i == 17) { w.Harm(foe, m); w.Kill(foe, m); }
                if (i == 41) { w.Harm(f2, m); w.Kill(f2, m); }
                w.Tick(Frame60);
            }
            w.Run(0.5, Frame60);   // their next ticks read the tail
            Assert.Equal(60f, w.S(shots).Value);
            Assert.Equal(2f, w.S(kills).Value);
            Assert.Equal(260f, w.S(root).Value);   // 60 × 1 + 2 × 100
        }

        /// <summary>A trigger with a 1 s delay sleeps through it and fires on its EXACT time (its <c>ExpectedAt</c>), not rounded to the 0.2 s period; while it waits
        /// its slot is asleep until that time. ✅ Red-proof: <c>RuleCadence.ModeOf</c> ignoring <c>ExpectedAt</c> ⇒ the fire lands up to a period late ⇒ red.</summary>
        [Fact]
        public void CE2383_ATriggersDelay_EndsOnItsExactTime()
        {
            var w = new ObjWorld();
            w.WithHistory();
            var src = Source(w);
            var t = w.Trigger(TrigJson(w.Net(src), ",\"delay\":1"));
            w.Run(1.5, Frame60);
            Flip(w, src, true);
            double expected = 0;
            for (int i = 0; i < 180 && w.S(t).FireCount == 0; i++)
            {
                w.Tick(Frame60);
                if (w.S(t).TriggerState == TriggerState.Waiting && expected == 0)
                {
                    expected = w.S(t).ExpectedAt;
                    Assert.Equal((CadenceMode.Asleep, expected), (w.Brain(t).Cadence, w.Brain(t).CadenceNextTick));   // asleep until its own time
                }
            }
            Assert.NotEqual(0, expected);
            Assert.Equal(1, w.S(t).FireCount);
            Assert.InRange(w.S(t).LastFired - expected, -1e-9, Frame60 + 1e-9);   // on the first frame at or after its time — not a period later
        }

        /// <summary>A child's change wakes its parent: the Points total follows a child's count within ONE frame of the child's own tick (not at the parent's next
        /// 5 Hz tick). ✅ Red-proof: <c>RuleCadence.AfterTick</c> not waking the parent ⇒ up to ~12 frames behind ⇒ red.</summary>
        [Fact]
        public void CE2383_AChildsChange_WakesItsParentAtTheNextFrame()
        {
            var (w, squad, m, foe, pal, other) = ScoreWorld();
            w.WithHistory();
            var root = w.Objective("Points", $"{{{Cm("Score")},\"target\":100}}");
            var shots = w.Objective("Count", $"{{{Cm("Shots", w.Net(squad), $",\"parent\":{w.Net(root)},\"points\":1")},\"what\":\"shots\"}}");
            w.Run(0.5, Frame60);
            int worstLag = 0, lag = 0;
            for (int i = 0; i < 120; i++)
            {
                if (i % 23 == 0) Shoot(w, m, 1);
                w.Tick(Frame60);
                lag = w.S(root).Value == w.S(shots).Value ? 0 : lag + 1;
                worstLag = Math.Max(worstLag, lag);
            }
            Assert.True(worstLag <= 1, $"the parent trailed its child by {worstLag} frames");
            Assert.Equal(w.S(shots).Value, w.S(root).Value);
        }

        /// <summary>The exercise closing wakes EVERY rule: measures and their Points parent are all closed within a few frames (a composite closes after its children),
        /// not spread over the period. ✅ Red-proof: <c>RuleWakeSystem</c> not waking on the phase change ⇒ closes trail by up to a period ⇒ red.</summary>
        [Fact]
        public void CE2383_TheExerciseClosing_SettlesEveryRuleAtOnce()
        {
            var (w, squad, m, foe, pal, other) = ScoreWorld();
            var root = w.Objective("Points", $"{{{Cm("Score")},\"target\":100}}");
            var children = Enumerable.Range(0, 6).Select(i =>
                w.Objective("Count", $"{{{Cm("C" + i, w.Net(squad), $",\"parent\":{w.Net(root)},\"points\":1")},\"what\":\"shots\"}}")).ToArray();
            w.Run(1);
            w.WithHistory();
            w.Run(0.5, Frame60);
            EndTheExercise(w);   // arms + flips the trigger; it notices within a period (D2) and runs EndExercise
            for (int i = 0; i < 120 && ExerciseResult.PhaseOf(w.Repo) == ExercisePhase.Running; i++) w.Tick(Frame60);
            Assert.NotEqual(ExercisePhase.Running, ExerciseResult.PhaseOf(w.Repo));
            int frames = 0;   // counted from the phase change: what the wake governs
            while (frames < 60 && (w.S(root).Closed == 0 || children.Any(c => w.S(c).Closed == 0))) { w.Tick(Frame60); frames++; }
            Assert.True(frames <= 4, $"closing took {frames} frames");
        }

        /// <summary>A trigger running its <c>do</c> list ticks EVERY frame (an action waits on one-frame acknowledgements); a closed rule sleeps until woken.</summary>
        [Fact]
        public void CE2383_AnActingTrigger_TicksEveryFrame_AFinalRuleSleeps()
        {
            var w = new ObjWorld { MissionAnswer = null };   // the order is never answered: the trigger stays Acting
            w.WithHistory();
            var unit = w.Unit();
            var src = Cond(w);
            var t = Fire(w, src, Do(Step("Order", $"{{\"target\":{w.Net(unit)},\"behavior\":\"HoldStance\"}}")));
            Flip(w, src, true);
            for (int i = 0; i < 30 && w.S(t).TriggerState != TriggerState.Acting; i++) w.Tick(Frame60);
            Assert.Equal(TriggerState.Acting, w.S(t).TriggerState);
            w.Tick(Frame60);
            Assert.Equal(CadenceMode.EveryFrame, w.Brain(t).Cadence);

            var (sw, squad, m, foe, pal, other) = ScoreWorld();
            sw.WithHistory();
            var reach = sw.Objective("Count", $"{{{Cm("Shots", sw.Net(squad))},\"what\":\"shots\",\"target\":1}}");
            sw.Run(0.5, Frame60);
            sw.Repo.GetComponentRW<ObjectiveStatus>(reach).Closed = 1;   // closed: nothing changes it any more
            sw.Run(0.5, Frame60);
            Assert.Equal((CadenceMode.Asleep, CadenceGate.Never), (sw.Brain(reach).Cadence, sw.Brain(reach).CadenceNextTick));
        }
    }
}
