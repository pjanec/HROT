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
}
