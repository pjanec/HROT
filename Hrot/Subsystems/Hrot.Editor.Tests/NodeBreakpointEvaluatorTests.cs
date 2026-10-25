using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.Debugging;
using StructEdit.Reflection;
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
}
