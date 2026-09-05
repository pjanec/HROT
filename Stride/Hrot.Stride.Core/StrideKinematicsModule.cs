#nullable enable
using System.Collections.Generic;
using CarKinem.Formation;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Hrot.Common.Systems;           // DeadReckoningSyncSystem (namespace Hrot.Common.Systems, project Hrot.Core)

namespace Hrot.Stride.Core;

/// <summary>
/// <b>StrideKinematicsModule</b> — the FDP kinematic system set for the Stride node (STR-P1-T1).
///
/// <para>
/// Replaces <c>GroundKinematicsModule</c>'s role in <c>SimHostCoreLogicPack</c>: it holds all
/// the <em>kept</em> spatial/command/navigation systems while deliberately <em>omitting</em>
/// the two FDP integrators (<c>CarKinematicsSystem</c> and <c>LinearKinematicsSystem</c>) whose
/// job is taken over by the Bullet physics engine in Phase 1.
/// </para>
///
/// <para>
/// <b>Kept systems (design §5.1, §5.2):</b>
/// <list type="bullet">
///   <item><see cref="SpatialHashSystem"/> — rebuilds spatial grid from <c>SimTransform</c> + collider.</item>
///   <item><see cref="FormationTargetSystem"/> — high-level command processing.</item>
///   <item><see cref="VehicleCommandSystem"/> — high-level command processing.</item>
///   <item><see cref="NavigationExecutionSystem"/> — writes CQRS <c>NavigationStatus</c>; solver-agnostic.</item>
///   <item><see cref="CrowdAgentUpdateSystem"/> — velocity-only crowd steering.  The
///     <c>SimTransform</c>-mutation refactor is P2-T4; the system is kept as-is for Phase 1.</item>
///   <item><see cref="DeadReckoningSyncSystem"/> constructed with <c>driveFromNetwork = false</c> (§5.4)
///     so smoothing applies <em>only</em> to non-owned ghost entities.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Excluded systems (§5.1, §5.5):</b>
/// <list type="bullet">
///   <item><c>CarKinematicsSystem</c> — replaced by <c>KinematicVehicleMotor</c> (P1-T4).</item>
///   <item><c>LinearKinematicsSystem</c> — replaced by Bullet rigid/character bodies.</item>
///   <item><c>TerrainQuerySubmitSystem</c> / <c>TerrainQuerySolverSystem</c> / <c>TerrainQueryResolutionSystem</c>
///     — geographic ground-clamp pipeline; Bullet resting contact provides authoritative Z (§5.5).</item>
/// </list>
/// </para>
///
/// <para>
/// Exclusion is <em>purely topological</em>: entities still carry <c>SimTransform</c> /
/// <c>SimVelocity</c>; the integrators are simply never registered.
/// </para>
/// </summary>
public sealed class StrideKinematicsModule
{
    private readonly IDtCrowdProvider    _dtCrowd;
    private readonly TrajectoryPoolManager _trajectoryPool;

    /// <summary>
    /// Systems that run in the <c>Simulation</c> phase.
    /// Mirrors <c>GroundKinematicsModule.SimulationSystems</c> but omits the two integrators.
    /// </summary>
    public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

    /// <summary>
    /// Systems that run in the <c>PostSimulation</c> phase.
    /// Contains only <see cref="DeadReckoningSyncSystem"/> (<c>DriveFromNetwork=false</c>).
    /// <c>CarKinematicsSystem</c> and <c>LinearKinematicsSystem</c> are intentionally absent.
    /// </summary>
    public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }

    /// <summary>
    /// Constructs the module.
    /// </summary>
    /// <param name="dtCrowd">
    /// DtCrowd provider injected into <see cref="CrowdAgentUpdateSystem"/>.
    /// Use a no-op implementation during Phase 0 / tests before real crowd is wired.
    /// </param>
    /// <param name="trajectoryPool">
    /// Optional shared trajectory pool forwarded to <see cref="FormationTargetSystem"/>.
    /// A new pool is allocated lazily when <c>null</c>.
    /// </param>
    /// <param name="formationTemplates">
    /// Optional formation-template manager forwarded to <see cref="FormationTargetSystem"/>.
    /// A new manager (with default templates) is allocated lazily when <c>null</c>.
    /// </param>
    public StrideKinematicsModule(
        IDtCrowdProvider          dtCrowd,
        TrajectoryPoolManager?    trajectoryPool     = null,
        FormationTemplateManager? formationTemplates = null)
    {
        _dtCrowd = dtCrowd;

        _trajectoryPool = trajectoryPool     ?? new TrajectoryPoolManager();
        var pool        = _trajectoryPool;
        var templates   = formationTemplates ?? new FormationTemplateManager();

        SimulationSystems = new IEcsModuleSystem[]
        {
            new SpatialHashSystem(),
            new FormationTargetSystem(templates, pool),
            new VehicleCommandSystem(),
            new NavigationExecutionSystem(),
            new CrowdAgentUpdateSystem(dtCrowd),
        };

        // CarKinematicsSystem and LinearKinematicsSystem are INTENTIONALLY ABSENT.
        // TerrainQuerySubmitSystem / TerrainQuerySolverSystem / TerrainQueryResolutionSystem
        // are INTENTIONALLY ABSENT (§5.5 — Bullet resting contact provides authoritative Z).
        PostSimulationSystems = new IEcsModuleSystem[]
        {
            // DriveFromNetwork=false: ghost/non-owned entities are dead-reckoned;
            // locally-owned bodies are driven by Bullet → reverse-sync only (§5.4).
            new DeadReckoningSyncSystem(driveFromNetwork: false),
        };
    }

    /// <summary>Exposes the underlying crowd provider (for diagnostics / tests).</summary>
    public IDtCrowdProvider DtCrowd => _dtCrowd;

    /// <summary>
    /// Shared trajectory pool (forwarded to <c>RouteTrajectorySyncSystem</c> in
    /// <c>SimHostCoreLogicPack</c>-style compositions that re-use this module).
    /// </summary>
    public TrajectoryPoolManager TrajectoryPool => _trajectoryPool;
}
