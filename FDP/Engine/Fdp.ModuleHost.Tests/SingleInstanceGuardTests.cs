using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Xunit;

namespace Fdp.ModuleHost.Tests;

/// <summary>
/// <b><c>CE-165</c> — a <see cref="SingleInstanceAttribute"/> system may be registered once.</b>
///
/// <para>This is <c>B1</c> of <c>DESIGN_Subsystem_Composition_Unification.md</c> §4.1j, and it is a hard
/// prerequisite for the role-based composition that follows: a node's capability set is the UNION of its
/// roles, and a union double-counts anything two roles both carry. It is also a fix for a live defect —
/// the running <c>Hrot.Editor</c> concatenates the Brain and MuscleGround packs with no deduplication and
/// both carry <c>UnitHierarchySystem</c>.</para>
/// </summary>
public sealed class SingleInstanceGuardTests
{
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class SingletonSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    /// <summary>A second copy in a DIFFERENT phase still ticks twice per frame, so it must also throw.</summary>
    [SingleInstance]
    [UpdateInPhase(SystemPhase.Input)]
    private sealed class SingletonInInputPhase : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class OrdinarySystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    [Fact]
    public void ASingleInstanceSystemRegistersOnce()
    {
        var scheduler = new SystemScheduler();
        scheduler.RegisterSystem(new SingletonSystem());   // must not throw
    }

    [Fact]
    public void ASecondRegistrationOfASingleInstanceSystemThrows()
    {
        var scheduler = new SystemScheduler();
        scheduler.RegisterSystem(new SingletonSystem());

        var ex = Assert.Throws<InvalidOperationException>(
            () => scheduler.RegisterSystem(new SingletonSystem()));

        // The message has to point at the COMPOSITION ROOT, not at the system: the system is fine, the
        // host that registered it twice is not. A message naming only the type sends the reader to the
        // wrong file.
        Assert.Contains(nameof(SingletonSystem), ex.Message, StringComparison.Ordinal);
        Assert.Contains("COMPOSITION ROOT", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard is keyed on TYPE, not on instance identity. A composition root that fuses two role packs
    /// gets two SEPARATE instances — the packs each construct their own — so reference equality (which is
    /// what <c>ModuleHostKernel</c>'s only pre-existing duplicate check uses, on the hot-swap path) would
    /// pass both straight through. That is precisely how this defect survived.
    /// </summary>
    [Fact]
    public void TheGuardIsKeyedOnTypeNotInstanceIdentity()
    {
        var scheduler = new SystemScheduler();
        scheduler.RegisterSystem(new SingletonSystem());

        Assert.Throws<InvalidOperationException>(
            () => scheduler.RegisterSystem(new SingletonSystem()));   // a DIFFERENT instance
    }

    /// <summary>
    /// Registering into a different phase must not launder the duplicate: two instances in two phases still
    /// execute twice per frame, which is the thing the attribute forbids.
    /// </summary>
    [Fact]
    public void TheGuardLooksAcrossAllPhasesNotJustTheOneBeingRegisteredInto()
    {
        var scheduler = new SystemScheduler();
        scheduler.RegisterSystem(new SingletonSystem());          // Simulation
        scheduler.RegisterSystem(new SingletonInInputPhase());    // Input — a different type, fine

        Assert.Throws<InvalidOperationException>(
            () => scheduler.RegisterSystem(new SingletonInInputPhase()));
    }

    /// <summary>
    /// <b>The guard is OPT-IN and must stay that way.</b> Plenty of systems are legitimately registered more
    /// than once — per-arm wrappers, the editor's toggled simulation groups. Only a system that is a
    /// singleton by design carries the attribute, so an unmarked type must register freely. Without this
    /// rail the guard could be tightened into a global rule and nothing would notice until a host failed
    /// to boot.
    /// </summary>
    [Fact]
    public void AnUnmarkedSystemMayStillBeRegisteredTwice()
    {
        var scheduler = new SystemScheduler();
        scheduler.RegisterSystem(new OrdinarySystem());
        scheduler.RegisterSystem(new OrdinarySystem());   // must not throw
    }
}
