using System;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3035</c> — the SOP SLOT (R-189, R-198): a second behaviour beside the task, any tier, run by the real
    /// <see cref="BehaviorIngressSystem"/> and <see cref="BrainTickSystem"/>. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §6.
    /// </summary>
    public sealed unsafe class SopSlotTests
    {
        private const int TaskId = 0x5301, SopId = 0x5302, SopChannelId = 0x5303, SopFaultId = 0x5304, OtherSopId = 0x5305, HsmSopId = 0x5306,
                          QuickReactionId = 0x5307, LongReactionId = 0x5308, OtherTaskId = 0x5309,
                          IdleSopId = 0x530A, ReactingSopId = 0x530B;

        [ThreadStatic] private static int _taskTicks, _sopTicks, _channelTicks;
        [ThreadStatic] private static bool _wasHit;

        private static NodeStatus TaskRuns(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p) { _taskTicks++; return NodeStatus.Running; }
        private static NodeStatus SopDecides(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p) { _sopTicks++; return NodeStatus.Success; }
        private static NodeStatus SopMoves(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        {
            _channelTicks++;
            ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            loco.ActiveAction = 99;
            loco.BehaviorInstanceId = ctx.World.GetComponentRO<BehaviorState>(ctx.Self).InstanceId;   // as real actions do
            return NodeStatus.Running;
        }
        private static NodeStatus SopFaults(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        {
            BehaviorFault.Raise(ctx.World, ctx.Self, BehaviorFaultCode.Custom, "test fault");
            return NodeStatus.Running;
        }

        // ⭐ CE-2079 — SOP trees built on the two actions, as a recipe would be: one row each.
        private static NodeStatus IdleRow(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        { _sopTicks++; return SopActions.DoWhenIdle(ctx.World, ctx.Self, "SopT_Task2", "{}"); }
        private static NodeStatus ReactRow(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        {
            _sopTicks++;
            return _wasHit ? SopActions.React(ctx.World, ctx.Self, "SopT_Cover", ReactionUrgency.Hit, "{}") : NodeStatus.Failure;
        }

        private static BehaviorDefinition Tree(string name, NodeLogicDelegate<byte, BTreeContext> leaf)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(leaf));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }

        private static HsmDefinitionBlob OneStateHsm()
        {
            var states = new StateDef[1];
            states[0] = new StateDef { ParentIndex = 0xFFFF, FirstTransitionIndex = 0xFFFF, TransitionCount = 0 };
            var header = new HsmDefinitionHeader { StructureHash = 0x5306, StateCount = 1, TransitionCount = 0 };
            return new HsmDefinitionBlob(header, states, Array.Empty<TransitionDef>(), Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        private sealed class Fixture : IDisposable
        {
            public readonly EntityRepository World = TestWorldFactory.Create();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly BrainTickSystem Brain;
            public readonly Entity Unit;

            public Fixture()
            {
                BlueprintTierTable.RegisterAll(World);
                if (!World.IsComponentTypeRegistered<SopState>()) World.RegisterComponent<SopState>();
                World.Bus.Register<ClearSopEvent>();
                World.Bus.RegisterManaged<AssignSopEvent>();
                // ⭐ as production hosts do — SopActions / the resume publish only onto a REGISTERED event
                if (!World.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) World.Bus.RegisterManaged<AssignBehaviorEvent>();
                if (!World.IsComponentTypeRegistered<Roe>()) World.RegisterComponent<Roe>();
                Registry.Register(TaskId,       "SopT_Task",    Tree("SopT_Task", TaskRuns));
                Registry.Register(SopId,        "SopT_Sop",     Tree("SopT_Sop", SopDecides));
                Registry.Register(SopChannelId, "SopT_Mover",   Tree("SopT_Mover", SopMoves));
                Registry.Register(SopFaultId,   "SopT_Faulty",  Tree("SopT_Faulty", SopFaults));
                Registry.Register(OtherSopId,   "SopT_Other",   Tree("SopT_Other", SopDecides));
                Registry.Register(QuickReactionId, "SopT_Duck",   Tree("SopT_Duck", SopDecides));   // ends on its first tick
                Registry.Register(LongReactionId,  "SopT_Cover",  Tree("SopT_Cover", TaskRuns));    // runs until replaced
                Registry.Register(OtherTaskId,     "SopT_Task2",  Tree("SopT_Task2", TaskRuns));
                Registry.Register(IdleSopId,       "SopT_IdleSop",  Tree("SopT_IdleSop", IdleRow));
                Registry.Register(ReactingSopId,   "SopT_ReactSop", Tree("SopT_ReactSop", ReactRow));
                Registry.Register(HsmSopId,     "SopT_HsmSop",  new BehaviorDefinition
                {
                    Name = "SopT_HsmSop", BrainTier = BehaviorConstants.BrainTierHsm, HsmDefinition = OneStateHsm(),
                });
                Ingress = new BehaviorIngressSystem(Registry);
                Brain   = new BrainTickSystem(Registry);
                Unit    = World.CreateEntity();
                World.AddComponent(Unit, new BehaviorState());
                _taskTicks = _sopTicks = _channelTicks = 0;
                _wasHit = false;
            }

            public void Task(string name, BehaviorOrigin origin)
            {
                World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = name, JsonParams = "", Origin = origin });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }

            public void Sop(string name, BehaviorOrigin origin)
            {
                World.Bus.PublishManaged(new AssignSopEvent { Entity = Unit, BehaviorName = name, JsonParams = "{}", Origin = origin });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }

            public void React(string name, ReactionUrgency urgency)
            {
                World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = name, JsonParams = "", Origin = BehaviorOrigin.Reaction, Urgency = urgency });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }

            /// <summary>One frame in order: brain tick, then next frame's ingress sees what it published.</summary>
            public void FrameThenIngress()
            {
                Brain.Execute(World, 0.016f);
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }

            public BehaviorState Task_ => World.GetComponent<BehaviorState>(Unit);
            public PausedTask? Paused => BehaviorIngressSystem.PausedTaskOf(World, Unit);

            public void Frames(int n) { for (int i = 0; i < n; i++) Brain.Execute(World, 0.016f); }

            public SopState SopState => World.GetComponent<SopState>(Unit);

            public void Dispose() => World.Dispose();
        }

        [Fact]
        public void CE3035_TheSop_TicksBesideTheTask_SlowerAndWithoutEverFinishing()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.Sop("SopT_Sop", BehaviorOrigin.Superior);
            Assert.Equal(SopId, f.SopState.SopHash);
            Assert.True(SopTokens.IsSop(f.SopState.SopInstanceId));                    // a token no task can carry

            f.Frames(31);   // ~0.5 s at 16 ms

            Assert.Equal(31, _taskTicks);                                              // the task ran every frame, undisturbed
            Assert.InRange(_sopTicks, 2, 4);                                           // first frame (woken) + every 0.2 s
            Assert.Equal(TaskId, f.World.GetComponent<BehaviorState>(f.Unit).ActiveBehaviorHash);
            Assert.Equal(SopId, f.SopState.SopHash);                                   // Success restarted it — never finished
        }

        [Fact]
        public void CE3035_TheSop_IsGatedByWhoSetIt()
        {
            using var f = new Fixture();
            f.Sop("SopT_Sop", BehaviorOrigin.Superior);
            f.Sop("SopT_Other", BehaviorOrigin.Sop);
            Assert.Equal(SopId, f.SopState.SopHash);
            Assert.Equal(1, f.Ingress.SopRefusedCount);

            f.Sop("SopT_Other", BehaviorOrigin.Operator);
            Assert.Equal(OtherSopId, f.SopState.SopHash);

            f.World.Bus.Publish(new ClearSopEvent { Entity = f.Unit, Origin = BehaviorOrigin.Superior });
            f.World.Bus.SwapBuffers();
            f.Ingress.Execute(f.World, 0.016f);
            Assert.Equal(OtherSopId, f.SopState.SopHash);                              // a Superior cannot clear an Operator's SOP
        }

        [Fact]
        public void CE3035_TheSopAndTheTask_AreNeverTheSameBehaviour()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.Sop("SopT_Task", BehaviorOrigin.Operator);                               // the task as SOP: refused
            Assert.Equal(0, f.SopState.SopHash);

            f.Sop("SopT_Sop", BehaviorOrigin.Operator);
            f.Task("SopT_Sop", BehaviorOrigin.Operator);                               // the SOP as task: refused
            Assert.Equal(TaskId, f.World.GetComponent<BehaviorState>(f.Unit).ActiveBehaviorHash);
        }

        [Fact]
        public void CE3035_AnSopChannelWrite_IsReverted_AndStopsTheSop()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            uint task = f.World.GetComponent<BehaviorState>(f.Unit).InstanceId;
            f.World.AddComponent(f.Unit, new LocomotionChannel { ActiveAction = 5, BehaviorInstanceId = task });
            f.Sop("SopT_Mover", BehaviorOrigin.Superior);

            f.Frames(31);

            var loco = f.World.GetComponent<LocomotionChannel>(f.Unit);
            Assert.Equal(5, loco.ActiveAction);                                        // the task keeps its command
            Assert.Equal(task, loco.BehaviorInstanceId);
            Assert.Equal(1, _channelTicks);                                            // stopped after the first write
            Assert.Equal(1, f.SopState.SopFaulted);
            Assert.Equal(SopChannelId, f.SopState.SopHash);                            // stays assigned and visible (R-193)
        }

        [Fact]
        public void CE3035_AnSopFault_StopsTheSop_AndNotTheTask()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.Sop("SopT_Faulty", BehaviorOrigin.Superior);
            f.Frames(31);

            Assert.Equal(1, f.SopState.SopFaulted);
            Assert.Equal(31, _taskTicks);
            Assert.Equal(TaskId, f.World.GetComponent<BehaviorState>(f.Unit).ActiveBehaviorHash);
        }

        [Fact]
        public void CE3035_ATaskChange_DoesNotSweepTheSopsStorage()
        {
            using var f = new Fixture();
            f.Sop("SopT_HsmSop", BehaviorOrigin.Superior);                             // its instance is an Hsm-kind slot
            using (BrainSlotScope.Enter(f.Unit, HsmSopId, f.SopState.SopInstanceId))
                Assert.True(RootHsmAccess.TryGetInstance(f.World, f.Unit, out _, out _));

            f.Task("SopT_Task", BehaviorOrigin.Superior);                              // the task start sweeps Hsm-kind slots…
            f.World.Bus.Publish(new ClearBehaviorEvent { Entity = f.Unit, Origin = BehaviorOrigin.Superior });
            f.World.Bus.SwapBuffers();
            f.Ingress.Execute(f.World, 0.016f);                                        // …and so does a clear

            using (BrainSlotScope.Enter(f.Unit, HsmSopId, f.SopState.SopInstanceId))
                Assert.True(RootHsmAccess.TryGetInstance(f.World, f.Unit, out _, out _), "the task's sweep took the SOP's HSM instance");
        }
        // ── ⭐ CE-2078 — reactions in the gate (R-199, docs/DESIGN_Decision_Layer.md §4.1) ─────────────────────────────

        [Fact]
        public void CE2078_AReaction_PausesTheTask_AndTheTaskRestartsWhenItEnds()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.React("SopT_Duck", ReactionUrgency.Hit);

            Assert.Equal(QuickReactionId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Reaction, f.Task_.Origin);
            Assert.Equal(ReactionUrgency.Hit, f.Task_.Urgency);
            Assert.Equal("SopT_Task", f.Paused!.BehaviorName);
            Assert.Equal(BehaviorOrigin.Superior, f.Paused.Origin);

            f.FrameThenIngress();   // the reaction ends on its first tick ⇒ next frame the task restarts

            Assert.Equal(TaskId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Superior, f.Task_.Origin);               // it keeps its rank
            Assert.Equal(ReactionUrgency.NotAReaction, f.Task_.Urgency);
            Assert.Null(f.Paused);
        }

        [Fact]
        public void CE2078_StayOnTask_RefusesEveryReaction()
        {
            using var f = new Fixture();
            f.World.AddComponent(f.Unit, new Roe { Reactions = RoeReactions.StayOnTask, SetBy = BehaviorOrigin.Superior });
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.React("SopT_Duck", ReactionUrgency.Hit);

            Assert.Equal(TaskId, f.Task_.ActiveBehaviorHash);
            Assert.Null(f.Paused);
            Assert.Equal(1, f.Ingress.RefusedCount);
        }

        [Fact]
        public void CE2078_ARunningReaction_YieldsOnlyToAMoreUrgentOne_AndTheFirstPauseStays()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Operator);
            f.React("SopT_Cover", ReactionUrgency.Contact);
            f.React("SopT_Duck", ReactionUrgency.Contact);                    // same: refused
            f.React("SopT_Duck", ReactionUrgency.Alert);                      // lower: refused
            Assert.Equal(LongReactionId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(2, f.Ingress.RefusedCount);

            f.React("SopT_Duck", ReactionUrgency.UnderFire);                  // more urgent: replaces it
            Assert.Equal(QuickReactionId, f.Task_.ActiveBehaviorHash);
            Assert.Equal("SopT_Task", f.Paused!.BehaviorName);                // ④ never stacked: still the TASK
            Assert.Equal(BehaviorOrigin.Operator, f.Paused.Origin);
        }

        [Fact]
        public void CE2078_DuringAReaction_AnOrderIsWeighedAgainstThePausedTask_AndTheIdleChoiceWaits()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Operator);
            f.React("SopT_Cover", ReactionUrgency.Hit);

            f.Task("SopT_Task2", BehaviorOrigin.Sop);                         // the SOP's idle choice: refused
            f.Task("SopT_Task2", BehaviorOrigin.Superior);                    // cannot end an Operator's task: refused
            Assert.Equal(LongReactionId, f.Task_.ActiveBehaviorHash);
            Assert.NotNull(f.Paused);

            f.Task("SopT_Task2", BehaviorOrigin.Operator);                    // may: replaces the reaction AND the task
            Assert.Equal(OtherTaskId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Operator, f.Task_.Origin);
            Assert.Null(f.Paused);

            f.FrameThenIngress();                                             // nothing comes back
            Assert.Equal(OtherTaskId, f.Task_.ActiveBehaviorHash);
        }

        [Fact]
        public void CE2078_AReactionOverTheIdleChoice_PausesNothing_AndAReactionThatClearsItself_RestartsTheTask()
        {
            using var f = new Fixture();
            f.Task("SopT_Task2", BehaviorOrigin.Sop);                         // the SOP's idle choice
            f.React("SopT_Cover", ReactionUrgency.Alert);
            Assert.Equal(LongReactionId, f.Task_.ActiveBehaviorHash);
            Assert.Null(f.Paused);                                            // the SOP chooses again when it wakes

            f.Task("SopT_Task", BehaviorOrigin.Superior);                     // an order ends the reaction
            f.React("SopT_Cover", ReactionUrgency.Alert);                     // a new one pauses THAT task
            f.World.Bus.Publish(new ClearBehaviorEvent { Entity = f.Unit, Origin = BehaviorOrigin.Self });
            f.World.Bus.SwapBuffers();
            f.Ingress.Execute(f.World, 0.016f);                               // the reaction ends itself…
            f.World.Bus.SwapBuffers();
            f.Ingress.Execute(f.World, 0.016f);                               // …and the task is back
            Assert.Equal(TaskId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Superior, f.Task_.Origin);
        }

        [Fact]
        public void CE2078_AReactionsFinish_SaysItWasAReaction()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.React("SopT_Duck", ReactionUrgency.Hit);
            f.Brain.Execute(f.World, 0.016f);
            f.World.Bus.SwapBuffers();

            var origins = new System.Collections.Generic.List<BehaviorOrigin>();
            foreach (var evt in f.World.Bus.Read<BehaviorFinishedEvent>()) origins.Add(evt.Origin);
            Assert.Equal(new[] { BehaviorOrigin.Reaction }, origins);         // the mission skips it (MissionDirectorSystemTests)
        }
        // ── ⭐ CE-2079 — the two SOP actions (docs/DESIGN_Decision_Layer.md §4.6) ──────────────────────────────────────

        [Fact]
        public void CE2079_DoWhenIdle_StartsTheIdleChoiceOnce_AndDoesNotRestartItEveryWake()
        {
            using var f = new Fixture();
            f.Sop("SopT_IdleSop", BehaviorOrigin.Superior);
            f.FrameThenIngress();

            Assert.Equal(OtherTaskId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Sop, f.Task_.Origin);
            uint run = f.Task_.InstanceId;

            for (int i = 0; i < 31; i++) f.FrameThenIngress();                  // ~0.5 s: the SOP re-reads its tree twice
            Assert.True(_sopTicks >= 3);
            Assert.Equal(run, f.Task_.InstanceId);                             // same run — never re-published
        }

        [Fact]
        public void CE2079_DoWhenIdle_UnderAnOrder_PublishesNothing_SoTheSopDoesNotWakeItself()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.Sop("SopT_IdleSop", BehaviorOrigin.Superior);
            for (int i = 0; i < 31; i++) f.FrameThenIngress();

            Assert.Equal(TaskId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(0, f.Ingress.RefusedCount);                           // nothing reached the gate to be refused
            Assert.InRange(_sopTicks, 2, 4);                                   // every 0.2 s, not every frame (R-195 wake loop)
        }

        [Fact]
        public void CE2079_React_PausesTheTask_AndIsNotRepeatedWhileTheReactionRuns()
        {
            using var f = new Fixture();
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.Sop("SopT_ReactSop", BehaviorOrigin.Superior);
            f.FrameThenIngress();
            Assert.Equal(TaskId, f.Task_.ActiveBehaviorHash);                  // not hit: the row fails, nothing happens

            _wasHit = true;
            f.World.GetComponentRW<SopState>(f.Unit).SopWake = 1;              // a sensing change wakes it (R-195)
            f.FrameThenIngress();
            Assert.Equal(LongReactionId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Reaction, f.Task_.Origin);
            Assert.Equal("SopT_Task", f.Paused!.BehaviorName);
            uint run = f.Task_.InstanceId;

            for (int i = 0; i < 31; i++) f.FrameThenIngress();                 // still "hit" on every wake
            Assert.Equal(run, f.Task_.InstanceId);                             // the reaction was not restarted
            Assert.Equal(0, f.Ingress.RefusedCount);
            // ⭐ and the row SUCCEEDS while its reaction runs — a Selector stops there instead of falling to lower rows
            using (BrainSlotScope.Enter(f.Unit, ReactingSopId, f.SopState.SopInstanceId))
                Assert.Equal(NodeStatus.Success, SopActions.React(f.World, f.Unit, "SopT_Cover", ReactionUrgency.Hit, "{}"));
        }

        [Fact]
        public void CE2079_React_UnderStayOnTask_IsAFailure_AndPublishesNothing()
        {
            using var f = new Fixture();
            f.World.AddComponent(f.Unit, new Roe { Reactions = RoeReactions.StayOnTask, SetBy = BehaviorOrigin.Superior });
            f.Task("SopT_Task", BehaviorOrigin.Superior);
            f.Sop("SopT_ReactSop", BehaviorOrigin.Superior);
            _wasHit = true;
            using (BrainSlotScope.Enter(f.Unit, ReactingSopId, f.SopState.SopInstanceId))
                Assert.Equal(NodeStatus.Failure,
                    SopActions.React(f.World, f.Unit, "SopT_Cover", ReactionUrgency.Hit, "{}"));   // a Selector tries its next row
            for (int i = 0; i < 31; i++) f.FrameThenIngress();

            Assert.Equal(TaskId, f.Task_.ActiveBehaviorHash);
            Assert.Equal(0, f.Ingress.RefusedCount);
        }

        private struct CoverParams
        {
            public float Radius;
            public int Rounds;
            public Fdp.Toolkit.Replication.EntityRef Threat;
            public FixedString32 Posture;
            public ReactionUrgency Level;
        }

        [Fact]
        public void CE2079_Params_RoundTrip_ThroughTheCuratedParsePath()
        {
            var sent = new CoverParams { Radius = 12.5f, Rounds = 3, Threat = new Fdp.Toolkit.Replication.EntityRef(77), Posture = "prone", Level = ReactionUrgency.UnderFire };
            string json = SopActions.ToJson(sent);

            // ⭐ the curated parse: FromBlockResolver deserialises with BehaviorParams.JsonOptions (identity resolve)
            var parse = BehaviorParams.FromBlockResolver<CoverParams, CoverParams>((in CoverParams a, ref CoverParams b, EntityRepository w, Entity e) => b = a);
            CoverParams got = default;
            using var world = TestWorldFactory.Create();
            parse(json, (byte*)&got, sizeof(CoverParams), world, default);

            Assert.Equal(sent.Radius, got.Radius);
            Assert.Equal(sent.Rounds, got.Rounds);
            Assert.Equal(sent.Threat, got.Threat);
            Assert.Equal(sent.Posture, got.Posture);
            Assert.Equal(sent.Level, got.Level);
        }
    }
}
