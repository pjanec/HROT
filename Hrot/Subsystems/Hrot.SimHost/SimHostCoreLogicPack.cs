using System;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.CarKinem.Modules;
using Fdp.Toolkit.Combat.Modules;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Replication.Services;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems.Routing;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost;

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
    /// </list>
    ///
    /// <para><b>Registration pattern:</b> the contained sub-modules use the
    /// <see cref="RegisterSystems(SystemGroup,SystemGroup,SystemGroup)"/> overload and are added
    /// directly to the supplied <see cref="SystemGroup"/> instances.</para>
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
        private readonly NetworkEntityMap            _entityMap;

        // ── Public accessors (mirroring SimulationLogicModule) ────────────────

        /// <summary>Shared trajectory pool (forwarded from GroundKinematicsModule).</summary>
        public TrajectoryPoolManager TrajectoryPool => _groundKinematicsModule.TrajectoryPool;

        /// <summary>Shared formation-template manager (forwarded from GroundKinematicsModule).</summary>
        public FormationTemplateManager FormationTemplates => _groundKinematicsModule.FormationTemplates;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the Muscle-tier logic pack with the dependencies required by
        /// the contained sub-modules.
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
        public SimHostCoreLogicPack(
            NetworkEntityMap                           entityMap,
            RoadNetworkBlob                            roadNetwork              = default,
            TrajectoryPoolManager?                     trajectoryPool           = null,
            FormationTemplateManager?                  formationTemplateManager = null)
        {
            _entityMap              = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _combatModule           = new CombatModule();
            _damageAssessmentModule = new DamageAssessmentModule();
            _groundKinematicsModule = new GroundKinematicsModule(
                roadNetwork,
                trajectoryPool,
                formationTemplateManager);
        }

        // ── IEcsModule ────────────────────────────────────────────────────────

        /// <summary>
        /// No systems are registered through <see cref="ISystemRegistry"/> in this pack.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>
        /// No per-frame work is executed directly in this pack.
        /// </summary>
        public void Tick(ISimulationView view, float deltaTime) { }

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

            // Combat (Input + PostSim).
            foreach (var s in _combatModule.InputSystems) inputGroup.AddSystem(s);
            foreach (var s in _combatModule.PostSimulationSystems) postSimGroup.AddSystem(s);

            // DamageAssessment (Sim).
            foreach (var s in _damageAssessmentModule.SimulationSystems) simGroup.AddSystem(s);

            // Navigation bridge systems (collocated with GroundKinematics, same as SimulationLogicModule).
            simGroup.AddSystem(new NavigationIntentBridgeSystem());
            simGroup.AddSystem(new RouteTrajectorySyncSystem(_groundKinematicsModule.TrajectoryPool));
            inputGroup.AddSystem(new PersonalRouteAuthoringSystem());

            // Ground kinematics (Sim).
            foreach (var s in _groundKinematicsModule.SimulationSystems) simGroup.AddSystem(s);
            foreach (var s in _groundKinematicsModule.PostSimulationSystems) simGroup.AddSystem(s);
        }
    }
}
