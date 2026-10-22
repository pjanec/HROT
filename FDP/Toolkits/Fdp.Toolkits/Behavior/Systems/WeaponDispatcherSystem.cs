using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Fbt;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Routes the active <see cref="WeaponChannel"/> to the registered executor.
    /// Checks <see cref="ActorCapabilities.CanShoot"/> before dispatching.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in ActionDispatchModule.
    // [UpdateAfter(typeof(BTreeTickSystem))] -- ordering maintained by array position in ActionDispatchModule.
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public class WeaponDispatcherSystem : DispatcherSystemBase<WeaponChannel>
    {
        private EntityQuery? _mounts;
        private EntityQuery? _loadouts;

        public override void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(WeaponDispatcherSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // Cleanly terminate active actions for entities entering TearDown.
            // The 1-frame ELM delay guarantees the entity and its channel are still intact.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (repo.HasComponent<WeaponChannel>(evt.Entity))
                {
                    ref var ch = ref repo.GetComponentRW<WeaponChannel>(evt.Entity);
                    if (ch.ActiveAction != 0)
                    {
                        _executors[ch.ActiveAction]?.OnExit(evt.Entity, ref ch, repo);
                        ch.ActiveAction = 0;
                    }
                }
            }

            // ⭐⭐ Q94 A-3 (R-3018) — the muscle's answers to this node's fire requests spend (or refuse) the rounds asked, before any
            //   executor asks again this frame.
            Fdp.Toolkit.Combat.ShotLedger.Settle(repo);

            // ⭐ CE-3089 (G7) — a weapon MOUNT child reloads too, whichever mount is firing (it has no WeaponChannel, so the loop
            //   below never reaches it). Measured live: the TOW fired once and its cooldown stayed at 1 for the rest of the run.
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponMountInfo>())
            {
                _mounts ??= repo.Query()
                    .With<Fdp.Toolkit.Combat.Components.WeaponMountInfo>()
                    .With<Fdp.Toolkit.Combat.Components.WeaponState>()
                    .Build();
                foreach (var mount in _mounts)
                {
                    ref var ws = ref repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(mount);
                    DrainCooldown(ref ws, deltaTime);
                    Fdp.Toolkit.Combat.ShotLedger.Tick(ref ws, deltaTime);   // ⭐ Q94 A-3 — the ask timeout and the refusal hold
                    Fdp.Toolkit.Combat.Magazine.Tick(ref ws, deltaTime);   // ⭐ CE-3136 P-5 — a reload runs whether or not the mount fires
                }
            }

            // ⭐ M8 (D-I) — a round SWITCH runs out here, beside the reload: one reload's time, whether or not the mount fires.
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.AmmoLoadout>())
            {
                _loadouts ??= repo.Query().With<Fdp.Toolkit.Combat.Components.AmmoLoadout>().Build();
                foreach (var e in _loadouts)
                {
                    DrainSwitch(ref repo.GetComponentRW<Fdp.Toolkit.Combat.Components.AmmoLoadout>(e), deltaTime);
                }
            }

            var q = repo.Query()
                .With<WeaponChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                if (repo.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(entity))
                {
                    ref var weaponState = ref repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(entity);
                    DrainCooldown(ref weaponState, deltaTime);
                    Fdp.Toolkit.Combat.ShotLedger.Tick(ref weaponState, deltaTime);   // ⭐ Q94 A-3
                    // ⭐ CE-3136 P-5 (D9) — the reload runs here, every frame, so it finishes while the unit is hidden (B8)
                    Fdp.Toolkit.Combat.Magazine.Tick(ref weaponState, deltaTime);
                }

                ref var channel = ref repo.GetComponentRW<WeaponChannel>(entity);
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                // Capability check: no shooting -- fail the channel immediately.
                // Guard applies unconditionally (not only when Running) to prevent a
                // first-activation bypass where Status is Inactive before OnEnter sets Running.
                // ⭐ CE-1210 (§8n MS-2) — a POSSESSED gunner in a vehicle's crew seat fires the station his seat operates
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanShoot) && !Fdp.Toolkit.Combat.WeaponSystems.OperatesAStation(repo, entity))
                {
                    channel.Status = NodeStatus.Failure;
                    Fdp.Toolkit.Combat.WeaponPostureOf.Idle(repo, entity, deltaTime);
                    continue;
                }

                // Lifecycle: detect when a new action has been dispatched.
                if (channel.ActionInstanceId != channel.DispatchedInstanceId)
                {
                    ushort oldAction = channel.DispatchedAction;

                    // Note: at the time OnExit is called, channel.ActiveAction and channel.ActionInstanceId
                    // still hold the OUTGOING action's values. DispatchedInstanceId is updated after this call.
                    // This allows OnExit to identify what it is cleaning up.
                    _executors[oldAction]?.OnExit(entity, ref channel, repo);
                    _executors[channel.ActiveAction]?.OnEnter(entity, ref channel, repo);

                    channel.DispatchedInstanceId = channel.ActionInstanceId;
                    channel.DispatchedAction = channel.ActiveAction;
                }

                // Execute: drive the current action each tick.
                if (channel.ActiveAction != 0 && channel.Status == NodeStatus.Running)
                {
                    _executors[channel.ActiveAction]?.Execute(entity, ref channel, repo, deltaTime);
                }
                // ⭐ WL-6 (DESIGN_Weapon_Layers W-13b) — no weapon action runs: the weapon brain lowers the weapon (Aimed ⇒ Ready at
                //   once, Ready ⇒ AtEase after WeaponPostureOf.LowerAfterSeconds). A running action's executor reports it instead.
                else
                {
                    Fdp.Toolkit.Combat.WeaponPostureOf.Idle(repo, entity, deltaTime);
                    // ⭐ CE-2333 (AQ98 D, R-2070) — no action at all: the weapon status row clears (a finished one keeps its Done / Failed)
                    if (channel.ActiveAction == 0) Fdp.Toolkit.Behavior.Diagnostics.ActionStatusOf.ClearWeapon(repo, entity);
                }
            }
        }

        /// <summary>⭐ M8 (D-I) — a round switch runs out by <paramref name="deltaTime"/> (one reload's time), the one place it drains.</summary>
        public static void DrainSwitch(ref Fdp.Toolkit.Combat.Components.AmmoLoadout load, float deltaTime)
        {
            if (load.SwitchRemaining > 0f) load.SwitchRemaining = MathF.Max(0f, load.SwitchRemaining - deltaTime);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2166</c> — THE one drain of a weapon's per-shot cooldown, once a frame, for every weapon and mount, whether or not it
        /// fires. ⛔ The executors only READ it: they drained it again while an aim or point-fire ran, so a 10 s cooldown fired every
        /// ~5 s (📄 docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §1).
        /// </summary>
        public static void DrainCooldown(ref Fdp.Toolkit.Combat.Components.WeaponState ws, float deltaTime)
        {
            if (ws.CooldownSecondsRemaining > 0f) ws.CooldownSecondsRemaining -= deltaTime;
        }
    }
}
