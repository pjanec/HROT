using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.Weather;
using Hrot.AI.Behaviors.Brains.Objectives.Verbs;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S2 ⑧ (📄 docs/DESIGN_Scenario_Rules.md §00.9 S2-d / S2-e / S2-f, SR-114 / SR-116 / SR-117) — the VERBS (<c>SetObjectiveActive</c> · <c>EndExercise</c> · <c>Message</c>),
    /// run as a trigger's <c>do</c> list through the real load path (assignment → <c>BrainTickSystem</c>), on objective entities that are really ticking.
    /// </summary>
    public sealed partial class ScenarioRulesTests
    {
        private static string Do(params string[] steps) => "[" + string.Join(",", steps) + "]";
        private static string Step(string act, string args) => "{\"act\":\"" + act + "\",\"args\":" + args + "}";

        /// <summary>A trigger that fires ONCE when <paramref name="src"/> is flipped and runs <paramref name="doList"/>.</summary>
        private static Entity Fire(ObjWorld w, Entity src, string doList)
        {
            var t = w.Trigger(TrigJson(w.Net(src), "", doList));
            w.Run(1.5);   // assigned and armed (a condition already true when it arms is not an edge)
            return t;
        }

        /// <summary>The condition source of the verb rails: a status with <c>Role = Trigger</c> (so it is no root objective and no straggler) whose State the rail flips.</summary>
        private static Entity Cond(ObjWorld w)
        {
            var e = Source(w);
            w.Repo.GetComponentRW<ObjectiveStatus>(e).Role = ObjectiveRole.Trigger;
            return e;
        }

        private static Entity HoldObjective(ObjWorld w, Entity squad, string extra = "")
            => w.Objective("HoldArea", $"{{{Common(w, squad, extra)},\"area\":{Circle0},\"holdSeconds\":10}}");

        /// <summary>SR-114 — <c>SetObjectiveActive</c>: off freezes (progress kept, nothing evaluated), on resumes; instant (Success the same tick); a target that is final needs a
        /// reset; a missing target fails with its reason. ✅ Red-proof: <c>ObjectiveRules.SetActive</c> writing Active for <c>active:false</c> ⇒ the frozen assertions go red.</summary>
        [Fact]
        public void SetObjectiveActive_SwitchesOffAndOn_KeepsTheProgress_AndRefusesAFinalOneWithoutReset()
        {
            var w = new ObjWorld();
            var men = new[] { w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend) };
            var squad = w.Squad(ForceId.Friend, men);
            var hold = HoldObjective(w, squad);
            var src = Cond(w);
            w.Run(3);

            var off = Fire(w, src, Do(Step("SetObjectiveActive", $"{{\"target\":{w.Net(hold)},\"active\":false}}")));
            Flip(w, src, true); w.Run(0.5);
            Assert.Equal(ObjectiveState.Inactive, w.S(hold).State);
            Assert.Equal(TriggerState.Fired, Phase(w, off));                       // instant: the list finished in the firing tick
            int held = w.S(hold).ProgressCur;
            Assert.InRange(held, 2, 5);
            w.Run(5);
            Assert.Equal(held, w.S(hold).ProgressCur);                             // frozen: nothing evaluated, progress kept

            var on = Fire(w, src, Do(Step("SetObjectiveActive", $"{{\"target\":{w.Net(hold)},\"active\":true}}")));
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);
            Assert.Equal(ObjectiveState.Active, w.S(hold).State);
            w.Run(12);
            Assert.Equal(ObjectiveState.Achieved, w.S(hold).State);                // resumed from the kept progress and finished

            // a final one: refused without reset, with the reason on the trigger
            var again = Fire(w, src, Do(Step("SetObjectiveActive", $"{{\"target\":{w.Net(hold)},\"active\":true}}")));
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);
            Assert.Equal(ObjectiveState.Failed, w.S(again).State);
            Assert.Contains("only a reset leaves it", w.S(again).Reason.ToString());
            Assert.Equal(ObjectiveState.Achieved, w.S(hold).State);

            // a missing target
            var ghost = Fire(w, src, Do(Step("SetObjectiveActive", "{\"target\":99999,\"active\":false}")));
            Flip(w, src, false); w.Run(0.5); Flip(w, src, true); w.Run(0.5);
            Assert.Equal(ObjectiveState.Failed, w.S(ghost).State);
            Assert.Contains("target not found", w.S(ghost).Reason.ToString());
        }

        /// <summary>SR-114 — <c>reset: true</c> is the only way out of a final state, and it RESTARTS the objective: the hold timer is cleared (the kind's working state), the status
        /// is Active again, and it can be achieved a second time. ✅ Red-proof: the <c>f.Reset</c> clear removed from <c>HoldAreaBehavior.Tick</c> ⇒ the second hold completes at once ⇒ red.</summary>
        [Fact]
        public void SetObjectiveActive_WithReset_RestartsAFinalObjective_AndClearsItsWorkingState()
        {
            var w = new ObjWorld();
            var squad = w.Squad(ForceId.Friend, w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend), w.Man(Inside, ForceId.Friend));
            var hold = w.Objective("HoldArea", $"{{{Common(w, squad)},\"area\":{Circle0},\"holdSeconds\":4}}");
            var src = Cond(w);
            w.Run(6);
            Assert.Equal(ObjectiveState.Achieved, w.S(hold).State);

            Fire(w, src, Do(Step("SetObjectiveActive", $"{{\"target\":{w.Net(hold)},\"active\":true,\"reset\":true}}")));
            Flip(w, src, true); w.Run(1);              // the trigger fires, the objective restarts at its next tick
            Assert.Equal(ObjectiveState.Active, w.S(hold).State);                  // out of Achieved
            Assert.InRange(w.S(hold).ProgressCur, 0, 1);                           // the timer restarted
            w.Run(2);
            Assert.Equal(ObjectiveState.Active, w.S(hold).State);                  // not achieved at once from a kept timer
            w.Run(4);
            Assert.Equal(ObjectiveState.Achieved, w.S(hold).State);                // a second time
        }

        /// <summary>SR-116 / SR-117 / SR-122 — the END: a trigger whose <c>do</c> = [Message, EndExercise] runs both in order; the ReachArea (nobody inside) closes Failed "not achieved by
        /// the end", the NoHarm (judged at the end) closes Achieved, both Closed; <c>ExerciseResult.Phase</c> Ended with a Lose / Win for the side, EndedAt and the reason; the trigger is Done;
        /// <c>ExerciseEndedEvent</c> raised ONCE; the message was raised BEFORE the end. ✅ Red-proofs: <c>CloseRuleOf</c> not asked at Closing (<c>ObjectiveHost.Enter</c>'s close removed) ⇒
        /// both stay Active ⇒ red; the event published on every tick of the Ended phase ⇒ the count rail goes red.</summary>
        [Fact]
        public void EndExercise_ClosesEveryObjectiveByItsKind_WritesTheOutcome_RaisesTheEventOnce_AfterTheMessage()
        {
            var w = new ObjWorld();
            var env = w.Environment();
            var squad = w.Squad(ForceId.Friend, w.Man(Outside, ForceId.Friend), w.Man(Outside, ForceId.Friend));
            var reach = w.Objective("ReachArea", $"{{{Common(w, squad, ",\"required\":true")},\"area\":{Circle0}}}");
            var noHarm = w.Objective("NoHarm", $"{{{Common(w, squad, ",\"title\":\"No harm\"").Replace("\"title\":\"T\",", "")},\"protected\":\"Neutral\",\"bySide\":\"Friend\"}}");
            var src = Cond(w);
            var end = Fire(w, src, Do(Step("Message", "{\"toKind\":\"All\",\"text\":\"Exercise over\",\"seconds\":4,\"priority\":2}"),
                                      Step("EndExercise", "{\"reason\":\"time is up\"}")));
            w.Run(3);
            Assert.Equal((ObjectiveState.Active, ObjectiveState.Active), (w.S(reach).State, w.S(noHarm).State));
            Assert.Equal(ExercisePhase.Running, ExerciseResult.PhaseOf(w.Repo));

            Flip(w, src, true); w.Run(0.5);                                        // the end fires: Message, EndExercise begins (Closing)
            Assert.Equal(ExercisePhase.Closing, w.Repo.GetComponentRO<ExerciseResult>(env).Phase);
            w.Run(2);
            var r = w.Repo.GetComponentRO<ExerciseResult>(env);
            Assert.Equal((ExercisePhase.Ended, "time is up"), (r.Phase, r.Reason.ToString()));
            Assert.InRange(r.EndedAt, 3.0, w.Time);

            Assert.Equal((ObjectiveState.Failed, (byte)1, "not achieved by the end"), (w.S(reach).State, w.S(reach).Closed, w.S(reach).Reason.ToString()));
            Assert.Equal((ObjectiveState.Achieved, (byte)1), (w.S(noHarm).State, w.S(noHarm).Closed));
            Assert.Equal(SideOutcome.Lose, r.Friend.Outcome);                      // its required ReachArea failed
            Assert.Equal(0.5f, r.Friend.Score);                                    // (0 + 1) / 2
            Assert.Equal((SideOutcome.None, SideOutcome.None), (r.Hostile.Outcome, r.Neutral.Outcome));
            Assert.Equal((TriggerState.Done, ObjectiveState.Achieved, (byte)1), (Phase(w, end), w.S(end).State, w.S(end).Closed));   // it finished its own list, then closed
            Assert.Equal(1, w.Ended);                                              // ONCE
            w.Run(3);
            Assert.Equal(1, w.Ended);
            var msg = Assert.Single(w.Messages);
            Assert.Equal(("All", "Exercise over", 4f, (byte)2), (msg.ToKind, msg.Text, msg.Seconds, msg.Priority));
            Assert.Equal(w.Net(end), msg.Source);
        }

        /// <summary>SR-116 ③ — a straggler that never closes (a status nobody ticks) does not hold the end: after 2 s of sim time it goes on and ends anyway. ✅ Red-proof: the timeout
        /// removed from <c>EndExercise</c> ⇒ never Ended ⇒ red.</summary>
        [Fact]
        public void EndExercise_DoesNotWaitForeverForAStraggler_AfterTwoSecondsItEnds()
        {
            var w = new ObjWorld();
            var env = w.Environment();
            var stuck = Source(w);                                                 // an Objective-role status with no brain: it will never close
            w.Repo.GetComponentRW<ObjectiveStatus>(stuck).Title = new FixedString64("Orphan");
            var src = Cond(w);
            Fire(w, src, Do(Step("EndExercise", "{\"reason\":\"x\"}")));
            w.Run(1);
            Flip(w, src, true); w.Run(1.5);                                        // closing for 1.5 s: still waiting
            Assert.Equal(ExercisePhase.Closing, w.Repo.GetComponentRO<ExerciseResult>(env).Phase);
            w.Run(1);
            Assert.Equal(ExercisePhase.Ended, w.Repo.GetComponentRO<ExerciseResult>(env).Phase);
            Assert.Equal(0, w.S(stuck).Closed);                                    // it was never closed — the end went on without it
            w.Run(0.5);
            Assert.Equal(1, w.Ended);
        }

        /// <summary>SR-116 — EndExercise without a World Environment fails with a reason (nothing half-written). Ending twice is refused too.</summary>
        [Fact]
        public void EndExercise_FailsWithoutAWorldEnvironment()
        {
            var w = new ObjWorld();
            var src = Cond(w);
            var t = Fire(w, src, Do(Step("EndExercise", "{}")));
            w.Run(1); Flip(w, src, true); w.Run(1);
            Assert.Equal(ObjectiveState.Failed, w.S(t).State);
            Assert.Contains("no World Environment", w.S(t).Reason.ToString());
        }

        /// <summary>SR-121 — <c>Message</c> is validated at ASSIGNMENT: a text over 127 characters or an unknown <c>toKind</c> is refused (the entity gets no behaviour), a good one is
        /// accepted. ✅ Red-proof: <c>MessageParams.Check</c> removed ⇒ the long text is silently cut ⇒ red.</summary>
        [Fact]
        public void Message_IsValidatedAtAssignment_TextTooLongAndUnknownKindAreRefused()
        {
            Assert.NotNull(Record.Exception(() => ActionList.Validate(Do(Step("Message", "{\"text\":\"" + new string('x', 128) + "\"}")))));
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Do(Step("Message", "{\"toKind\":\"Everyone\",\"text\":\"hi\"}"))));
            Assert.Contains("toKind", ex.Message);
            ActionList.Validate(Do(Step("Message", "{\"toKind\":\"Side\",\"toValue\":1,\"text\":\"" + new string('x', 127) + "\"}")));   // the limit itself passes
            Assert.Equal(6f, new MessageParams().Seconds);                          // the documented default
        }
    }
}
