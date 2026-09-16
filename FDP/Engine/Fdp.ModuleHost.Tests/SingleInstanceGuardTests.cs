using System;
using System.Linq;
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

/// <summary>
/// <b><c>B2</c> — the shared one-system module and the shared fuse.</b>
///
/// <para>Both replace per-host copies: <c>SimHostModule</c> (six call sites) and
/// <c>IgUnitHierarchyModule</c> (one) were byte-for-byte twins differing only in their <c>Name</c>, and
/// four composition roots had each hand-rolled the same "fuse two role lists, first wins" loop.</para>
/// </summary>
public sealed class SharedCompositionPrimitivesTests
{
    [UpdateInPhase(SystemPhase.Simulation)]
    private sealed class SimSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    [UpdateInPhase(SystemPhase.Input)]
    private sealed class InputSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime) { }
    }

    /// <summary>
    /// B2's acceptance: the hosts still register their systems <b>in the same phases</b>. The wrapper must
    /// stay transparent — it exists only because <c>RegisterGlobalSystem</c> rejects Simulation, so if it
    /// ever influenced placement it would be doing more than its one job.
    /// </summary>
    [Fact]
    public void TheSharedModuleRegistersItsSystemIntoTheSystemsOwnPhase()
    {
        var scheduler = new SystemScheduler();
        var module    = new SingleSystemModule("NetworkSpawning", new SimSystem());

        module.RegisterSystems(scheduler);
        scheduler.BuildExecutionOrders();

        // GetAllProfileData is the existing public per-phase view; no new accessor was added for a test.
        var byPhase = scheduler.GetAllProfileData();

        Assert.Equal("NetworkSpawning", module.Name);
        Assert.Contains(byPhase[SystemPhase.Simulation], e => e.System is SimSystem);
        Assert.False(byPhase.TryGetValue(SystemPhase.Input, out var input)
                     && input.Exists(e => e.System is SimSystem));
    }

    [Fact]
    public void TheSharedModuleRefusesToBeNameless()
    {
        Assert.Throws<ArgumentException>(() => new SingleSystemModule(" ", new SimSystem()));
        Assert.Throws<ArgumentNullException>(() => new SingleSystemModule("x", null!));
    }

    /// <summary>First wins, order preserved — the behaviour every hand-rolled loop already had.</summary>
    [Fact]
    public void TheSharedFuseKeepsTheFirstOfEachTypeAndPreservesOrder()
    {
        var brainOnly  = new SimSystem();
        var shared     = new InputSystem();   // stands in for UnitHierarchySystem: carried by BOTH packs
        var muscleCopy = new InputSystem();   // the SECOND instance a plain Concat would register
        var muscleOnly = new SimSystem();

        var fused = SystemComposition
            .DistinctByType(new IEcsModuleSystem[] { brainOnly, shared },
                            new IEcsModuleSystem[] { muscleCopy, muscleOnly })
            .ToList();

        Assert.Equal(2, fused.Count);                 // one per TYPE, not per instance
        Assert.Same(brainOnly, fused[0]);             // order preserved
        Assert.Same(shared,    fused[1]);             // the BRAIN copy survived, not the muscle one
        Assert.DoesNotContain(muscleCopy, fused);
        Assert.DoesNotContain(muscleOnly, fused);     // its type was already taken by brainOnly
    }

    [Fact]
    public void TheSharedFuseToleratesNullSequencesAndNullMembers()
    {
        var only = new SimSystem();

        var fused = SystemComposition
            .DistinctByType(null!, new IEcsModuleSystem[] { null!, only })
            .ToList();

        Assert.Single(fused);
        Assert.Same(only, fused[0]);
    }
}
