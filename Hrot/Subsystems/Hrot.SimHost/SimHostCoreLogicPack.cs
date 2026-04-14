using System;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.CarKinem.Modules;
using Fdp.Toolkit.Combat.Modules;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Perception.Modules;
using Fdp.Toolkit.Replication.Services;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems.Routing;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost
{
    /// <summary>
    /// Composite <see cref="IEcsModule"/> that groups the four Muscle-tier simulation
    /// modules into a single named unit for use by the HROT Editor composition root and
    /// the Feature Switch (Phase 5 / PACK2-C001).
    ///
    /// <para><b>Contained modules (in registration order):</b></para>
    /// <list type="number">
    ///   <item><see cref="CombatModule"/> — fire processing, raycast, hit-resolution, damage</item>
    ///   <item><see cref="DamageAssessmentModule"/> — detonation → damage-assessed event</item>
    ///   <item>Navigation bridge systems — NavigationIntent → NavState, route sync, authoring, context</item>
    ///   <item><see cref="GroundKinematicsModule"/> — spatial hash, formation, vehicle physics, nav execution</item>
    ///   <item><see cref="AutonomousPerceptionModule"/> — LOS / threat evaluation (slow background)</item>
    /// </list>
    ///
    /// <para><b>Registration pattern:</b> Because the four contained modules extend
    /// <see cref="ComponentSystem"/> rather than <see cref="IEcsModuleSystem"/>, their
    /// systems must be wired into a <see cref="SystemGroup"/> via
    /// <see cref="RegisterSystems(SystemGroup,SystemGroup,SystemGroup)"/>.
    /// The <see cref="IEcsModule.RegisterSystems(ISystemRegistry)"/> overload is a no-op
    /// and is provided for API compliance only (same pattern as
    /// <c>FDP.Toolkit.Physics.Modules.PhysicsQueryModule</c>).</para>
    ///
    /// <para><b>Execution order:</b> matches the production order used by
    /// <see cref="SimulationLogicModule"/> for the <c>MuscleGround</c> role.</para>
    /// </summary>
    public sealed class SimHostCoreLogicPack : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "SimHostCoreLogicPack";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        // ── Sub-modules ───────────────────────────────────────────────────────
        private readonly CombatModule                _combatModule;
        private readonly DamageAssessmentModule      _damageAssessmentModule;
        private readonly GroundKinematicsModule      _groundKinematicsModule;
        private readonly AutonomousPerceptionModule  _perceptionModule;
        private readonly NetworkEntityMap            _entityMap;

        // ── Public accessors (mirroring SimulationLogicModule) ────────────────

        /// <summary>Shared trajectory pool (forwarded from GroundKinematicsModule).</summary>
        public TrajectoryPoolManager TrajectoryPool => _groundKinematicsModule.TrajectoryPool;

        /// <summary>Shared formation-template manager (forwarded from GroundKinematicsModule).</summary>
        public FormationTemplateManager FormationTemplates => _groundKinematicsModule.FormationTemplates;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the Muscle-tier logic pack with the dependencies required by
        /// the four contained sub-modules.
        /// </summary>
        /// <param name="entityMap">
        /// Shared network entity map injected into <see cref="CombatModule"/>
        /// (<c>FireProcessingSystem</c> resolves <c>WeaponFireIntent</c> network IDs
        /// from this map).
        /// </param>
        /// <param name="roadNetwork">
        /// Road-network blob forwarded to <see cref="GroundKinematicsModule"/>.
        /// A default (empty) blob is valid for tests and maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        /// Optional shared trajectory pool.  A new pool is lazily allocated by
        /// <see cref="GroundKinematicsModule"/> when <c>null</c>.
        /// </param>
        /// <param name="formationTemplateManager">
        /// Optional formation-template manager.  A new manager (with default templates)
        /// is lazily allocated by <see cref="GroundKinematicsModule"/> when <c>null</c>.
        /// </param>
        /// <param name="colliderRadiusReader">
        /// Optional delegate forwarded to <see cref="AutonomousPerceptionModule"/> for
        /// physics-accurate LOS segment-circle occlusion checks.  Pass <c>null</c> to
        /// treat all occluders as point entities.
        /// </param>
        public SimHostCoreLogicPack(
            NetworkEntityMap                           entityMap,
            RoadNetworkBlob                            roadNetwork              = default,
            TrajectoryPoolManager?                     trajectoryPool           = null,
            FormationTemplateManager?                  formationTemplateManager = null,
            Func<ISimulationView, Entity, float>?      colliderRadiusReader     = null)
        {
            _entityMap              = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _combatModule           = new CombatModule();
            _damageAssessmentModule = new DamageAssessmentModule();
            _groundKinematicsModule = new GroundKinematicsModule(
                roadNetwork,
                trajectoryPool,
                formationTemplateManager);
            _perceptionModule       = new AutonomousPerceptionModule(colliderRadiusReader);
        }

        // ── IEcsModule ────────────────────────────────────────────────────────

        /// <summary>
        /// No-op — the contained sub-modules use <see cref="ComponentSystem"/>-based
        /// <see cref="SystemGroup"/> registration and cannot be registered via
        /// <see cref="ISystemRegistry"/>.
        /// Call <see cref="RegisterSystems(SystemGroup,SystemGroup,SystemGroup)"/> to
        /// wire them into the application's system groups.
        ///
        /// <para>The <see cref="AutonomousPerceptionModule"/> is driven via
        /// <see cref="Tick"/> and does not need any group registration.</para>
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>
        /// Drives <see cref="AutonomousPerceptionModule"/> which executes its internal
        /// perception pipeline via Pattern 2 (direct <c>Tick</c> execution).
        /// </summary>
        public void Tick(ISimulationView view, float deltaTime)
            => _perceptionModule.Tick(view, deltaTime);

        // ── SystemGroup-based registration ────────────────────────────────────

        /// <summary>
        /// Registers the Muscle-tier systems into the supplied system groups in the
        /// same execution order used by <see cref="SimulationLogicModule"/> for the
        /// <c>MuscleGround</c> role.
        /// </summary>
        /// <param name="inputGroup">Input-phase group (fire processing, raycast, route authoring).</param>
        /// <param name="simGroup">Simulation-phase group (damage, navigation, kinematics).</param>
        /// <param name="postSimGroup">Post-simulation group (ballistics).</param>
        public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)
        {
            if (inputGroup   == null) throw new ArgumentNullException(nameof(inputGroup));
            if (simGroup     == null) throw new ArgumentNullException(nameof(simGroup));
            if (postSimGroup == null) throw new ArgumentNullException(nameof(postSimGroup));

            // Combat (Input + Sim + PostSim).
            _combatModule.RegisterSystems(inputGroup, simGroup, postSimGroup, _entityMap);

            // DamageAssessment (Sim).
            _damageAssessmentModule.RegisterSystems(simGroup);

            // Navigation bridge systems (collocated with GroundKinematics, same as SimulationLogicModule).
            simGroup.AddSystem(new NavigationIntentBridgeSystem());
            simGroup.AddSystem(new RouteTrajectorySyncSystem(_groundKinematicsModule.TrajectoryPool));
            inputGroup.AddSystem(new PersonalRouteAuthoringSystem());

            // Ground kinematics (Sim).
            _groundKinematicsModule.RegisterSystems(simGroup);
        }
    }
}
