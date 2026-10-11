using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
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
                ReleaseTurrets(world, entity);   // ⭐ WL-3 — the turret stops following a dead target
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            // ⭐ CE-3136 B7 — the asked rounds are fired: done.
            if (p.Rounds > 0 && RoundsFired(ref channel) >= p.Rounds)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            // ⭐ CE-3089 (G7) — the mount that fires: chosen per shot (Auto), a named mount, or the primary (0, the default). Its
            //   ammo and cooldown live on ITS WeaponState — the owner for mount 0, the mount child otherwise.
            // ⭐ WL-4 W-12a (Weapon_Layers §5f L-4) — Auto chooses ONCE per engagement (a new target, or the chosen mount ran
            //   dry), not per shot: a per-frame flip started a new turret epoch and lost a round the muscle had fired (§5e).
            Entity mountEntity = entity;
            int mountIndex = p.Mount == AimAndFireParams.MountAuto ? EngagementMount(world, entity, ref channel, p.Target, out mountEntity)
                           : p.Mount == 0 ? 0
                           : MountByIndex(world, entity, p.Mount, out mountEntity);
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (weapon.Ammo == 0)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Failure;
                Say(world, entity, ActionReason.OutOfAmmo, p.Target);
                return;
            }

            // ⭐⭐ WL-3 (DESIGN_Weapon_Layers §5e, W-1/W-4) — a turreted mount is fought through its turret's WeaponTask: this
            //   weapon brain keeps the gates (target, rounds, ammo, magazine, ROE, seen-now) and GRANTS rounds; the weapon muscle
            //   aims, times and fires them. The profile decides (W-5); the per-shot path below is unchanged for the rest.
            var mountDto = CombatTkb.MountOf(world, entity, mountIndex);
            if (WeaponBrainProfiles.For(mountDto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown).UsesTurretTask(world, entity, mountDto))
            {
                TurretPath(entity, ref channel, world, in p, mountIndex, TurretParts.Find(world, entity, mountDto!.TurretIndex), ref weapon);
                return;
            }

            // ⭐⭐ CE-3136 P-3 (peek-and-fire D3 + D4) — an aimed shot needs the target SEEN NOW (D4), continuously for the aim time
            //   (D3). The aim clock runs beside the cooldown, never after it; it restarts on lost sight, a new target or the
            //   shooter's stance change, and OnEnter starts it fresh. Once aimed, later rounds need only the cooldown. Not seen ⇒
            //   hold (Running, no round spent), like the ROE hold. Blind fire (FireAtPoint) has neither gate.
            ref var aim = ref AimTimer(ref channel);
            bool seen = Fdp.Toolkit.Perception.SightNow.Sees(world, entity, p.Target);
            // ⭐⭐ WL-4 W-12e (Weapon_Layers §5f L-2) — not seen: the weapon brain may THROW at where the target was last seen (a
            //   grenade over the cover it hides behind). It counts as a round of this action; nothing else changes.
            if (!seen && HiddenTargetThrow.TryThrow(world, entity, p.Target))
            {
                aim = aim.Restarted(default, 0);
                int thrown = ++RoundsFired(ref channel);
                channel.Status = p.Rounds > 0 && thrown >= p.Rounds ? NodeStatus.Success : NodeStatus.Running;
                Say(world, entity, ActionReason.Throwing, p.Target, thrown, p.Rounds);
                return;
            }
            if (!seen)
                aim = aim.Restarted(default, 0);
            else
            {
                byte stance = (byte)ShooterStance(world, entity);
                if (aim.Target != p.Target || aim.Stance != stance)
                    aim = aim.Restarted(p.Target, stance);
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
                if (!seen && p.BlindFire != 0) Say(world, entity, ActionReason.BlindFire, p.Target, RoundsFired(ref channel), p.Rounds);
                else if (!seen || aim.Ready == 0) SayAim(world, entity, p.Target, seen, in aim, mountIndex);
                else Say(world, entity, ActionReason.Cooldown, p.Target, weapon.CooldownSecondsRemaining);
                return;
            }

            // ⭐⭐ AQ89 A (R-269) — SUPPRESS: not seen and nothing thrown ⇒ slow rounds at where the memory last placed the target,
            //   instead of holding. They strike the cover he hides behind and stamp UnderFire on him (a near miss — AQ85 E).
            if (!seen && p.BlindFire != 0)
            {
                FireBlind(entity, ref channel, world, in p, mountIndex, mountDto, ref weapon);
                return;
            }

            if (!seen || aim.Ready == 0)
            {
                channel.Status = NodeStatus.Running;
                SayAim(world, entity, p.Target, seen, in aim, mountIndex);
                return;
            }

            // ⭐ CE-3136 P-5 (D9) — an empty magazine RELOADS instead of failing (the dispatcher runs the reload every frame, so it
            //   also finishes while the unit is hidden); the action holds meanwhile. Out of ammunition altogether is still Failure.
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.Reloading, p.Target, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
                return;
            }

            // ⭐ CE-321 — hold fire while a friendly is on the line (LineOfFire): the action keeps running and no round is spent,
            //   so a unit that moves clear (or a friendly that moves away) fires again.
            if (LineOfFire.BlockedByFriendly(world, entity, p.Target))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.HoldFriendlyOnLine, p.Target);
                return;
            }

            // ⭐ CE-2075 (R-200) — the unit's ROE, enforced ONCE here so no behaviour can forget it. HoldFire never fires;
            //   ReturnFire fires only within ReturnFireWindowSeconds of being hit. Holds like the friendly-line guard (Running, no
            //   round spent), so an ROE change or a fresh hit resumes fire without re-issuing the action.
            if (!RoePermitsFire(world, entity))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.HoldRoe, p.Target);
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
            Say(world, entity, ActionReason.Firing, p.Target, fired, p.Rounds);
        }

        /// <summary>⭐ AQ89 A — a blind round aims this far above the remembered ground point: a crouched man's middle, so it strikes
        /// a wall he crouches behind (a near miss) rather than flying over it.</summary>
        public const float BlindAimHeightMetres = 0.5f;

        /// <summary>⭐ AQ89 A — blind rounds go this many times slower than aimed ones (bursts with pauses, not a magazine dump).</summary>
        public const float BlindCooldownFactor = 2.5f;

        /// <summary>
        /// ⭐⭐ AQ89 A (R-269) — one suppressive round at the hidden target's last-known point: the at-point path a peek's blind burst
        /// flies (<see cref="WeaponFireIntent.AtPoint"/>), with the aimed shot's gates — a live, identified memory slot
        /// (<see cref="HiddenTargetThrow.LastKnown"/>), the magazine, the friendly line to the POINT, the ROE. An indirect mount
        /// never fires blind (that is the throw's decision, W-12e). It counts as a round of this action.
        /// </summary>
        private static void FireBlind(Entity entity, ref WeaponChannel channel, EntityRepository world, in AimAndFireParams p,
                                      int mountIndex, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mountDto, ref WeaponState weapon)
        {
            channel.Status = NodeStatus.Running;
            if (CombatTkb.WarheadOf(world, mountDto) is { Indirect: true } || !HiddenTargetThrow.LastKnown(world, entity, p.Target, out var point))
            {
                Say(world, entity, ActionReason.HoldNotSeen, p.Target);
                return;
            }
            point.Z += BlindAimHeightMetres;
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
                Say(world, entity, ActionReason.Reloading, p.Target, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
                return;
            }
            if (LineOfFire.BlockedByFriendly(world, entity, point)) { Say(world, entity, ActionReason.HoldFriendlyOnLine, p.Target); return; }
            if (!RoePermitsFire(world, entity)) { Say(world, entity, ActionReason.HoldRoe, p.Target); return; }

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = entity,
                Target      = Entity.Null,
                WeaponIndex = mountIndex,
                AtPoint     = true,
                TargetPoint = point,
            });
            Magazine.Spend(ref weapon);
            weapon.CooldownSecondsRemaining = MathF.Max(p.CooldownSeconds, 0.1f) * BlindCooldownFactor;
            int fired = ++RoundsFired(ref channel);
            if (p.Rounds > 0 && fired >= p.Rounds) channel.Status = NodeStatus.Success;
            Say(world, entity, ActionReason.BlindFire, p.Target, fired, p.Rounds);
        }

        /// <summary>
        /// ⭐ WL-3 (§5e) — the turret path. The task's epoch moves only with a new target or mount, so a Track ↔ Engage switch keeps
        /// it and the muscle's <see cref="WeaponTaskStatus.RoundsFired"/> keeps counting; every round it reports is spent HERE once
        /// (magazine, total, the action's count — W-4). The grant is what was fired under this epoch plus what the magazine holds
        /// now (capped by the rounds asked), so it stays constant while rounds are in flight and drops to Track for a reload.
        /// </summary>
        private static void TurretPath(Entity entity, ref WeaponChannel channel, EntityRepository world, in AimAndFireParams p,
                                       int mountIndex, Entity part, ref WeaponState weapon)
        {
            var current = world.HasComponent<WeaponTask>(part) ? world.GetComponent<WeaponTask>(part) : default;
            bool fresh = current.Mode == WeaponTaskMode.Idle || current.Target != p.Target || current.MountIndex != (byte)mountIndex;
            uint epoch = fresh ? current.Epoch + 1 : current.Epoch;
            ref int accounted = ref Accounted(ref channel);
            if (fresh) accounted = 0;

            var status = world.IsComponentTypeRegistered<WeaponTaskStatus>() && world.HasComponent<WeaponTaskStatus>(part)
                ? world.GetComponent<WeaponTaskStatus>(part) : default;
            bool mine = status.Epoch == epoch && !fresh;
            if (mine)
            {
                for (int n = status.RoundsFired - accounted; n > 0 && weapon.Ammo > 0; n--)
                {
                    Magazine.Spend(ref weapon);
                    RoundsFired(ref channel)++;
                }
                if (status.RoundsFired > accounted) accounted = status.RoundsFired;
                if (status.State == WeaponTaskState.Failed)
                {
                    ReleaseTurrets(world, entity);
                    channel.Status = NodeStatus.Failure;
                    Say(world, entity, status.Reason == WeaponTaskFailure.OutOfArc ? ActionReason.OutOfArc : ActionReason.Failed, p.Target);
                    return;
                }
            }
            if (p.Rounds > 0 && RoundsFired(ref channel) >= p.Rounds)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            bool seen = Fdp.Toolkit.Perception.SightNow.Sees(world, entity, p.Target);
            bool roe = RoePermitsFire(world, entity);
            Magazine.StartIfEmpty(ref weapon);
            bool loaded = Magazine.Ready(weapon);
            int grant = accounted;
            if (seen && roe && loaded)
            {
                int inHand = weapon.MagazineSize > 0 ? weapon.MagazineRounds : weapon.Ammo;
                if (p.Rounds > 0) inHand = System.Math.Min(inHand, p.Rounds - RoundsFired(ref channel));
                grant += System.Math.Max(0, inHand);
            }

            var next = new WeaponTask
            {
                Mode = grant > accounted ? WeaponTaskMode.Engage : WeaponTaskMode.Track,
                Target = p.Target,
                MountIndex = (byte)mountIndex,
                RoundsGranted = (ushort)System.Math.Min(grant, ushort.MaxValue),
                CooldownSeconds = p.CooldownSeconds,
                Epoch = epoch,
            };
            if (fresh || !current.Equals(next)) world.SetComponent(part, next);

            channel.Status = NodeStatus.Running;
            if (!seen) Say(world, entity, ActionReason.HoldNotSeen, p.Target);
            else if (!roe) Say(world, entity, ActionReason.HoldRoe, p.Target);
            else if (!loaded) Say(world, entity, ActionReason.Reloading, p.Target, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
            else if (mine && status.State == WeaponTaskState.Slewing) Say(world, entity, ActionReason.Slewing, p.Target);
            else Say(world, entity, ActionReason.Firing, p.Target, RoundsFired(ref channel), p.Rounds);
        }

        /// <summary>⭐ WL-3 — every turret of <paramref name="unit"/> with an order goes back to Idle (it holds its pose); the epoch is
        /// kept so the next order is a new one.</summary>
        public static void ReleaseTurrets(EntityRepository world, Entity unit)
        {
            if (!world.IsComponentTypeRegistered<WeaponTask>() || !world.IsComponentTypeRegistered<TurretPose>()) return;
            for (int i = 0; i < TurretParts.MaxTurrets; i++)
            {
                var part = TurretParts.Find(world, unit, i);
                if (part.IsNull) break;
                if (!world.HasComponent<WeaponTask>(part)) continue;
                ref var task = ref world.GetComponentRW<WeaponTask>(part);
                if (task.Mode != WeaponTaskMode.Idle) task = new WeaponTask { Mode = WeaponTaskMode.Idle, Epoch = task.Epoch };
            }
        }

        /// <summary>⭐ WL-3 — rounds of the current turret epoch already spent here (bytes 28–31, after the action's round count).</summary>
        private static unsafe ref int Accounted(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<int>(s + sizeof(Entity) + sizeof(AimState) + sizeof(int));
        }

        // ⭐ CE-3136 (T1) — the weapon row of the unit's ActionStatus: why this action is (not) firing this frame.
        private static void Say(EntityRepository world, Entity unit, ActionReason reason, Entity target, float progress = 0f, float needed = 0f)
            => ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdAimAndFire, reason, target, progress, needed);

        private static void SayAim(EntityRepository world, Entity unit, Entity target, bool seen, in AimState aim, int mountIndex)
        {
            if (!seen) Say(world, unit, ActionReason.HoldNotSeen, target);
            else Say(world, unit, ActionReason.Aiming, target, aim.Elapsed, AimSecondsFor(world, unit, mountIndex));
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 — the aim timer, in the channel state after the target (bytes 8–23 of 32).</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct AimState
        {
            public Entity Target;
            public float  Elapsed;
            public byte   Stance;
            public byte   Ready;
            /// <summary>⭐ WL-4 W-12a — the mount chosen for this engagement (the struct's two former padding bytes: still 16).</summary>
            public byte   ChosenMount;
            /// <summary>1 when <see cref="ChosenMount"/> holds a choice.</summary>
            public byte   HasChoice;

            /// <summary>A fresh aim (timer and readiness reset) that KEEPS the engagement's mount choice.</summary>
            public readonly AimState Restarted(Entity target, byte stance)
                => new AimState { Target = target, Stance = stance, ChosenMount = ChosenMount, HasChoice = HasChoice };
        }

        /// <summary>
        /// ⭐ WL-4 W-12a (L-4) — the mount for this engagement: the stored choice while the target is the one it was made for and
        /// the mount still has ammunition; otherwise <see cref="WeaponChoice.Choose"/> once more. The engaged target is the
        /// channel state's first field (written by <see cref="OnEnter"/>).
        /// </summary>
        private static unsafe int EngagementMount(EntityRepository world, Entity unit, ref WeaponChannel channel, Entity target, out Entity mountEntity)
        {
            ref var aim = ref AimTimer(ref channel);
            ref Entity engaged = ref EngagedTarget(ref channel);
            mountEntity = unit;
            if (aim.HasChoice != 0 && engaged == target)
            {
                int kept = aim.ChosenMount == 0 ? 0 : MountByIndex(world, unit, aim.ChosenMount, out mountEntity);
                if (kept == aim.ChosenMount && world.HasComponent<WeaponState>(mountEntity) && world.GetComponentRO<WeaponState>(mountEntity).Ammo > 0)
                    return kept;
            }
            int chosen = WeaponChoice.Choose(world, unit, target, out mountEntity);
            aim.ChosenMount = (byte)chosen;
            aim.HasChoice = 1;
            engaged = target;
            return chosen;
        }

        /// <summary>The target this action engages (channel state bytes 0–7).</summary>
        private static unsafe ref Entity EngagedTarget(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<Entity>(s);
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
            ReleaseTurrets(world, entity);   // ⭐ WL-3 (W-1) — releasing the channel sets the turret's task to Idle
        }
    }
}
