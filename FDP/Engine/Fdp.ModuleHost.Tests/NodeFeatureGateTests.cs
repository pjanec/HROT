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
}
