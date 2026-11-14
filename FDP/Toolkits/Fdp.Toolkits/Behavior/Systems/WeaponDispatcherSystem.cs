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
        private EntityQuery? _barrels;   // ⭐ CE-1358

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

            // ⭐ CE-1358 (docs/DESIGN_Weapon_Layers.md §5k) — EVERY barrel cools, once a frame: a unit's own, a mount child, and a weapon
            //   system (a door gun) that has neither a channel nor a mount record.
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponState>())
            {
                _barrels ??= repo.Query().With<Fdp.Toolkit.Combat.Components.WeaponState>().Build();
                foreach (var e in _barrels)
                {
                    if (repo.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponState>(e).Heat <= 0f) continue;
                    Fdp.Toolkit.Combat.BarrelHeat.Tick(ref repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(e), deltaTime);
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

            _channels ??= repo.Query()
                .With<WeaponChannel>()
                .With<ActorCapabilityState>()
                .Build();

            if (Fdp.Core.Jobs.ParallelEntities.Enabled)
            {
                // ⭐⭐ M-6 (CE-4485; DESIGN_Parallel_Simulation §18.5) — each unit's dispatch is a PER-ENTITY phase: it writes its own channel and
                //   weapon, its parts (mount children, turret parts), and the weapon systems it CONTROLS (a station's M2, the Javelin it carries,
                //   the tripod it mans — WeaponSystems.Controlled) — claimed with OwnAlso, their parts with them. ⭐ Disjoint BY CONSTRUCTION:
                //   a weapon has ONE controller (WeaponSystems.ControllerOf, §6 W-D), a function of seats and possession, which nothing in this
                //   phase changes. Everything else follows the rule: other units read as of the tick start, the fire intents (publishes)
                //   deferred and played in batch order.
                int n = 0;
                foreach (var entity in _channels)
                {
                    if (n == _list.Length) Array.Resize(ref _list, _list.Length * 2);
                    _list[n++] = entity;
                }
                DeclareTickReads();
                // ⭐ CE-4516 — the parts index made fresh serially first (as the brains' and the squads' phases do): every PartsOf in the weapon
                //   phase then reads lock-free and never takes the late-add path inside it (📐 the read-check survey named PartMetadata, read by
                //   PartIndex.TakeLateAdds from AimAndFireExecutor.EngagementMount within the phase)
                Fdp.Toolkit.Replication.Utilities.PartIndex.PrepareFrame(repo);
                Fdp.Core.Jobs.ParallelEntities.Run(repo, _list, n, new DispatchJob { System = this, Repo = repo, Dt = deltaTime });
                return;
            }
            foreach (var entity in _channels) DispatchOne(repo, entity, deltaTime);
        }

        private EntityQuery? _channels;
        private Entity[] _list = new Entity[64];

        /// <summary>
        /// ⭐ M-7 (CE-4486) — what a unit's weapon frame reads on OTHER units that this phase also writes: from the tick-start view. 📐 Measured
        /// (the read-check survey, 1 000 men, 4 threads): the force level read the TARGET's <see cref="Fdp.Toolkit.Combat.Components.WeaponPosture"/>
        /// (<c>ForceLevels.AimsAt</c>) while the target's own job wrote it — the engagement mount it chose, kept in the channel, differed in one
        /// 4-thread run in three. The aim point reads the target's stance intent, which a shooter's own frame writes (<c>StanceRequest</c>).
        /// </summary>
        private static void DeclareTickReads()
        {
            if (s_declared) return;
            Fdp.Core.ParallelReadCheck.DeclareUnmanaged<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            Fdp.Core.ParallelReadCheck.DeclareUnmanaged<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            s_declared = true;
        }
        private static bool s_declared;

        private struct DispatchJob : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads
        {
            // ⭐ M-7 — what it reads on others that nothing writes while it runs (the survey): who controls a weapon (seats, stations), and a
            //   target's size for the aim point
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads
                .Unmanaged<Fdp.Toolkit.Combat.WeaponSystemParams>()
                .Unmanaged<IsEmbarkedTag>()
                .Unmanaged<PassengerBuffer>()
                .Unmanaged<CarrierStations>()
                .Unmanaged<Fdp.Toolkit.Physics.Components.PhysicsCollider>()
                .Unmanaged<global::CarKinem.Core.VehicleParams>();
            public WeaponDispatcherSystem System;
            public EntityRepository Repo;
            public float Dt;
            public void Execute(Entity entity, int index, Fdp.Core.Jobs.BatchContext ctx)
            {
                Span<Entity> controlled = stackalloc Entity[Fdp.Toolkit.Combat.WeaponSystems.MaxControlled];
                int k = Fdp.Toolkit.Combat.WeaponSystems.Controlled(Repo, entity, controlled);
                for (int i = 0; i < k; i++) Fdp.Core.ParallelReadCheck.OwnAlso(controlled[i]);
                System.DispatchOne(Repo, entity, Dt);
            }
            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d) { }
        }

        /// <summary>One unit's weapon frame: its barrel's cooldown / ask / reload, the capability gate, the action's lifecycle and execution.</summary>
        private void DispatchOne(EntityRepository repo, Entity entity, float deltaTime)
        {
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
                    return;
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
