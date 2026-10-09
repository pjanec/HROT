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
            AimTimer(ref channel) = default;   // ⭐ CE-3136 P-3 — a new action aims afresh
            RoundsFired(ref channel) = 0;      // ⭐ CE-3136 B7

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

            // ⭐ CE-3136 B7 — the asked rounds are fired: done.
            if (p.Rounds > 0 && RoundsFired(ref channel) >= p.Rounds)
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            // ⭐ CE-3089 (G7) — the mount that fires: chosen per shot (Auto), a named mount, or the primary (0, the default). Its
            //   ammo and cooldown live on ITS WeaponState — the owner for mount 0, the mount child otherwise.
            Entity mountEntity = entity;
            int mountIndex = p.Mount == AimAndFireParams.MountAuto ? WeaponChoice.Choose(world, entity, p.Target, out mountEntity)
                           : p.Mount == 0 ? 0
                           : MountByIndex(world, entity, p.Mount, out mountEntity);
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (weapon.Ammo == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // ⭐⭐ CE-3136 P-3 (peek-and-fire D3 + D4) — an aimed shot needs the target SEEN NOW (D4), continuously for the aim time
            //   (D3). The aim clock runs beside the cooldown, never after it; it restarts on lost sight, a new target or the
            //   shooter's stance change, and OnEnter starts it fresh. Once aimed, later rounds need only the cooldown. Not seen ⇒
            //   hold (Running, no round spent), like the ROE hold. Blind fire (FireAtPoint) has neither gate.
            ref var aim = ref AimTimer(ref channel);
            bool seen = Fdp.Toolkit.Perception.SightNow.Sees(world, entity, p.Target);
            if (!seen)
                aim = default;
            else
            {
                byte stance = (byte)ShooterStance(world, entity);
                if (aim.Target != p.Target || aim.Stance != stance)
                    aim = new AimState { Target = p.Target, Stance = stance };
                if (aim.Ready == 0)
                {
                    aim.Elapsed += dt;
                    if (aim.Elapsed >= AimSecondsFor(world, entity, mountIndex)) aim.Ready = 1;
                }
            }

            // Drain cooldown continuously each frame; fire only after cooldown reaches zero.
            if (weapon.CooldownSecondsRemaining > 0f)
            {
                weapon.CooldownSecondsRemaining -= dt;
                channel.Status = NodeStatus.Running;
                return;
            }

            if (!seen || aim.Ready == 0)
            {
                channel.Status = NodeStatus.Running;
                return;
            }

            // ⭐ CE-3136 P-5 (D9) — an empty magazine RELOADS instead of failing (the dispatcher runs the reload every frame, so it
            //   also finishes while the unit is hidden); the action holds meanwhile. Out of ammunition altogether is still Failure.
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
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
                WeaponIndex = mountIndex,
            });

            Magazine.Spend(ref weapon);   // ⭐ CE-3136 P-5 — the total and the magazine; an emptied magazine starts its reload
            weapon.CooldownSecondsRemaining = p.CooldownSeconds;

            int fired = ++RoundsFired(ref channel);
            channel.Status = p.Rounds > 0 && fired >= p.Rounds ? NodeStatus.Success : NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 — the aim timer, in the channel state after the target (bytes 8–23 of 32).</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct AimState
        {
            public Entity Target;
            public float  Elapsed;
            public byte   Stance;
            public byte   Ready;
        }

        /// <summary>⭐ <c>CE-3136</c> B7 — the rounds the CURRENT AimAndFire action has fired (valid once the dispatcher has entered it).</summary>
        public static int RoundsFiredOf(ref WeaponChannel channel) => RoundsFired(ref channel);

        /// <summary>⭐ <c>CE-3136</c> B7 — rounds fired by this action, in the channel state after the aim timer (bytes 24–27).</summary>
        private static unsafe ref int RoundsFired(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<int>(s + sizeof(Entity) + sizeof(AimState));
        }

        private static unsafe ref AimState AimTimer(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<AimState>(s + sizeof(Entity));
        }

        private static Fdp.Toolkit.Tkb.Domain.StanceId ShooterStance(EntityRepository world, Entity e)
            => world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>()
                ? Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(world, e)
                : Fdp.Toolkit.Tkb.Domain.StanceId.Standing;

        /// <summary>The fired mount's aim time (TKB, by type), else <c>EngineFallbacks.AimSeconds</c>.</summary>
        public static float AimSecondsFor(EntityRepository world, Entity shooter, int mountIndex)
            => Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.AimSecondsOrFallback(
                   CombatTkb.MountOf(world, shooter, mountIndex)?.AimSeconds ?? 0f);

        /// <summary>The mount child with <paramref name="index"/> (its WeaponState entity); mount 0 on the owner when there is none.</summary>
        private static int MountByIndex(EntityRepository world, Entity owner, int index, out Entity mount)
        {
            mount = owner;
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return 0;
            System.Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, owner, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == index
                    && world.HasComponent<WeaponState>(mounts[i]))
                {
                    mount = mounts[i];
                    return index;
                }
            return 0;
        }

        // OnExit

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // No state to clean up.
        }
    }
}
