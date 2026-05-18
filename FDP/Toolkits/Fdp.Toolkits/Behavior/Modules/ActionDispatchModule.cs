using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Systems;

namespace Fdp.Toolkit.Behavior.Modules
{
    /// <summary>
    /// Generic action-dispatch grouping: registers a <see cref="LocomotionDispatcherSystem"/>
    /// and a <see cref="WeaponDispatcherSystem"/> with their executor sets into a
    /// given <see cref="SystemGroup"/>.
    ///
    /// <para>
    /// This class is intentionally executor-agnostic — it accepts any
    /// <see cref="IActionExecutor{TChannel}"/> implementation via constructor injection,
    /// breaking the circular dependency that previously required it to live inside
    /// project specific parts (where domain-specific executors such as
    /// <c>JoinFormationExecutor</c> are defined).
    /// </para>
    ///
    /// <para><b>Usage (composition root):</b></para>
    /// <code>
    /// var module = new ActionDispatchModule(
    ///     locoExecutors: new[]
    ///     {
    ///         (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
    ///         (NavigationConstants.ActionIdFollowRoute,   new FollowRouteExecutor()),
    ///         (NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(vehicleApi, entityMap)),
    ///     },
    ///     weaponExecutors: new[]
    ///     {
    ///         (CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor()),
    ///     });
    /// module.RegisterSystems(simGroup);
    /// </code>
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="LocomotionDispatcherSystem"/> with all supplied locomotion executors.</item>
    ///   <item><see cref="WeaponDispatcherSystem"/> with all supplied weapon executors.</item>
    ///   <item><see cref="InteractionDispatcherSystem"/> with all supplied interaction executors.</item>
    /// </list>
    /// </summary>
    public sealed class ActionDispatchModule
    {
        private readonly (ushort ActionId, IActionExecutor<LocomotionChannel>    Executor)[] _locoExecutors;
        private readonly (ushort ActionId, IActionExecutor<WeaponChannel>        Executor)[] _weaponExecutors;
        private readonly (ushort ActionId, IActionExecutor<InteractionChannel>   Executor)[] _interactionExecutors;

        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        /// <summary>
        /// Initialises the module with a set of locomotion, weapon, and interaction executor registrations.
        /// </summary>
        /// <param name="locoExecutors">
        ///   Pairs of (actionId, executor) for locomotion. Each executor handles the
        ///   <see cref="LocomotionChannel"/> when <c>ActiveAction == actionId</c>.
        ///   Must not be <c>null</c>.
        /// </param>
        /// <param name="weaponExecutors">
        ///   Pairs of (actionId, executor) for weapons. <c>null</c> or empty is valid
        ///   (no weapon dispatcher executors will be registered).
        /// </param>
        /// <param name="interactionExecutors">
        ///   Pairs of (actionId, executor) for interaction actions (e.g. EjectPassengers).
        ///   <c>null</c> or empty is valid (no interaction dispatcher executors will be registered).
        /// </param>
        public ActionDispatchModule(
            (ushort, IActionExecutor<LocomotionChannel>)[]    locoExecutors,
            (ushort, IActionExecutor<WeaponChannel>)[]?       weaponExecutors      = null,
            (ushort, IActionExecutor<InteractionChannel>)[]?  interactionExecutors = null)
        {
            _locoExecutors        = locoExecutors         ?? throw new ArgumentNullException(nameof(locoExecutors));
            _weaponExecutors      = weaponExecutors       ?? Array.Empty<(ushort, IActionExecutor<WeaponChannel>)>();
            _interactionExecutors = interactionExecutors  ?? Array.Empty<(ushort, IActionExecutor<InteractionChannel>)>();

            // Build and wire dispatcher systems during construction so that SimulationSystems
            // is ready immediately after the module is created.
            var locoDispatcher = new LocomotionDispatcherSystem();
            foreach (var (id, exec) in _locoExecutors)
                locoDispatcher.RegisterExecutor(id, exec);

            var weaponDispatcher = new WeaponDispatcherSystem();
            foreach (var (id, exec) in _weaponExecutors)
                weaponDispatcher.RegisterExecutor(id, exec);

            var interactionDispatcher = new InteractionDispatcherSystem();
            foreach (var (id, exec) in _interactionExecutors)
                interactionDispatcher.RegisterExecutor(id, exec);

            SimulationSystems = new IEcsModuleSystem[]
            {
                locoDispatcher,
                weaponDispatcher,
                interactionDispatcher,
            };
        }
    }
}
