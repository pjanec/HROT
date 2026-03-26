using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Combat.Executors
{
    /// <summary>
    /// Executor for the AimAndFire weapon action.
    /// Registered with <see cref="FDP.Toolkit.Behavior.Systems.WeaponDispatcherSystem"/> as the
    /// handler for the AimAndFire action ID.
    ///
    /// <b>BS1-T004:</b> Now publishes <see cref="WeaponFireIntent"/> (using stable network entity
    /// IDs) instead of <see cref="FireRequestEvent"/> (which used local ECS handles).  The
    /// <see cref="NetworkEntityMap"/> required for entity-ID conversion is injected at
    /// construction time.
    ///
    /// <b>Phase 0 Adaptation:</b> Uses <see cref="Fdp.Kernel.SimTransform"/> for position
    /// (not <c>VehicleState</c>), consistent with the Phase 0 refactor that replaced
    /// VehicleState-derived positions with SimTransform.
    /// </summary>
    public sealed class AimAndFireExecutor : IActionExecutor<WeaponChannel>
    {
        private readonly NetworkEntityMap _entityMap;

        /// <param name="entityMap">
        /// Shared network entity map used to convert ECS <see cref="Entity"/> handles to
        /// stable <c>long</c> network IDs before publishing <see cref="WeaponFireIntent"/>.
        /// </param>
        public AimAndFireExecutor(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap;
        }

        // ── OnEnter ──────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // Read params from channel inline storage.
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            // Store target in channel State so Execute can read it each tick.
            fixed (byte* dst = channel.State)
                *(Entity*)dst = p.Target;

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            // Read params.
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            // 1. Target-dead check — success: the objective is achieved.
            if (!world.IsAlive(p.Target))
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            // 2. Ammo check — failure: cannot fire without ammunition.
            ref var weapon = ref world.GetComponentRW<WeaponState>(entity);
            if (weapon.Ammo == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // 3. Cooldown check — wait until cooldown expires.
            if (weapon.CooldownTicksRemaining > 0)
            {
                weapon.CooldownTicksRemaining--;
                channel.Status = NodeStatus.Running;
                return;
            }

            // 4. Resolve network entity IDs for the intent payload.
            //    If an entity is not yet mapped (edge case during spawn), default to 0.
            _entityMap.TryGetNetworkId(entity, out long shooterId);
            _entityMap.TryGetNetworkId(p.Target, out long targetId);

            // 5. Publish WeaponFireIntent for FireProcessingSystem (local AllInOne) or
            //    WeaponFireIntentEgressTranslator (split topology).
            world.Bus.Publish(new WeaponFireIntent
            {
                ShooterEntityId = shooterId,
                TargetEntityId  = targetId,
                WeaponIndex     = 0,   // POC: single weapon slot
            });

            // 6. Consume one round and start cooldown.
            weapon.Ammo--;
            weapon.CooldownTicksRemaining = p.CooldownTicks;

            channel.Status = NodeStatus.Running;
        }

        // ── OnExit ───────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // No state to clean up.
        }
    }
}
