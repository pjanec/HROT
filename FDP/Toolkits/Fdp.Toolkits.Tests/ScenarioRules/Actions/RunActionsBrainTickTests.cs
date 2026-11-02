using System;
using System.Collections.Generic;
using System.Reflection;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.ScenarioRules.Actions;
using Xunit;

namespace Fdp.Toolkit.ScenarioRules.Tests.Actions
{
    /// <summary>
    /// ⭐ rules S2-d (SR-98) — <c>RunActions</c> called from a REAL behaviour: <c>BrainTickSystem</c>, the store provisioned by INGRESS, not by hand
    /// (the PoC's P4 rails). The slot-kind finding is flipped into <c>StepRunnerTests.CE4161_*</c>.
    /// </summary>
    public sealed unsafe class RunActionsBrainTickTests
    {
        private static readonly string ThreeSteps = ActionKit.Steps(
            ActionKit.Step("TestRecord", "{\"value\":1}"), ActionKit.Step("TestWaitTicks", "{\"ticks\":3}"), ActionKit.Step("TestRecord", "{\"value\":2}"));

        // ══ P4 — CALLED FROM A REAL BEHAVIOUR (BrainTickSystem; the store is provisioned by INGRESS, not by hand) ═════

        private const int BtOkId = 0x7C01, BtFailId = 0x7C02, BtOtherId = 0x7C03;
        [ThreadStatic] private static NodeStatus _leafLast;
        [ThreadStatic] private static int _leafCalls, _otherTicks;
        [ThreadStatic] private static bool _viewWasActive;
        [ThreadStatic] private static RunActionsState _leafFinal;

        private static bool OccurrenceTickViewActive()
        {
            var f = typeof(OccurrenceStoreAccess).GetField("t_view", BindingFlags.NonPublic | BindingFlags.Static)!;
            var v = f.GetValue(null);
            return v != null && (bool)v.GetType().GetField("Active")!.GetValue(v)!;
        }

        private static NodeStatus ListLeaf(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p)
        {
            _viewWasActive |= OccurrenceTickViewActive();
            _leafLast = ActionHost.Tick(ctx.World, ctx.Self, 0, out _leafFinal);
            _leafCalls++;
            return _leafLast;
        }
        private static NodeStatus Idle(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p) { _otherTicks++; return NodeStatus.Running; }

        private static BehaviorDefinition SeqThenIdle(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(ListLeaf).Action(Idle));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }
        private static BehaviorDefinition SelThenIdle(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Selector(sel => sel.Action(ListLeaf).Action(Idle));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }
        private static BehaviorDefinition JustIdle(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(Idle));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree,
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }

        private sealed class BtFixture : IDisposable
        {
            public readonly EntityRepository World = ActionKit.NewWorld();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly BrainTickSystem Brain;
            public readonly Entity Unit;

            public BtFixture(string list)
            {
                if (!World.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) World.Bus.RegisterManaged<AssignBehaviorEvent>();
                Registry.Register(BtOkId,    "BtOk",    SeqThenIdle("BtOk"));
                Registry.Register(BtFailId,  "BtFail",  SelThenIdle("BtFail"));
                Registry.Register(BtOtherId, "BtOther", JustIdle("BtOther"));
                Ingress = new BehaviorIngressSystem(Registry);
                Brain   = new BrainTickSystem(Registry);
                Unit    = World.CreateEntity();
                World.AddComponent(Unit, new BehaviorState());
                World.AddComponent(Unit, new ActionTestLog());
                World.AddComponent(Unit, new ActionFailure { Step = -1 });
                World.SetManagedComponent(Unit, new ActionArgs(new[] { list }));
                _leafLast = NodeStatus.Running; _leafCalls = _otherTicks = 0; _viewWasActive = false; _leafFinal = default;
                Trap.Reset();
            }

            public void Assign(string name)
            {
                World.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Unit, BehaviorName = name, JsonParams = "", Origin = BehaviorOrigin.Superior });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }
            public void Frames(int n) { for (int i = 0; i < n; i++) Brain.Execute(World, 0.016f); }
            public void Dispose() => World.Dispose();
        }

        [Fact]
        public void RunActionsAsAnodeOfARealBTree_RunsTheListThroughBrainTick_InsideTheAmbientTickView()
        {
            using var f = new BtFixture(ThreeSteps);
            f.Assign("BtOk");
            Assert.True(OccurrenceStoreAccess.HasStore(f.World, f.Unit));          // the store came from INGRESS (nothing added by hand)

            f.Frames(1);                                                            // record 1, wait 1/3  → Running
            Assert.Equal(NodeStatus.Running, _leafLast);
            Assert.Equal(1, f.World.GetComponentRO<ActionTestLog>(f.Unit).Count);
            f.Frames(2);                                                            // wait 3/3, record 2 → Success
            Assert.Equal(NodeStatus.Success, _leafLast);
            var log = f.World.GetComponentRO<ActionTestLog>(f.Unit);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));
            Assert.True(_viewWasActive, "the leaf did not run inside OccurrenceStoreAccess.BeginTickView");   // ← the premise: inner attach happens INSIDE the ambient view
            f.Frames(2);
            Assert.True(_otherTicks >= 1);                                          // the sequence moved on to the next node: the list's Success propagated
            Assert.Equal(3, _leafCalls);                                            // and the list did not run again
        }

        [Fact]
        public void AFailingList_FailsTheBTreeNode_AndTheSelectorFallsThrough()
        {
            using var f = new BtFixture(ActionKit.Steps(ActionKit.Step("TestRecord", "{\"value\":1}"), ActionKit.Step("TestFail", "{\"code\":6}")));
            f.Assign("BtFail");
            f.Frames(1);
            Assert.Equal(NodeStatus.Failure, _leafLast);
            Assert.Equal((1, 1), (_leafFinal.Failed, _leafFinal.FailedStep));
            Assert.Equal((1, "code 6"), (f.World.GetComponentRO<ActionFailure>(f.Unit).Step, f.World.GetComponentRO<ActionFailure>(f.Unit).Reason.ToString()));
            Assert.True(_otherTicks >= 1);                                          // the selector took its next branch in the same tick
        }

        [Fact]
        public void TheStepSlotsOfALongList_AppendBlocksInsideTheTick_AndNothingIsLost()
        {
            // wide steps, one per tick: whatever the ingress-sized first block is, the lazily attached slots must fit or append
            // 📐 measured: ingress sizes the unit's first block EXACTLY (BlueprintBlackboard256, 3 slots / 176 B payload = root cursor + outer + ONE step),
            //   so thin steps swap through the one free index and never append. A FAT step (> 176 B) must append inside the tick.
            var steps = new List<string>();
            for (int i = 0; i < 4; i++) { steps.Add(ActionKit.Step("TestFat", "{\"n\":" + (i + 1) + ",\"a\":\"x\"}")); steps.Add(ActionKit.Step("TestWaitTicks", "{\"ticks\":2}")); }
            using var f = new BtFixture(ActionKit.Steps(steps.ToArray()));
            f.Assign("BtOk");
            int blocksBefore = ActionHost.BlockCount(f.World, f.Unit);
            Assert.Equal(1, blocksBefore);
            f.Frames(1);
            Assert.Equal(2, ActionHost.BlockCount(f.World, f.Unit));                  // ← the append happened INSIDE BrainTick's ambient view, under the outer's live ref
            OccurrenceStoreAccess.TryFindSlot(f.World, f.Unit, ActionHost.OuterSlotKey, out byte* ob, out int oo, out _);
            nint outerAddr = (nint)(ob + oo);
            f.Frames(1);
            OccurrenceStoreAccess.TryFindSlot(f.World, f.Unit, ActionHost.OuterSlotKey, out byte* ob2, out int oo2, out _);
            Assert.Equal(outerAddr, (nint)(ob2 + oo2));                             // the outer's slot did not move across the append / detach / re-attach
            f.Frames(6);
            Assert.Equal(NodeStatus.Success, _leafLast);
            var log = f.World.GetComponentRO<ActionTestLog>(f.Unit);
            Assert.Equal(4, log.Count);                                             // each fat step logged exactly once
            for (int i = 0; i < 4; i++) Assert.Equal(i + 1, log.At(i));
            Assert.Equal(0, Trap.Moved);
            Assert.Equal(2, ActionHost.BlockCount(f.World, f.Unit));                  // one appended block, reused by every later fat step
        }

        [Fact]
        public void ABehaviourChangeMidStep_SweepsTheOrphanedStepSlot_BecauseItsKindIsHsm()
        {
            using var f = new BtFixture(ActionKit.Steps(ActionKit.Step("TestWaitTicks", "{\"ticks\":50}")));
            f.Assign("BtOk");
            f.Frames(2);
            Assert.True(ActionHost.Holds(f.World, f.Unit, StepRunner.SlotKey(0)));
            Assert.True(ActionHost.Holds(f.World, f.Unit, ActionHost.OuterSlotKey));

            f.Assign("BtOther");                                                 // the task is REPLACED while the step is mid-flight
            Assert.False(ActionHost.Holds(f.World, f.Unit, StepRunner.SlotKey(0)), "the orphaned step slot leaked");
            Assert.False(ActionHost.Holds(f.World, f.Unit, ActionHost.OuterSlotKey), "the orphaned outer slot leaked");
        }
    }
}
