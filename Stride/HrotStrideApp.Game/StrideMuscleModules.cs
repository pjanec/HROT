#nullable enable
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Modules;
using Fdp.Toolkit.Navigation.Systems;
using Hrot.CGF.Systems;
using Hrot.Common.Systems;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Systems.Routing;
using Hrot.Stride.Core;

namespace HrotStrideApp;

// Stage 3 seam: MuscleModuleContext lives in Hrot.Editor which is already referenced
// by HrotStrideApp.Game (see HrotStrideApp.Game.csproj). The using below makes
// ToEditorModuleList() visible without a separate adapter project.
using Hrot.Editor;

/// <summary>
/// <b>StrideMuscleModules</b> — reusable factory for the kernel-resident Stride muscle
/// module set (STR-P1, BATCH refactor).
///
/// <para>
/// Returns the list of <see cref="IEcsModule"/>s that run INSIDE the
/// <see cref="Fdp.ModuleHost.ModuleHostKernel"/> for the Stride composition:
/// <list type="bullet">
///   <item><see cref="StrideKinematicsModule"/> — spatial hash, formation, vehicle command,
///     navigation execution, crowd-agent steering (no FDP integrators).</item>
///   <item><see cref="CombatModule"/> — fire processing, raycast solver, hit resolution,
///     ballistics post-sim.</item>
///   <item><see cref="DamageAssessmentModule"/> — damage-assessment sim systems.</item>
///   <item>Navigation-bridge systems: <see cref="NavigationIntentBridgeSystem"/>,
///     <see cref="RouteTrajectorySyncSystem"/>.</item>
///   <item><see cref="VehicleNavigationIntentSystem"/> — vehicle navmesh navigation intent.</item>
///   <item><see cref="UnitHierarchySystem"/>, <see cref="EqsResultUpdateSystem"/>.</item>
///   <item><see cref="PersonalRouteAuthoringSystem"/> (input-phase).</item>
/// </list>
/// These are exactly the systems registered on the kernel by
/// <see cref="EditorStrideSubsystem.Initialize"/> steps 7–7c (preserved verbatim).
/// </para>
///
/// <para>
/// The returned <see cref="IReadOnlyList{T}"/> is framework-agnostic and does NOT reference
/// <c>Hrot.Editor</c>'s <c>MuscleModuleContext</c>.  A later stage (Stage 3) will adapt it
/// to <c>EditorSubsystem.MuscleModuleFactory</c> (type
/// <c>Func&lt;MuscleModuleContext, IReadOnlyList&lt;IEcsModule&gt;&gt;</c>).
/// </para>
/// </summary>
public static class StrideMuscleModules
{
    /// <summary>
    /// Builds the kernel-resident Stride muscle module set and returns the individual
    /// module and system objects for registration on the kernel.
    /// </summary>
    /// <param name="deferredCrowd">
    /// The DotRecast crowd provider injected into <see cref="StrideKinematicsModule"/> and
    /// <see cref="NavigationIntentBridgeSystem"/>.  Starts in no-op mode; initialized with
    /// the real navmesh by <c>StrideHrotGame.BakeNavmesh</c> after <c>BeginRun</c>.
    /// </param>
    /// <returns>
    /// A <see cref="StrideMuscleModuleSet"/> containing all the constructed objects needed
    /// for kernel registration and for wiring the physics bracket.
    /// </returns>
    public static StrideMuscleModuleSet Build(DotRecastDtCrowdProvider deferredCrowd)
    {
        var strideKinematics    = new StrideKinematicsModule(dtCrowd: deferredCrowd);
        var combatModule        = new CombatModule();
        var damageModule        = new DamageAssessmentModule();
        var navIntentBridge     = new NavigationIntentBridgeSystem(
                                      strideKinematics.TrajectoryPool, deferredCrowd);
        var routeTrajSync       = new RouteTrajectorySyncSystem(strideKinematics.TrajectoryPool);
        var personalRoute       = new PersonalRouteAuthoringSystem();
        var vehicleNavIntent    = new VehicleNavigationIntentSystem();

        return new StrideMuscleModuleSet(
            strideKinematics,
            combatModule,
            damageModule,
            navIntentBridge,
            routeTrajSync,
            personalRoute,
            vehicleNavIntent);
    }
}

/// <summary>
/// Value object returned by <see cref="StrideMuscleModules.Build"/>.
/// Holds all constructed objects so the caller can register them on the kernel and
/// wire them into the physics bracket without repeated construction.
/// </summary>
public sealed class StrideMuscleModuleSet
{
    /// <summary>The Stride kinematics module (no FDP integrators).</summary>
    public StrideKinematicsModule StrideKinematics { get; }

    /// <summary>The combat module (fire/raycast/hit/ballistics).</summary>
    public CombatModule Combat { get; }

    /// <summary>The damage-assessment module.</summary>
    public DamageAssessmentModule Damage { get; }

    /// <summary>
    /// Navigation intent bridge system — auto-registers infantry crowd agents on MoveTo.
    /// </summary>
    public NavigationIntentBridgeSystem NavIntentBridge { get; }

    /// <summary>Route-trajectory sync system.</summary>
    public RouteTrajectorySyncSystem RouteTrajSync { get; }

    /// <summary>Personal route authoring system (input-phase).</summary>
    public PersonalRouteAuthoringSystem PersonalRoute { get; }

    /// <summary>
    /// Vehicle navigation intent system — navmesh navigation for vehicle entities
    /// driven by <c>NavigationIntent</c>. Also wired into
    /// <see cref="Hrot.Stride.Core.StridePhysicsBracket.VehicleNavIntentSystem"/> for the
    /// pre-kernel STR-D21 double-execute (idempotent).
    /// </summary>
    public VehicleNavigationIntentSystem VehicleNavIntent { get; }

    internal StrideMuscleModuleSet(
        StrideKinematicsModule         strideKinematics,
        CombatModule                   combat,
        DamageAssessmentModule         damage,
        NavigationIntentBridgeSystem   navIntentBridge,
        RouteTrajectorySyncSystem      routeTrajSync,
        PersonalRouteAuthoringSystem   personalRoute,
        VehicleNavigationIntentSystem  vehicleNavIntent)
    {
        StrideKinematics = strideKinematics;
        Combat           = combat;
        Damage           = damage;
        NavIntentBridge  = navIntentBridge;
        RouteTrajSync    = routeTrajSync;
        PersonalRoute    = personalRoute;
        VehicleNavIntent = vehicleNavIntent;
    }

    // ── Stage-3 seam: MuscleModuleFactory adapter ─────────────────────────────────────
    //
    // Converts this StrideMuscleModuleSet into a single IEcsModule suitable for injection
    // into EditorSubsystem.MuscleModuleFactory (type Func<MuscleModuleContext, IReadOnlyList<IEcsModule>>).
    //
    // The returned module's RegisterSystems() reproduces EXACTLY the same kernel-phase
    // membership as EditorStrideSubsystem.Initialize steps 7–7c:
    //
    //   Input phase (from [UpdateInPhase(SystemPhase.Input)] on the concrete types):
    //     Combat.InputSystems          (all)
    //     PersonalRoute
    //
    //   Simulation phase (from [UpdateInPhase(SystemPhase.Simulation)]):
    //     Damage.SimulationSystems     (all)
    //     NavIntentBridge
    //     RouteTrajSync
    //     StrideKinematics.SimulationSystems (all)
    //     VehicleNavIntent
    //     UnitHierarchySystem          (new instance)
    //     EqsResultUpdateSystem        (new instance)
    //
    //   Post-simulation phase (from [UpdateInPhase(SystemPhase.PostSimulation)]):
    //     Combat.PostSimulationSystems (all)
    //     StrideKinematics.PostSimulationSystems (all)
    //
    // IMPORTANT: The CGF systems (cgfPack.InputSystems, cgfPack.SimulationSystems) are
    // NOT included here.  In EditorSubsystem's injected path the CGF systems are registered
    // through their own TogglableInputGroup / TogglableSimulationGroup.  Only the MUSCLE
    // portion is the factory's responsibility.
    //
    // Usage:
    //   editor.MuscleModuleFactory = ctx =>
    //   {
    //       var crowd = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
    //       var muscleSet = StrideMuscleModules.Build(crowd);
    //       return muscleSet.ToEditorModuleList();
    //   };

    /// <summary>
    /// Returns a single <see cref="IEcsModule"/> that registers all kernel-resident muscle
    /// systems in the EXACT same phases and order as
    /// <see cref="EditorStrideSubsystem.Initialize"/> steps 7–7c.
    ///
    /// <para>
    /// Suitable for injection into
    /// <c>EditorSubsystem.MuscleModuleFactory</c> (Stage-3 de-risk seam).
    /// The CGF systems are NOT included — <c>EditorSubsystem</c> registers those itself through
    /// its own toggleable groups.
    /// </para>
    /// </summary>
    public IReadOnlyList<IEcsModule> ToEditorModuleList()
        => new IEcsModule[] { new StrideMuscleModule(this) };
}

/// <summary>
/// <b>StrideMuscleModule</b> — the single <see cref="IEcsModule"/> that registers all
/// kernel-resident Stride muscle systems in the correct FDP phases when injected into
/// <see cref="Hrot.Editor.EditorSubsystem"/> via <c>MuscleModuleFactory</c>.
///
/// <para>
/// Phase composition mirrors <see cref="EditorStrideSubsystem.Initialize"/> steps 7–7c exactly:
/// <list type="bullet">
///   <item><b>Input</b>: Combat input systems + PersonalRouteAuthoringSystem.</item>
///   <item><b>Simulation</b>: Damage sim systems, NavIntentBridge, RouteTrajSync,
///     StrideKinematics sim systems, VehicleNavIntent, UnitHierarchySystem, EqsResultUpdateSystem.</item>
///   <item><b>PostSimulation</b>: Combat post-sim systems + StrideKinematics post-sim systems.</item>
/// </list>
/// Each system carries its own <c>[UpdateInPhase]</c> attribute; the kernel uses that attribute
/// to assign the phase — no manual phase specification is needed here.
/// </para>
/// </summary>
public sealed class StrideMuscleModule : IEcsModule
{
    private readonly StrideMuscleModuleSet _set;

    public string          Name   => "StrideMuscle";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public StrideMuscleModule(StrideMuscleModuleSet set)
        => _set = set;

    /// <summary>
    /// Registers muscle systems in the same order as
    /// <see cref="EditorStrideSubsystem.Initialize"/> steps 7–7c.
    /// Duplicate types (by exact reference, not type) are skipped — same guard as
    /// <c>EditorStrideSimulationModule</c> in the existing composition.
    /// </summary>
    public void RegisterSystems(ISystemRegistry registry)
    {
        var seen = new System.Collections.Generic.HashSet<System.Type>();

        void Add(IEcsModuleSystem sys)
        {
            if (seen.Add(sys.GetType())) registry.RegisterSystem(sys);
        }

        // ── Input phase ───────────────────────────────────────────────────────────
        // Mirrors: foreach (var sys in muscleSet.Combat.InputSystems) Kernel.RegisterGlobalSystem(sys);
        //          Kernel.RegisterGlobalSystem(muscleSet.PersonalRoute);
        foreach (var sys in _set.Combat.InputSystems)        Add(sys);
        Add(_set.PersonalRoute);

        // ── Simulation phase ──────────────────────────────────────────────────────
        // Mirrors the simSystems list in EditorStrideSubsystem.Initialize:
        //   foreach (var s in muscleSet.Damage.SimulationSystems) simSystems.Add(s);
        //   simSystems.Add(muscleSet.NavIntentBridge);
        //   simSystems.Add(muscleSet.RouteTrajSync);
        //   foreach (var s in muscleSet.StrideKinematics.SimulationSystems) simSystems.Add(s);
        //   simSystems.Add(muscleSet.VehicleNavIntent);
        //   simSystems.Add(new UnitHierarchySystem());
        //   simSystems.Add(new EqsResultUpdateSystem());
        foreach (var sys in _set.Damage.SimulationSystems)              Add(sys);
        Add(_set.NavIntentBridge);
        Add(_set.RouteTrajSync);
        foreach (var sys in _set.StrideKinematics.SimulationSystems)    Add(sys);
        Add(_set.VehicleNavIntent);
        Add(new UnitHierarchySystem());
        Add(new EqsResultUpdateSystem());

        // ── Post-simulation phase ─────────────────────────────────────────────────
        // Mirrors: foreach (var sys in muscleSet.Combat.PostSimulationSystems) Kernel.RegisterGlobalSystem(sys);
        //          foreach (var sys in muscleSet.StrideKinematics.PostSimulationSystems) Kernel.RegisterGlobalSystem(sys);
        foreach (var sys in _set.Combat.PostSimulationSystems)           Add(sys);
        foreach (var sys in _set.StrideKinematics.PostSimulationSystems) Add(sys);
    }

    public void Tick(ISimulationView view, float deltaTime) { }
}
