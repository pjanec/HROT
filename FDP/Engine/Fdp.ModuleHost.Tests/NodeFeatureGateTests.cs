using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Fdp.ModuleHost.Tests;

/// <summary>
/// ⭐ Node Features S1 (b) / F-13 / F-18 — a <c>[NodeFeature]</c> system (or a system of a <c>[NodeFeature]</c> module) is read once
/// beside <c>[TickMode]</c> and skipped while its feature is off; an undeclared id throws at registration.
/// 📄 docs/DESIGN_Node_Features.md §4 F-13.
/// </summary>
public sealed class NodeFeatureGateTests
{
    private sealed class Gate : IFeatureGate
    {
        public readonly Dictionary<string, bool> State = new();
        public bool On(FeatureHandle f) => State[_ids[f.Index]];
        private readonly List<string> _ids = new();
        public FeatureHandle Resolve(string id)
        {
            if (!State.ContainsKey(id)) throw new InvalidOperationException("undeclared " + id);
            int i = _ids.IndexOf(id); if (i < 0) { _ids.Add(id); i = _ids.Count - 1; }
            return new FeatureHandle(i);
        }
    }

    [UpdateInPhase(SystemPhase.Simulation), TickMode(TickPolicy.EveryFrame), NodeFeature("hud.x")]
    private sealed class Featured : IEcsModuleSystem
    {
        public int Runs;
        public void Execute(ISimulationView view, float deltaTime) => Runs++;
    }

    [UpdateInPhase(SystemPhase.Simulation), TickMode(TickPolicy.EveryFrame)]
    private sealed class Plain : IEcsModuleSystem
    {
        public int Runs;
        public void Execute(ISimulationView view, float deltaTime) => Runs++;
    }

    [UpdateInPhase(SystemPhase.Simulation), TickMode(TickPolicy.EveryFrame), NodeFeature("nope")]
    private sealed class Undeclared : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    [TickMode(TickPolicy.EveryFrame), NodeFeature("hud.x")]
    private sealed class FeaturedModule : IEcsModule
    {
        public string Name => "m";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public void Tick(ISimulationView view, float deltaTime) { }
    }

    private static void Run(SystemScheduler s, IEcsModuleSystem sys) { s.BuildExecutionOrders(); s.ExecutePhase(SystemPhase.Simulation, new EntityRepository(), 0.1f); }

    [Fact]
    public void AFeatureSystemIsSkippedWhileOff_AndRunsWhenOn_APlainOneAlwaysRuns()
    {
        var gate = new Gate(); gate.State["hud.x"] = false;
        var sch = new SystemScheduler { FeatureGate = gate };
        var f = new Featured(); var p = new Plain();
        sch.RegisterSystem(f); sch.RegisterSystem(p);
        Run(sch, f);
        Assert.Equal(0, f.Runs); Assert.Equal(1, p.Runs);
        gate.State["hud.x"] = true;
        Run(sch, f);
        Assert.Equal(1, f.Runs); Assert.Equal(2, p.Runs);
    }

    [Fact]
    public void AModulesFeatureIsInheritedByItsSystems()
    {
        var gate = new Gate(); gate.State["hud.x"] = false;
        var sch = new SystemScheduler { FeatureGate = gate };
        var p = new Plain();
        sch.RegisterSystem(p, typeof(FeaturedModule));
        Run(sch, p);
        Assert.Equal(0, p.Runs);
        gate.State["hud.x"] = true; Run(sch, p);
        Assert.Equal(1, p.Runs);
    }

    [Fact]
    public void AnUndeclaredFeatureId_ThrowsAtRegistration_AndSoDoesNoGate()
    {
        var gate = new Gate();
        Assert.Throws<InvalidOperationException>(() => new SystemScheduler { FeatureGate = gate }.RegisterSystem(new Undeclared()));
        Assert.Throws<InvalidOperationException>(() => new SystemScheduler().RegisterSystem(new Featured()));
    }
    // ⭐ CE-4294 — a [NodeFeature] system INSIDE a group. The kernel walks every group's members for their step readers before the
    //   first frame (RegisterStepReaders); that walk used to record a member's tick policy alone, so its feature was never read.

    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class Group : ISystemGroup
    {
        private readonly IEcsModuleSystem[] _members;
        public Group(params IEcsModuleSystem[] members) => _members = members;
        public string Name => "group";
        public IReadOnlyList<IEcsModuleSystem> GetSystems() => _members;
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    [UpdateInPhase(SystemPhase.Simulation), TickMode(TickPolicy.WhenTimeAdvances), NodeFeature("hud.x")]
    private sealed class FeaturedStep : IEcsModuleSystem
    {
        public int Runs;
        public void Execute(ISimulationView view, float deltaTime) => Runs++;
    }

    [Theory]
    [InlineData(true)]    // the kernel's order: step readers registered before the first frame
    [InlineData(false)]   // no step readers: the member is first seen at its first run
    public void CE4294_AFeatureSystemInsideAGroup_IsSkippedWhileOff_AndRunsWhenOn(bool stepReadersFirst)
    {
        // FdpConfig.TickModeGate is ON by default (FdpConfig.cs:138); with it off the step-reader walk is skipped and only the
        // first-run path is exercised - both rows still hold.
        {
            var gate = new Gate(); gate.State["hud.x"] = false;
            var sch = new SystemScheduler { FeatureGate = gate };
            var every = new Featured(); var step = new FeaturedStep(); var plain = new Plain();
            sch.RegisterSystem(new Group(every, step, plain));
            if (stepReadersFirst) sch.RegisterStepReaders(new EventAccumulator());
            Run(sch, every);
            Assert.Equal(0, every.Runs); Assert.Equal(0, step.Runs); Assert.Equal(1, plain.Runs);
            gate.State["hud.x"] = true;
            Run(sch, every);
            Assert.Equal(1, every.Runs); Assert.Equal(1, step.Runs); Assert.Equal(2, plain.Runs);
            gate.State["hud.x"] = false;
            Run(sch, every);
            Assert.Equal(1, every.Runs); Assert.Equal(1, step.Runs); Assert.Equal(3, plain.Runs);
        }
    }

    [Fact]
    public void CE4294_AnUndeclaredFeatureInsideAGroup_ThrowsBeforeTheFirstFrame()
    {
        if (!FdpConfig.TickModeGate) return;   // only under FDP_TICKMODE_GATE=0 (a bisect): the step-reader walk does not run at all
        var sch = new SystemScheduler { FeatureGate = new Gate() };
        sch.RegisterSystem(new Group(new Plain(), new Undeclared()));
        Assert.Throws<InvalidOperationException>(() => sch.RegisterStepReaders(new EventAccumulator()));
    }
}
