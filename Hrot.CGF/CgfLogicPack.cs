using System;
using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.Replication.Services;
using Hrot.Common.Systems;
using ModuleHost.Core.Abstractions;

namespace Hrot.CGF
{
    /// <summary>
    /// Composite <see cref="IEcsModule"/> that groups the three Brain-tier CGF modules
    /// into a single named unit for use by the HROT Editor composition root and
    /// the Feature Switch (Phase 5 / PACK2-C001).
    ///
    /// <para><b>Contained modules (in registration order matching the Brain role):</b></para>
    /// <list type="number">
    ///   <item><see cref="MissionControlModule"/> — doctrine ingress + mission direction</item>
    ///   <item><see cref="CognitiveRuntimeModule"/> — BTree/HSM tick + channel arbitration</item>
    ///   <item><see cref="ActionDispatchModule"/> — locomotion + weapon dispatchers</item>
    /// </list>
    ///
    /// <para><b>Registration pattern:</b> Because all three contained modules extend
    /// <see cref="ComponentSystem"/> (not <see cref="IEcsModuleSystem"/>), their
    /// systems must be wired into a simulation-phase <see cref="SystemGroup"/> via
    /// <see cref="RegisterSystems(SystemGroup)"/>. The
    /// <see cref="IEcsModule.RegisterSystems(ISystemRegistry)"/> overload is a no-op
    /// and is provided for API compliance only.</para>
    ///
    /// <para><b>Execution order:</b> matches the production order used by
    /// <c>SimulationLogicModule</c> for the <c>Brain</c> role
    /// (Mission → Cognitive → ActionDispatch).</para>
    /// </summary>
    public sealed class CgfLogicPack : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "CgfLogicPack";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        // ── Sub-modules ───────────────────────────────────────────────────────
        private readonly MissionControlModule   _missionControlModule;
        private readonly CognitiveRuntimeModule _cognitiveRuntimeModule;
        private readonly ActionDispatchModule   _actionDispatchModule;
        private readonly MissionControlExecutionSystem _missionExecutionSystem;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the Brain-tier CGF logic pack with the standard Brain executor set
        /// (MoveToExecutor, FollowRouteExecutor, JoinFormationExecutor, AimAndFireExecutor).
        /// </summary>
        /// <param name="doctrineRegistry">
        /// Doctrine definitions registry forwarded to <see cref="MissionControlModule"/>
        /// and <see cref="CognitiveRuntimeModule"/>.
        /// </param>
        /// <param name="entityMap">
        /// Network entity map forwarded to <see cref="ActionDispatchModule"/> via
        /// <see cref="JoinFormationExecutor"/>.
        /// </param>
        /// <param name="vehicleApi">
        /// Optional high-level vehicle command façade forwarded to
        /// <see cref="JoinFormationExecutor"/>.  <c>null</c> while the executor is a stub.
        /// </param>
        public CgfLogicPack(
            DoctrineRegistry  doctrineRegistry,
            NetworkEntityMap  entityMap,
            VehicleAPI?       vehicleApi = null)
        {
            if (doctrineRegistry == null) throw new ArgumentNullException(nameof(doctrineRegistry));
            if (entityMap        == null) throw new ArgumentNullException(nameof(entityMap));

            _missionControlModule   = new MissionControlModule(doctrineRegistry);
            _cognitiveRuntimeModule = new CognitiveRuntimeModule(doctrineRegistry);
            _missionExecutionSystem = new MissionControlExecutionSystem(entityMap, doctrineRegistry);
            _actionDispatchModule   = new ActionDispatchModule(
                locoExecutors: new (ushort, IActionExecutor<LocomotionChannel>)[]
                {
                    (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
                    (NavigationConstants.ActionIdFollowRoute,   new FollowRouteExecutor()),
                    (NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(vehicleApi, entityMap)),
                },
                weaponExecutors: new (ushort, IActionExecutor<WeaponChannel>)[]
                {
                    (CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor()),
                });
        }

        // ── IEcsModule ────────────────────────────────────────────────────────

        /// <summary>
        /// No-op — the contained sub-modules use <see cref="ComponentSystem"/>-based
        /// <see cref="SystemGroup"/> registration and cannot be registered via
        /// <see cref="ISystemRegistry"/>.
        /// Call <see cref="RegisterSystems(SystemGroup)"/> to wire them into the
        /// application's simulation-phase system group.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>Empty — all logic is handled by registered systems.</summary>
        public void Tick(ISimulationView view, float deltaTime) { }

        // ── SystemGroup-based registration ────────────────────────────────────

        /// <summary>
        /// Registers the Brain-tier systems into the supplied simulation-phase group
        /// in the same execution order used by <c>SimulationLogicModule</c> for the
        /// <c>Brain</c> role: Mission → Cognitive → ActionDispatch.
        /// </summary>
        /// <param name="simGroup">Simulation-phase system group.</param>
        public void RegisterSystems(SystemGroup simGroup)
        {
            if (simGroup == null) throw new ArgumentNullException(nameof(simGroup));

            // Mission intent execution must run before mission/cognitive runtime systems.
            simGroup.AddSystem(_missionExecutionSystem);
            _missionControlModule.RegisterSystems(simGroup);
            _cognitiveRuntimeModule.RegisterSystems(simGroup);
            _actionDispatchModule.RegisterSystems(simGroup);
        }
    }
}
