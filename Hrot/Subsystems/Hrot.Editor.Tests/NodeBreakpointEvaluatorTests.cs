using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.Debugging;
using StructEdit.Reflection;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐ <c>CE-1291</c> (Behaviour Debugging DBG-7) — a breakpoint on a BTree node BREAKS: a marked node is one breakpoint in the manager,
/// and when an armed unit's trace ring shows it ran this tick the manager pauses (and rewinds) — a node that did NOT run, or an unmarked
/// one, does not. 🔴 Measured before: the flag was drawn and read by nothing.
/// </summary>
public sealed class NodeBreakpointEvaluatorTests
{
    private sealed class Clock : IEngineDebugTimeController
    {
        public bool IsPausedByDebugger { get; private set; }
        public void RequestPause() => IsPausedByDebugger = true;
        public void RequestResume() => IsPausedByDebugger = false;
        public void RequestStepOneTick() { }
    }

    /// <summary>✅ Red-proof: the <c>_registered.ContainsKey(tag)</c> check made to look for a different tag ⇒ never breaks.</summary>
    [Fact]
    public void CE1291_AMarkedNodeThatRan_Breaks_AnUnmarkedOrUnrunOneDoesNot()
    {
        EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<BehaviorState>();
            w.RegisterComponent<BTreeTraceWorkingMemory1024>();
            return w;
        }
        var live = World();
        var snapshot = World();
        int hash = BehaviorHash.FromName("FightFromCover");
        var unit = live.CreateEntity();
        live.AddComponent(unit, new BehaviorState { ActiveBehaviorHash = hash });
        live.AddComponent(unit, new BTreeTraceWorkingMemory1024());

        Guid root = Guid.NewGuid(), shoot = Guid.NewGuid(), reload = Guid.NewGuid();
        var meta = new[]
        {
            new Fbt.NodeDebugMetadata { VisualId = root.ToString() },
            new Fbt.NodeDebugMetadata { VisualId = shoot.ToString() },
            new Fbt.NodeDebugMetadata { VisualId = reload.ToString() },
        };
        var manager = new DataBreakpointManager(live, snapshot, new DebugSnapshotProvider(snapshot), new Clock(),
            new PredicateCompiler(new ComponentEditServiceBuilder().Build()),
            new EventScannerCompiler(new ComponentEditServiceBuilder().Build()));
        var marked = new[] { new MarkedNode("FightFromCover", reload, "Reload") };
        var evaluator = new NodeBreakpointEvaluator(() => marked, manager, () => null, h => h == hash ? meta : null);

        void Run(params int[] nodes)
        {
            live.Tick();
            ref var trace = ref live.GetComponentRW<BTreeTraceWorkingMemory1024>(unit);
            foreach (var n in nodes) trace.WriteNodeEvaluated(n, Fbt.NodeStatus.Success, 1);
            evaluator.Execute(live, 0f);
        }

        Run();                                                   // first sight: reading starts here
        Assert.Single(evaluator.Registered);                     // one breakpoint for the one marked node
        Assert.Contains("Reload", manager.AllBreakpoints.Single().DisplayName);
        Run(0, 1);                                               // root and Shoot ran — Reload did not
        Assert.False(manager.IsPaused);
        Run(0, 2);                                               // Reload ran
        Assert.True(manager.IsPaused);

        marked = Array.Empty<MarkedNode>();                      // unmarked ⇒ its breakpoint goes
        evaluator.Execute(live, 0f);
        Assert.Empty(evaluator.Registered);
    }

    // ── CE-1323 — the HSM's five kinds (§9) ──────────────────────────────────────────────────────────────────────────────────

    private const string Machine = "SquadLeader";

    /// <summary>Patrol (initial) → Engage on Contact, twice-guarded Engage → Withdraw on Lost, a global Order → Withdraw; Engage holds
    /// Aim and Fire.</summary>
    private static (HsmDefinitionBlob Blob, MachineMetadata Meta) Squad()
    {
        var b = new HsmBuilder(Machine);
        b.Event("Contact", 1); b.Event("Lost", 2); b.Event("Order", 3);
        var patrol = b.State("Patrol").Initial();
        var engage = b.State("Engage").Child("Aim", c => c.Initial()).Child("Fire", _ => { });
        b.State("Withdraw");
        patrol.On("Contact").GoTo("Engage");
        engage.On("Lost").GoTo("Withdraw");
        b.GlobalTransition("Order", "Withdraw");
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        return (HsmEmitter.Emit(HsmFlattener.Flatten(graph)), HsmEmitter.BuildMachineMetadata(graph));
    }

    private static ushort S(MachineMetadata m, string name) => m.StateNames.First(kv => kv.Value == name).Key;

    private sealed class Rig
    {
        public readonly EntityRepository Live = World(), Snapshot = World();
        public readonly DataBreakpointManager Manager;
        public readonly NodeBreakpointEvaluator Evaluator;
        public readonly Entity A, B;
        public readonly MachineMetadata Meta;
        public readonly List<(Entity Unit, bool On)> Armed = new();

        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<BehaviorState>();
            w.RegisterComponent<HsmTraceWorkingMemory1024>();
            w.RegisterComponent<Fdp.Core.EntityInfo>();
            return w;
        }

        public Rig()
        {
            var (blob, meta) = Squad();
            Meta = meta;
            int hash = BehaviorHash.FromName(Machine);
            Manager = new DataBreakpointManager(Live, Snapshot, new DebugSnapshotProvider(Snapshot), new Clock(),
                new PredicateCompiler(new ComponentEditServiceBuilder().Build()),
                new EventScannerCompiler(new ComponentEditServiceBuilder().Build()));
            Evaluator = new NodeBreakpointEvaluator(() => Array.Empty<MarkedStep>(), Manager, () => null,
                hsmOf: h => h == hash ? (blob, meta) : null, arm: (_, u, on) => { Armed.Add((u, on)); return true; });
            foreach (var u in new[] { A = Live.CreateEntity(), B = Live.CreateEntity() })
            {
                Live.AddComponent(u, new BehaviorState { ActiveBehaviorHash = hash });
                Live.AddComponent(u, new HsmTraceWorkingMemory1024());
                Live.AddComponent(u, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Friend });
            }
            Tick(A);   // first sight: reading starts here
            Tick(B);
        }

        /// <summary>One tick on which <paramref name="unit"/>'s machine wrote <paramref name="records"/>.</summary>
        public unsafe void Tick(Entity unit, params (TraceOpCode Op, ushort A, ushort B, ushort C)[] records)
        {
            ref var t = ref Live.GetComponentRW<HsmTraceWorkingMemory1024>(unit);
            fixed (byte* buf = t.Buffer)
                foreach (var (op, a, b, c) in records)
                {
                    byte* rec = buf + t.WritePos;
                    for (int i = 0; i < 16; i++) rec[i] = 0;
                    rec[0] = (byte)op;
                    *(ushort*)(rec + 8) = a;
                    if (op == TraceOpCode.Transition) { *(ushort*)(rec + 10) = b; *(ushort*)(rec + 12) = c; }
                    else if (op == TraceOpCode.EventHandled) rec[10] = (byte)b;
                    t.WritePos = (ushort)((t.WritePos + HsmTraceWorkingMemory1024.RecordStride) % HsmTraceWorkingMemory1024.PayloadBytes);
                }
            Evaluator.Execute(Live, 0f);
        }

        public BreakpointId Add(BehaviourStepPredicateDto step, Entity? only = null)
        {
            step.Behaviour = Machine;
            return Manager.AddBreakpoint(step, only);
        }
    }

    private static (TraceOpCode, ushort, ushort, ushort) Enter(ushort s) => (TraceOpCode.StateEnter, s, 0, 0);
    private static (TraceOpCode, ushort, ushort, ushort) Exit(ushort s) => (TraceOpCode.StateExit, s, 0, 0);
    private static (TraceOpCode, ushort, ushort, ushort) Fired(ushort f, ushort t, ushort e) => (TraceOpCode.Transition, f, t, e);
    private static (TraceOpCode, ushort, ushort, ushort) Handled(ushort e, byte result = 0) => (TraceOpCode.EventHandled, e, result, 0);

    /// <summary>⭐⭐ CE-1323 — each of the five kinds stops on ITS step and says what it saw; the step it does not watch passes.
    /// ✅ Red-proofs: the StateEnter arm matched on exit too · the transition's target check dropped · the region slot filter dropped.</summary>
    [Fact]
    public void CE1323_EachKind_StopsOnItsStep_AndSaysWhatItSaw()
    {
        void Case(BehaviourStepPredicateDto step, (TraceOpCode, ushort, ushort, ushort)[] passes,
                  (TraceOpCode, ushort, ushort, ushort)[] stops, string what)
        {
            var r = new Rig();
            r.Add(step);
            r.Tick(r.A, passes);
            Assert.False(r.Manager.IsPaused, $"{step.Kind}: stopped on a step it does not watch");
            r.Tick(r.A, stops);
            Assert.True(r.Manager.IsPaused, $"{step.Kind}: did not stop");
            Assert.Equal(what, r.Manager.LastHitDetail);
        }
        var m = new Rig().Meta;
        ushort patrol = S(m, "Patrol"), engage = S(m, "Engage"), aim = S(m, "Aim"), fire = S(m, "Fire"), withdraw = S(m, "Withdraw");

        Case(new() { Kind = BehaviourStepKind.StateEnter, State = "Withdraw" },
             new[] { Exit(withdraw), Enter(engage) }, new[] { Enter(withdraw) }, "entered Withdraw");
        Case(new() { Kind = BehaviourStepKind.StateExit, State = "Patrol" },
             new[] { Enter(patrol) }, new[] { Exit(patrol) }, "left Patrol");
        Case(new() { Kind = BehaviourStepKind.InsideEnter, State = "Engage" },
             new[] { Enter(engage), Enter(withdraw) }, new[] { Enter(fire) }, "entered Fire (inside Engage)");
        Case(new() { Kind = BehaviourStepKind.TransitionFires, From = "Engage", To = "Withdraw" },
             new[] { Fired(patrol, engage, 1), Fired(engage, patrol, 2) }, new[] { Fired(engage, withdraw, 2) },
             "Engage -> Withdraw fired on Lost");
        Case(new() { Kind = BehaviourStepKind.TransitionFires, To = "Withdraw", Event = "Order" },   // a global: from any state
             new[] { Fired(aim, withdraw, 2) }, new[] { Fired(aim, withdraw, 3) }, "Aim -> Withdraw fired on Order");
        Case(new() { Kind = BehaviourStepKind.EventHandled, Event = "Lost" },
             new[] { Handled(1) }, new[] { Handled(2, 1) }, "event Lost handled (deferred)");
    }

    /// <summary>⭐⭐ CE-1323 — one arrival stops ONCE: after a continue the tick replays, the cursor is already at its end. 🔴 The
    /// whole-ring TraceBufferScan stopped again every tick while the record stayed in the ring.</summary>
    [Fact]
    public void CE1323_OneArrival_StopsOnce_NotAgainAfterAContinue()
    {
        var r = new Rig();
        r.Add(new() { Kind = BehaviourStepKind.StateEnter, State = "Engage" });
        r.Tick(r.A, Enter(S(r.Meta, "Engage")));
        Assert.True(r.Manager.IsPaused);
        r.Manager.RequestContinue();
        r.Tick(r.A);                                   // nothing new written: the record is still in the ring
        r.Tick(r.A);
        Assert.False(r.Manager.IsPaused);
    }

    /// <summary>⭐⭐ CE-1323 (DBG-11/12) — "just for a certain entity": the unit filter stops only on HIM, and arms his traces while it
    /// exists (and only what it armed is disarmed); a compound's other condition is checked on the unit at the step.
    /// ✅ Red-proofs: the evaluator's FilterEntity skip dropped · the manager's compound rest ignored.</summary>
    [Fact]
    public void CE1323_ForOneUnit_OrWithACondition()
    {
        var r = new Rig();
        var id = r.Add(new() { Kind = BehaviourStepKind.StateEnter, State = "Withdraw" }, only: r.B);
        r.Tick(r.A);
        Assert.Contains((r.B, true), r.Armed);         // his traces are on while the breakpoint is
        r.Tick(r.A, Enter(S(r.Meta, "Withdraw")));     // the other unit withdraws: no stop
        Assert.False(r.Manager.IsPaused);
        r.Manager.Remove(id);
        r.Tick(r.A);
        Assert.Contains((r.B, false), r.Armed);        // and off when it goes

        var c = new Rig();
        c.Manager.AddBreakpoint(new CompoundPredicateDto
        {
            Operator = LogicalOperator.And,
            Conditions = new()
            {
                new BehaviourStepPredicateDto { Behaviour = Machine, Kind = BehaviourStepKind.StateEnter, State = "Withdraw" },
                new PropertyMatchDto
                {
                    ComponentType = typeof(Fdp.Core.EntityInfo), PropertyPath = "ForceId", Operator = SearchOperator.Equals,
                    Predicate = new NumericPredicateDto { MinValue = 2, MaxValue = 2 },   // Hostile — they are Friend
                },
            },
        });
        c.Tick(c.A, Enter(S(c.Meta, "Withdraw")));
        Assert.False(c.Manager.IsPaused, "the compound's other condition is false on him");
        c.Live.SetComponent(c.B, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Hostile });
        c.Tick(c.B, Enter(S(c.Meta, "Withdraw")));
        Assert.True(c.Manager.IsPaused);
    }

    /// <summary>⭐ CE-1323 — a name that is not in the machine resolves to nothing, and the reason lists what is (the API answers it as a
    /// 400); an id beats a name.</summary>
    [Fact]
    public void CE1323_AWrongName_SaysWhatTheMachineHas()
    {
        var (blob, meta) = Squad();
        Assert.Null(BehaviourStepResolver.ResolveHsm(
            new BehaviourStepPredicateDto { Behaviour = Machine, Kind = BehaviourStepKind.StateEnter, State = "Retreat" }, blob, meta, out var why));
        Assert.Contains("Withdraw", why);
        Assert.Contains("Patrol", why);
        Assert.Null(BehaviourStepResolver.ResolveHsm(
            new BehaviourStepPredicateDto { Behaviour = Machine, Kind = BehaviourStepKind.EventHandled, Event = "Ambush" }, blob, meta, out why));
        Assert.Contains("Contact", why);
        var byId = BehaviourStepResolver.ResolveHsm(new BehaviourStepPredicateDto
        {
            Behaviour = Machine, Kind = BehaviourStepKind.StateEnter, State = "Retreat", Element = meta.StateStableIds[S(meta, "Patrol")],
        }, blob, meta, out _);
        Assert.Equal(new[] { S(meta, "Patrol") }, byId!.States);
    }

    /// <summary>⭐ <c>CE-1324</c> — the outcome of a BTree node step decides WHICH run stops it: activation = still Running, completion = Success or Failure, abort is a scope record (never an evaluation), Any = every run.</summary>
    [Theory]
    [InlineData(BehaviourNodeOutcome.Any, Fbt.NodeStatus.Running, true)]
    [InlineData(BehaviourNodeOutcome.Any, Fbt.NodeStatus.Failure, true)]
    [InlineData(BehaviourNodeOutcome.Running, Fbt.NodeStatus.Running, true)]
    [InlineData(BehaviourNodeOutcome.Running, Fbt.NodeStatus.Success, false)]
    [InlineData(BehaviourNodeOutcome.Completes, Fbt.NodeStatus.Success, true)]
    [InlineData(BehaviourNodeOutcome.Completes, Fbt.NodeStatus.Failure, true)]
    [InlineData(BehaviourNodeOutcome.Completes, Fbt.NodeStatus.Running, false)]
    [InlineData(BehaviourNodeOutcome.Aborted, Fbt.NodeStatus.Running, false)]
    public void CE1324_TheOutcome_DecidesWhichRunOfTheNodeStops(BehaviourNodeOutcome want, Fbt.NodeStatus status, bool matches)
        => Assert.Equal(matches, NodeBreakpointEvaluator.OutcomeMatches(want, status));
}
