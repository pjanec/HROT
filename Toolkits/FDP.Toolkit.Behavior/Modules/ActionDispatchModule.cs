using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Systems;

namespace FDP.Toolkit.Behavior.Modules
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
    /// </list>
    /// </summary>
    public sealed class ActionDispatchModule
    {
        private readonly (ushort ActionId, IActionExecutor<LocomotionChannel> Executor)[] _locoExecutors;
        private readonly (ushort ActionId, IActionExecutor<WeaponChannel>     Executor)[] _weaponExecutors;

        /// <summary>
        /// Initialises the module with a set of locomotion and weapon executor registrations.
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
        public ActionDispatchModule(
            (ushort, IActionExecutor<LocomotionChannel>)[] locoExecutors,
            (ushort, IActionExecutor<WeaponChannel>)[]?    weaponExecutors = null)
        {
            _locoExecutors   = locoExecutors  ?? throw new ArgumentNullException(nameof(locoExecutors));
            _weaponExecutors = weaponExecutors ?? Array.Empty<(ushort, IActionExecutor<WeaponChannel>)>();
        }

        /// <summary>
        /// Registers the locomotion and weapon dispatcher systems — with all injected
        /// executors wired in — into the provided group.
        /// </summary>
        /// <param name="group">The simulation-phase <see cref="SystemGroup"/> to add into.</param>
        public void RegisterSystems(SystemGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));

            // ── Locomotion dispatcher ─────────────────────────────────────────
            var locoDispatcher = new LocomotionDispatcherSystem();
            foreach (var (id, exec) in _locoExecutors)
                locoDispatcher.RegisterExecutor(id, exec);

            // ── Weapon dispatcher ─────────────────────────────────────────────
            var weaponDispatcher = new WeaponDispatcherSystem();
            foreach (var (id, exec) in _weaponExecutors)
                weaponDispatcher.RegisterExecutor(id, exec);

            group.AddSystem(locoDispatcher);
            group.AddSystem(weaponDispatcher);
        }
    }
}
