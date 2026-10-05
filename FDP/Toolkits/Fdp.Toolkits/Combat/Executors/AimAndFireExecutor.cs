using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat.Executors
{
    /// <summary>
    /// Executor for the AimAndFire weapon action.
    /// Registered with <see cref="Fdp.Toolkit.Behavior.Systems.WeaponDispatcherSystem"/> as the
    /// handler for the AimAndFire action ID.
    ///
    /// PACK-P003: Publishes WeaponFireIntent with local ECS Entity handles (Shooter and Target)
    /// instead of long network IDs. NetworkEntityMap is no longer needed here; network-ID
    /// resolution is deferred to WeaponFireIntentEgressTranslator at the network boundary.
    ///
    /// Phase 0 Adaptation: Uses SimTransform for position, consistent with the Phase 0 refactor.
    /// </summary>
    public sealed class AimAndFireExecutor : IActionExecutor<WeaponChannel>
    {
        /// <summary>
        /// ⭐ <c>CE-2075</c> — how long after being hit a <see cref="RoeFire.ReturnFire"/> unit may fire. 5 s is the window the
        /// design's own SOP example uses (<c>DESIGN_Decision_Layer.md</c> §4.3, "was hit within 5 s"). ⚠ A constant until the
        /// ROE carries its own window. ⭐ <c>CE-3064</c> (R-206): a near miss counts as being fired upon too.
        /// </summary>
        public const double ReturnFireWindowSeconds = RoeOf.DefaultReturnFireWindowSeconds;   // ⭐ CE-2095: the DEFAULT; the unit's ROE may set its own

        /// <summary>⭐ <c>CE-2075</c> — may <paramref name="shooter"/> fire under its ROE right now?</summary>
        public static bool RoePermitsFire(EntityRepository world, Entity shooter)
        {
            if (!world.IsComponentTypeRegistered<Roe>()) return true;   // no ROE in this world ⇒ fire at will
            return RoeOf.Fire(world, shooter) switch
            {
                RoeFire.HoldFire   => false,
                // ⭐ CE-3064 (R-206) — fired upon = hit OR near-missed within the window; a heard shot alone is not.
                RoeFire.ReturnFire => world.IsComponentTypeRegistered<RecentSenses>()
                                      && (RecentSensesOf.Within(world, shooter, Fdp.Toolkit.Perception.Events.SensorChange.Hit, RoeOf.ReturnFireWindowSeconds(world, shooter))
                                          || RecentSensesOf.Within(world, shooter, Fdp.Toolkit.Perception.Events.SensorChange.NearMiss, RoeOf.ReturnFireWindowSeconds(world, shooter))),
                _                  => true,
            };
        }

        // OnEnter

        public unsafe void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            fixed (byte* dst = channel.State)
                *(Entity*)dst = p.Target;

            channel.Status = NodeStatus.Running;
        }

        // Execute

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            // ⭐ CE-466 rule (CombatLife): done when the target is gone OR knocked out (Health ≤ 0) — CE-267's revert keeps the
            //   body in the world, so existence alone never ends the action (CE-321: the squad emptied its rifles into a corpse).
            if (!CombatLife.IsAlive(world, p.Target))
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            ref var weapon = ref world.GetComponentRW<WeaponState>(entity);
            if (weapon.Ammo == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // Drain cooldown continuously each frame; fire only after cooldown reaches zero.
            if (weapon.CooldownSecondsRemaining > 0f)
            {
                weapon.CooldownSecondsRemaining -= dt;
                channel.Status = NodeStatus.Running;
                return;
            }

            // ⭐ CE-321 — hold fire while a friendly is on the line (LineOfFire): the action keeps running and no round is spent,
            //   so a unit that moves clear (or a friendly that moves away) fires again.
            if (LineOfFire.BlockedByFriendly(world, entity, p.Target))
            {
                channel.Status = NodeStatus.Running;
                return;
            }

            // ⭐ CE-2075 (R-200) — the unit's ROE, enforced ONCE here so no behaviour can forget it. HoldFire never fires;
            //   ReturnFire fires only within ReturnFireWindowSeconds of being hit. Holds like the friendly-line guard (Running, no
            //   round spent), so an ROE change or a fresh hit resumes fire without re-issuing the action.
            if (!RoePermitsFire(world, entity))
            {
                channel.Status = NodeStatus.Running;
                return;
            }

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = entity,
                Target      = p.Target,
                WeaponIndex = 0,
            });

            weapon.Ammo--;
            weapon.CooldownSecondsRemaining = p.CooldownSeconds;

            channel.Status = NodeStatus.Running;
        }

        // OnExit

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // No state to clean up.
        }
    }
}
