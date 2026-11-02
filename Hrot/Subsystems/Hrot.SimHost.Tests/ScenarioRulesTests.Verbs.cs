using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Fdp.Toolkit.Weather;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Time.Domain;
using Hrot.AI.Behaviors.Brains.Objectives;
using Hrot.AI.Behaviors.Brains.Objectives.Verbs;
using Hrot.Common.Infrastructure;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Mission;
using Hrot.MuscleCharacter.Animation.Components;
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

        // ═════════════════════════════════════════ rules S2 ⑨ — pause-on-end, Order, MissionCommand ═════════════════════════════════════════

        private static string OrderStep(ObjWorld w, Entity target, string behavior, string extra = "")
            => Step("Order", $"{{\"target\":{w.Net(target)},\"behavior\":\"{behavior}\"{extra}}}");

        /// <summary>SR-97 — <c>Order</c> through the real path: a trigger whose do = [Order(HoldStance, params {stance: Prone}), Message] — the order is Running until the unit's active behaviour IS
        /// <c>HoldStance</c> (the hash), the unit runs it WITH the nested params (the stance it writes), and only then does the Message go. ✅ Red-proofs: <c>Order</c> returning Success on the first tick ⇒ the
        /// message precedes the unit's behaviour ⇒ red; the <c>ArgsRef</c> seeding removed ⇒ the unit gets no params ⇒ red.</summary>
        [Fact]
        public void Order_RunsUntilTheUnitHoldsTheBehaviour_WithTheNestedParams_ThenTheNextStepGoes()
        {
            var w = new ObjWorld();
            var unit = w.Unit();
            var src = Cond(w);
            w.Run(1);
            Assert.Equal(0, w.Repo.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash);
            var t = Fire(w, src, Do(OrderStep(w, unit, "HoldStance", ",\"params\":{\"stance\":\"Prone\"}"), Step("Message", "{\"text\":\"ordered\"}")));
            int hold = Fdp.Toolkit.Behavior.BehaviorHash.FromName("HoldStance");
            Flip(w, src, true);
            w.Tick(0.5);
            Assert.Equal(TriggerState.Acting, Phase(w, t));                                   // the order is out, not yet taken
            Assert.Empty(w.Messages);
            bool heldWhenMessageSeen = false;
            for (int i = 0; i < 12 && w.Messages.Count == 0; i++) { w.Tick(0.5); heldWhenMessageSeen = w.Repo.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash == hold; }
            Assert.Single(w.Messages);
            Assert.True(heldWhenMessageSeen);                                                 // the next step came AFTER the unit held the behaviour
            Assert.Equal(hold, w.Repo.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash);
            w.Run(1.5);
            Assert.Equal(StanceId.Prone, w.Repo.GetComponentRO<StanceIntent>(unit).TargetStance);   // it started WITH the params
            Assert.Equal((TriggerState.Done, BehaviorOrigin.Superior), (Phase(w, t), w.Repo.GetComponentRO<BehaviorState>(unit).Origin));
        }

        /// <summary>SR-97 — an order nobody takes fails after 10 s of sim time with "not accepted" (and the Message after it never goes); a missing target and a target with no brain fail at once with their
        /// reasons. ✅ Red-proof: the timeout test removed ⇒ the trigger stays Acting for ever ⇒ red.</summary>
        [Fact]
        public void Order_NotAccepted_FailsAfterTenSeconds_AndABadTargetFailsAtOnce()
        {
            var w = new ObjWorld { ResolveIntents = false };                                 // the intent is published and never turned into an assignment
            var unit = w.Unit();
            var man = w.Man(Inside, ForceId.Friend);                                         // no brain
            var src = Cond(w);
            var late = Fire(w, src, Do(OrderStep(w, unit, "HoldStance"), Step("Message", "{\"text\":\"never\"}")));
            var ghost = Fire(w, src, Do(Step("Order", "{\"target\":99999,\"behavior\":\"HoldStance\"}")));
            var mute = Fire(w, src, Do(OrderStep(w, man, "HoldStance")));
            Flip(w, src, true); w.Run(9);
            Assert.Equal(TriggerState.Acting, Phase(w, late));                                // still waiting at 9 s
            Assert.Equal(ObjectiveState.Failed, w.S(ghost).State);
            Assert.Contains("target not found", w.S(ghost).Reason.ToString());
            Assert.Contains("target has no brain", w.S(mute).Reason.ToString());
            w.Run(2);
            Assert.Equal(ObjectiveState.Failed, w.S(late).State);
            Assert.Contains("not accepted", w.S(late).Reason.ToString());
            Assert.Empty(w.Messages);
        }

        /// <summary>SR-97 — a behaviour name this host does not declare is refused at ASSIGNMENT (not after the 10 s wait); so is a text-less name; <c>params</c> on an action with no <c>ArgsRef</c> too.</summary>
        [Fact]
        public void Order_IsValidatedAtAssignment_UnknownBehaviourAndMissingNameAreRefused()
        {
            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Do(Step("Order", "{\"target\":5,\"behavior\":\"NoSuchBehaviour\"}"))));
            Assert.Contains("unknown behaviour 'NoSuchBehaviour'", ex.Message);
            Assert.Throws<ActionListException>(() => ActionList.Validate(Do(Step("Order", "{\"target\":5}"))));
            ActionList.Validate(Do(Step("Order", "{\"target\":5,\"behavior\":\"HoldStance\",\"ArgsRef\":2}")));
            Assert.Throws<ActionListException>(() => ActionList.ExtractParams(Do(Step("Message", "{\"text\":\"x\",\"params\":{}}")), 2));
            Assert.Equal(-1, new OrderParams().ArgsRef);                                       // no params ⇒ none
        }

        private static (Entity Unit, Guid[] Tasks) PlannedUnit(ObjWorld w)
        {
            var unit = w.Unit();
            try { Hrot.Map.Common.MissionComponentRegistry.RegisterAll(w.Repo); } catch (InvalidOperationException) { /* already registered by the host registry */ }
            var tasks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            w.Repo.SetManagedComponent(unit, new ActiveMissionPlan { TaskOrder = tasks });
            return (unit, tasks);
        }

        /// <summary>MissionCommand — Jump publishes <c>MissionControlIntent</c> (CMD_JUMP_TO_TASK, the target's net id, the task id at <c>taskIndex</c> of its <c>TaskOrder</c>) with a fresh RequestId and
        /// runs until the ack with that id: error 0 ⇒ Success, an error code ⇒ Failure "mission control error N". ✅ Red-proof: the ack matched on any id ⇒ (n/a, one request) — instead: the error branch
        /// returning Success ⇒ the error rail goes red.</summary>
        [Fact]
        public void MissionCommand_Jump_PublishesTheIntent_AndSucceedsOrFailsOnTheAck()
        {
            foreach (var (code, ok) in new[] { (0, true), (5, false) })
            {
                var w = new ObjWorld { MissionAnswer = code };
                var (unit, tasks) = PlannedUnit(w);
                var src = Cond(w);
                var t = Fire(w, src, Do(Step("MissionCommand", $"{{\"target\":{w.Net(unit)},\"command\":\"Jump\",\"taskIndex\":1}}"), Step("Message", "{\"text\":\"jumped\"}")));
                Flip(w, src, true); w.Run(3);
                var intent = Assert.Single(w.MissionIntents);
                Assert.Equal((eMissionCommandType.CMD_JUMP_TO_TASK, tasks[1], w.Net(unit)), (intent.Payload.CommandType, intent.Payload.TargetTaskId, intent.TargetEntityId));
                Assert.NotEqual(Guid.Empty, intent.RequestId);
                if (ok) { Assert.Equal(TriggerState.Done, Phase(w, t)); Assert.Single(w.Messages); }
                else
                {
                    Assert.Equal(ObjectiveState.Failed, w.S(t).State);
                    Assert.Contains("mission control error 5", w.S(t).Reason.ToString());
                    Assert.Empty(w.Messages);
                }
            }
        }

        /// <summary>MissionCommand — Abort needs no plan (CMD_ABORT_ALL); an unanswered request fails after 10 s ("no mission control ack"); a jump to a task the plan does not have fails at once;
        /// Replace is refused at assignment (a verb carries no plan). ✅ Red-proof: the 10 s timeout removed ⇒ stays Acting ⇒ red.</summary>
        [Fact]
        public void MissionCommand_AbortAndTheFailurePaths()
        {
            var w = new ObjWorld { MissionAnswer = 0 };
            var (unit, _) = PlannedUnit(w);
            var src = Cond(w);
            var abort = Fire(w, src, Do(Step("MissionCommand", $"{{\"target\":{w.Net(unit)},\"command\":\"Abort\"}}")));
            Flip(w, src, true); w.Run(3);
            Assert.Equal(TriggerState.Done, Phase(w, abort));
            Assert.Equal(eMissionCommandType.CMD_ABORT_ALL, Assert.Single(w.MissionIntents).Payload.CommandType);

            var w2 = new ObjWorld();                                                          // nobody answers
            var (unit2, _) = PlannedUnit(w2);
            var src2 = Cond(w2);
            var silent = Fire(w2, src2, Do(Step("MissionCommand", $"{{\"target\":{w2.Net(unit2)},\"command\":\"Abort\"}}")));
            var farTask = Fire(w2, src2, Do(Step("MissionCommand", $"{{\"target\":{w2.Net(unit2)},\"command\":\"Jump\",\"taskIndex\":7}}")));
            Flip(w2, src2, true); w2.Run(9);
            Assert.Equal(TriggerState.Acting, Phase(w2, silent));
            Assert.Contains("has no task 7", w2.S(farTask).Reason.ToString());
            w2.Run(2);
            Assert.Equal(ObjectiveState.Failed, w2.S(silent).State);
            Assert.Contains("no mission control ack", w2.S(silent).Reason.ToString());

            var ex = Assert.Throws<ActionListException>(() => ActionList.Validate(Do(Step("MissionCommand", "{\"target\":5,\"command\":\"Replace\"}"))));
            Assert.Contains("full mission plan", ex.Message);
        }

        private static int PausesAfter(EntityRepository repo, ExercisePauseOnEndSystem sys, Action<EntityRepository>? publish, Action? run = null)
        {
            repo.Bus.SwapBuffers();
            publish?.Invoke(repo);
            repo.Bus.SwapBuffers();
            if (run != null) run(); else sys.Execute(repo, 0.1f);
            repo.Bus.SwapBuffers();
            return ((ISimulationView)repo).ReadManagedEvents<PauseTimeIntent>().Count;
        }

        /// <summary>SR-116 — pause-on-end: exactly ONE <c>PauseTimeIntent</c> per frame that carries an <c>ExerciseEndedEvent</c>, none without one, and none on the frame after (an event lives one frame — it is
        /// not a toggle and does not repeat). ✅ Red-proof: the system publishing every frame ⇒ the "none without" rail goes red; publishing once per event but two events in one frame ⇒ n/a (the event is once per node).</summary>
        [Fact]
        public void PauseOnEnd_PublishesExactlyOnePauseIntentPerEnd_NoneWithoutOne()
        {
            var repo = new EntityRepository();
            repo.Bus.Register<ExerciseEndedEvent>(); repo.Bus.RegisterManaged<PauseTimeIntent>();
            var sys = new ExercisePauseOnEndSystem();
            Assert.Equal(0, PausesAfter(repo, sys, null));
            Assert.Equal(1, PausesAfter(repo, sys, r => r.Bus.Publish(new ExerciseEndedEvent { EndedAt = 5 })));
            Assert.Equal(0, PausesAfter(repo, sys, null));                                    // the next frame: the event is gone, no second pause
        }

        /// <summary>SR-116 — the feature <c>exercise.pause-on-end</c> is declared by the Brain capabilities, ON by default; the scheduler runs the system only while it is on: off ⇒ no pause intent. The
        /// pack carries the system. ✅ Red-proof: <c>[NodeFeature]</c> removed from the system ⇒ the off rail publishes ⇒ red.</summary>
        [Fact]
        public void PauseOnEnd_IsANodeFeature_OnByDefault_AndOffMeansNoPause_AndThePackCarriesIt()
        {
            var features = new NodeFeatures(new FeatureRegistry(new[] { ExercisePauseOnEndSystem.Feature }), NodeRole.Brain);
            Assert.True(features.On(ExercisePauseOnEndSystem.FeatureId));
            var repo = new EntityRepository();
            repo.Bus.Register<ExerciseEndedEvent>(); repo.Bus.RegisterManaged<PauseTimeIntent>();
            var sys = new ExercisePauseOnEndSystem();
            var sch = new SystemScheduler { FeatureGate = features };
            sch.RegisterSystem(sys); sch.BuildExecutionOrders();
            void Phase() => sch.ExecutePhase(SystemPhase.Simulation, repo, 0.1f);
            Assert.Equal(1, PausesAfter(repo, sys, r => r.Bus.Publish(new ExerciseEndedEvent { EndedAt = 1 }), Phase));
            features.SetNodeOverride(ExercisePauseOnEndSystem.FeatureId, "{\"Enabled\":false}");
            Assert.Equal(0, PausesAfter(repo, sys, r => r.Bus.Publish(new ExerciseEndedEvent { EndedAt = 2 }), Phase));

            var pack = new Hrot.CGF.CgfLogicPack(new Fdp.Toolkit.Behavior.BehaviorRegistry(), new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
                new Fdp.Toolkit.Behavior.TacticalOrderMapper.TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            Assert.Single(pack.SimulationSystems.OfType<ExercisePauseOnEndSystem>());
        }

        /// <summary>⭐ SR-116 — the pause goes out on the NODE's application bus, never the world's: the node builds a SEPARATE bus
        /// (<c>HrotNodeBuilder.cs:143</c>) and only that one reaches the cluster master (<c>ClusterOpEgressTranslator</c>). 📌 Live on
        /// <c>sr-triggers-march</c> (2026-11-02) the end ran and the cluster kept running: the system published on <c>repo.Bus</c>. The pack forwards
        /// the bus it is given (both Brain hosts pass <c>HrotNodeContext.EventBus</c>: <c>CgfNodeBootstrapper</c>, <c>EditorNodeBootstrapper</c>).
        /// ✅ Red-proof: the system publishing on <c>repo.Bus</c> again ⇒ the node bus sees 0 ⇒ red.</summary>
        [Fact]
        public void PauseOnEnd_GoesOutOnTheNodeBus_NotTheWorldBus_AndThePackForwardsIt()
        {
            var node = new FdpEventBus();
            node.RegisterManaged<PauseTimeIntent>();
            var repo = new EntityRepository();
            repo.Bus.Register<ExerciseEndedEvent>(); repo.Bus.RegisterManaged<PauseTimeIntent>();
            var sys = new ExercisePauseOnEndSystem(node);
            repo.Bus.Publish(new ExerciseEndedEvent { EndedAt = 3 });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0.1f);
            repo.Bus.SwapBuffers(); node.SwapBuffers();
            Assert.Equal(0, ((ISimulationView)repo).ReadManagedEvents<PauseTimeIntent>().Count);
            int onNode = 0; foreach (var _ in node.ReadManaged<PauseTimeIntent>()) onNode++;
            Assert.Equal(1, onNode);

            var pack = new Hrot.CGF.CgfLogicPack(new Fdp.Toolkit.Behavior.BehaviorRegistry(), new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
                new Fdp.Toolkit.Behavior.TacticalOrderMapper.TacticalIntentMapperRegistry(), new Fdp.Toolkit.Blueprints.BlueprintRegistry(), nodeBus: node);
            Assert.Same(node, pack.SimulationSystems.OfType<ExercisePauseOnEndSystem>().Single().NodeBus);
        }

        /// <summary>SR-116 end to end — a trigger ending the exercise: the pause is asked for ONCE after the end, and not again on the ticks after it.</summary>
        [Fact]
        public void PauseOnEnd_ARealEnd_AsksForThePauseOnce()
        {
            var w = new ObjWorld();
            w.Environment();
            var src = Cond(w);
            Fire(w, src, Do(Step("EndExercise", "{\"reason\":\"done\"}")));
            w.Run(2);
            Assert.Equal(0, w.Pauses);
            Flip(w, src, true); w.Run(4);
            Assert.Equal(1, w.Ended);
            Assert.Equal(1, w.Pauses);
            w.Run(4);
            Assert.Equal(1, w.Pauses);
        }
    }
}
