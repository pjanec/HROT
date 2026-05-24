using System;
using System.Collections.Generic;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Toolkit.CarKinem.Modules;
using Fdp.Toolkit.Combat.Modules;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Systems;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Systems;
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
    /// <para><b>Registration pattern:</b> the contained sub-modules expose phase-typed
    /// <c>IReadOnlyList&lt;IEcsModuleSystem&gt;</c> array properties (<c>InputSystems</c>,
    /// <c>SimulationSystems</c>, <c>PostSimulationSystems</c>).  Wrap those lists in
    /// <c>TogglableInputGroup</c>, <c>TogglableSimulationGroup</c>, and
    /// <c>TogglablePostSimulationGroup</c> at the composition root.</para>
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

        // ── Navigation bridge systems ─────────────────────────────────────────
        private readonly NavigationIntentBridgeSystem _navIntentBridge;
        private readonly RouteTrajectorySyncSystem    _routeTrajSync;
        private readonly PersonalRouteAuthoringSystem _personalRouteAuthoring;

        // ── Hierarchy system ──────────────────────────────────────────────────
        private readonly UnitHierarchySystem          _unitHierarchySystem;

        // ── Public accessors (mirroring SimulationLogicModule) ────────────────

        /// <summary>Shared trajectory pool (forwarded from GroundKinematicsModule).</summary>
        public TrajectoryPoolManager TrajectoryPool => _groundKinematicsModule.TrajectoryPool;

        /// <summary>Shared formation-template manager (forwarded from GroundKinematicsModule).</summary>
        public FormationTemplateManager FormationTemplates => _groundKinematicsModule.FormationTemplates;

        /// <summary>Systems to wrap in TogglableInputGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        /// <summary>Systems to wrap in TogglablePostSimulationGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }

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

            // Navigation bridge systems
            _navIntentBridge        = new NavigationIntentBridgeSystem();
            _routeTrajSync          = new RouteTrajectorySyncSystem(_groundKinematicsModule.TrajectoryPool);
            _personalRouteAuthoring = new PersonalRouteAuthoringSystem();

            // Hierarchy system
            _unitHierarchySystem    = new UnitHierarchySystem();

            // Phase arrays
            var inputList   = new List<IEcsModuleSystem>();
            var simList     = new List<IEcsModuleSystem>();
            var postSimList = new List<IEcsModuleSystem>();

            foreach (var s in _combatModule.InputSystems)  inputList.Add(s);
            inputList.Add(_personalRouteAuthoring);

            foreach (var s in _damageAssessmentModule.SimulationSystems) simList.Add(s);
            simList.Add(_navIntentBridge);
            simList.Add(_routeTrajSync);
            foreach (var s in _groundKinematicsModule.SimulationSystems) simList.Add(s);
            simList.Add(_unitHierarchySystem);
            simList.Add(new EqsResultUpdateSystem());

            foreach (var s in _combatModule.PostSimulationSystems)             postSimList.Add(s);
            foreach (var s in _groundKinematicsModule.PostSimulationSystems)   postSimList.Add(s);

            InputSystems          = inputList;
            SimulationSystems     = simList;
            PostSimulationSystems = postSimList;
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
    }
}
