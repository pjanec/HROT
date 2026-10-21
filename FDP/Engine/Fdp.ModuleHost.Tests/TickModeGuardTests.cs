using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Fdp.ModuleHost.Tests;

/// <summary>
/// <b><c>CE-3293</c> S1 (R-3036) — every system and every module declares its <see cref="TickPolicy"/>; there is no default.</b>
/// 📄 <c>docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md</c> §1 rule 1.
///
/// <para>The three checks of rule 1: at registration, on the first <c>ExecuteSystem</c> of a system that was never registered (a
/// group member), and the module's at registration. ⭐ The mark is not inherited: a base class cannot hand its mode down.</para>
/// </summary>
public sealed class TickModeGuardTests
{
    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class Unmarked : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    private sealed class StepOnly : IEcsModuleSystem
    {
        public int Runs;
        public void Execute(ISimulationView view, float deltaTime) => Runs++;
    }

    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    private class MarkedBase : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    /// <summary>Derives from a marked base and declares nothing itself.</summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class DerivedUnmarked : MarkedBase { }

    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class Group : ISystemGroup
    {
        private readonly IEcsModuleSystem[] _members;
        public Group(params IEcsModuleSystem[] members) => _members = members;
        public string Name => "group";
        public IReadOnlyList<IEcsModuleSystem> GetSystems() => _members;
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    private sealed class UnmarkedModule : IEcsModule
    {
        public string Name => "unmarked";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public void Tick(ISimulationView view, float deltaTime) { }
    }

    [TickMode(TickPolicy.EveryFrame)]
    private sealed class MarkedModule : IEcsModule
    {
        public string Name => "marked";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public void Tick(ISimulationView view, float deltaTime) { }
    }

    [Fact]
    public void RegisteringAnUnmarkedSystem_Throws_NamingIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new SystemScheduler().RegisterSystem(new Unmarked()));
        Assert.Contains(nameof(Unmarked), ex.Message);
        Assert.Contains("TickMode", ex.Message);
    }

    [Fact]
    public void AMarkedSystem_RegistersWithItsPolicy()
    {
        var scheduler = new SystemScheduler();
        var system = new StepOnly();
        scheduler.RegisterSystem(system);
        Assert.Equal(TickPolicy.WhenTimeAdvances, scheduler.GetTickPolicy(system));
    }

    /// <summary>⭐ A group member never passes <c>RegisterSystem</c>; its first run is where it is checked.</summary>
    [Fact]
    public void AnUnmarkedGroupMember_ThrowsOnItsFirstRun()
    {
        var scheduler = new SystemScheduler();
        var group = new Group(new StepOnly(), new Unmarked());
        scheduler.RegisterSystem(group);   // a group forwards: it carries no mark of its own
        Assert.Equal(TickPolicy.Forwarding, scheduler.GetTickPolicy(group));
        scheduler.BuildExecutionOrders();
        var ex = Assert.Throws<InvalidOperationException>(() => scheduler.ExecutePhase(SystemPhase.Simulation, new EntityRepository(), 1f / 60f));
        Assert.Contains(nameof(Unmarked), ex.Message);
    }

    [Fact]
    public void TheMark_IsNotInherited()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new SystemScheduler().RegisterSystem(new DerivedUnmarked()));
        Assert.Contains(nameof(DerivedUnmarked), ex.Message);
    }

    /// <summary>The manual-system wrapper forwards; the system inside it carries the mark and is checked at registration.</summary>
    [Fact]
    public void AManualSystem_IsCheckedAtRegistration_AndItsWrapperForwards()
    {
        var scheduler = new SystemScheduler();
        Assert.Throws<InvalidOperationException>(() => scheduler.RegisterManualSystem(new Unmarked()));
        var wrapper = scheduler.RegisterManualSystem(new StepOnly());
        Assert.Equal(TickPolicy.Forwarding, TickModes.OfSystem(wrapper.GetType()));
    }

    [Fact]
    public void RegisteringAnUnmarkedModule_Throws_NamingIt()
    {
        using var kernel = new ModuleHostKernel(new EntityRepository(), new EventAccumulator());
        var ex = Assert.Throws<InvalidOperationException>(() => kernel.RegisterModule(new UnmarkedModule()));
        Assert.Contains(nameof(UnmarkedModule), ex.Message);
        kernel.RegisterModule(new MarkedModule());   // a marked one registers
    }

    [Fact]
    public void Forwarding_CannotBeDeclared()
        => Assert.Throws<ArgumentException>(() => new TickModeAttribute(TickPolicy.Forwarding));
}
