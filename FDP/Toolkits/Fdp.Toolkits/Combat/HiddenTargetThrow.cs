using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>WL-4</c> W-12e (📄 <c>docs/DESIGN_Weapon_Layers.md</c> §5f, L-2/L-3) — the WEAPON BRAIN's answer to a target it cannot
    /// see: throw a grenade at where its memory last placed it. 🔒 User U-W7: <i>"the choice of a rifle vs grenade if the target is
    /// hidden behind an obstacle and the attacker dare to come close enough"</i>. Called by <see cref="AimAndFireExecutor"/> when
    /// the target is not seen now; the behaviour never asks for a grenade (R-261: weapon choice is the weapon brain's).
    /// <para>All of: a throwable mount (an INDIRECT warhead thrown by hand, &lt; 40 m/s) with ammunition and cooled down · the
    /// target's slot in the unit's <see cref="TargetMemory"/> live and not anonymous · the point within the mount's range and the
    /// throw's reach · the ROE · no friendly — the thrower included — inside the warhead's lethal radius of the point. The throw
    /// is the at-point path every grenade already flies (<see cref="WeaponFireIntent.AtPoint"/>); the muscle lobs it over the
    /// cover (<see cref="ThrowArc"/>, L-1).</para>
    /// </summary>
    public static class HiddenTargetThrow
    {
        /// <summary>L-3 — a thrown mount waits this long before the next throw (an M67's 4.5 s fuze, then a look).</summary>
        public const float CooldownSeconds = 6f;

        /// <summary>
        /// Throws at <paramref name="target"/>'s last-known position when every condition holds; true when a grenade left this
        /// frame (its round ASKED — spent when the muscle answers, Q94 A-3 — and its cooldown set).
        /// </summary>
        public static bool TryThrow(EntityRepository world, Entity unit, Entity target)
        {
            if (!LastKnown(world, unit, target, out var point)) return false;
            // ⭐⭐ CE-2360 I-4 (AQ102 R2-4, R-2073) — WHICH grenade, by the rule (ChooseThrow), never simply the first mount
            if (!ChooseThrow(world, unit, target, point, out var mountEntity, out int mountIndex, out var mount, out var warhead)) return false;
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (!Magazine.Ready(in weapon) || weapon.CooldownSecondsRemaining > 0f) return false;   // ⭐ Q94 A-3 — net of the rounds asked
            if (ShotLedger.Held(in weapon, out _)) return false;   // ⭐ Q94 A — the muscle refused the last one: not again at once
            if (!world.HasComponent<SimTransform>(unit)) return false;
            var from = world.GetComponentRO<SimTransform>(unit).Position;
            float flat = new Vector2(point.X - from.X, point.Y - from.Y).Length();
            float reach = mount.MuzzleVelocity * mount.MuzzleVelocity / CombatConstants.Gravity;   // the level-ground maximum
            float range = mount.Range > 0f ? MathF.Min(mount.Range, reach) : reach;
            if (flat > range) return false;
            if (!AimAndFireExecutor.RoePermitsFire(world, unit)) return false;
            // ⭐ CE-2360 — the friend check over the ONE lethal radius (GrenadeRules): a blast radius alone left an M67's fragments out
            if (FriendlyInBurst(world, unit, point, GrenadeRules.LethalRadius(warhead))) return false;
            if (Fdp.Toolkit.Behavior.Components.EngagementAreas.IsNoFire(world, unit, point)) return false;   // ⭐ CE-2282 DA-L

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = unit,
                Target      = Entity.Null,
                WeaponIndex = mountIndex,
                AtPoint     = true,
                TargetPoint = point,
                RaisedAt    = HitModel.Now(world),   // ⭐ CE-2180
                RequestId   = ShotLedger.Ask(ref weapon),   // ⭐⭐ Q94 A-3 (R-3018) — asked; the muscle's answer spends it
            });
            weapon.CooldownSecondsRemaining = CooldownSeconds;
            return true;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2360</c> I-4 (AQ102 R2-4, R-2073) — the grenade to throw at <paramref name="point"/>, among the unit's throwable mounts with
        /// rounds: a FRAGMENTATION grenade only OUTDOORS and with no civilian he knows of in its lethal radius; INTO a building (through its
        /// door or window) a STUN only — the throw has no intensity order, and with none ordered it is Precision (R-2073 "no intensity
        /// ordered = Precision"; the room drill's HighIntensity rides its own <c>GrenadeCheck.Choose</c>); any other thrown round as before,
        /// outdoors; every one inside the unit's force level (<see cref="ForceLevels.Permits"/>, as every other round). The best kind wins —
        /// fragmentation, then stun, then the rest. False when none may go: he throws nothing. 🔒 User: <i>"there need to be some condition
        /// not to do that if the mission is to clear the houses from terrorists but saving all the civilians"</i>.
        /// </summary>
        public static bool ChooseThrow(EntityRepository world, Entity unit, Entity target, Vector3 point, out Entity mountEntity, out int mountIndex,
                                       out WeaponMountDto mount, out WarheadDto warhead)
        {
            mountEntity = Entity.Null; mountIndex = -1; mount = null!; warhead = null!;
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return false;
            bool indoors = GrenadeRules.Indoors(world, point, out int building);
            int best = -1;
            Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, unit, mounts);
            for (int i = 0; i < n; i++)
            {
                if (!Throwable(world, unit, mounts[i], out int index, out var dto, out var wh)) continue;
                var kind = GrenadeRules.KindOf(wh);
                int rank = kind switch
                {
                    GrenadeKind.Fragmentation => indoors || GrenadeRules.KnownNeutralNear(world, unit, point, GrenadeRules.LethalRadius(wh), building) ? -1 : 2,
                    GrenadeKind.Stun => 1,
                    _ => indoors ? -1 : 0,
                };
                if (rank <= best || !ForceLevels.Permits(world, unit, target, unchecked((long)dto.AmmoGuid), dto)) continue;
                best = rank;
                mountEntity = mounts[i]; mountIndex = index; mount = dto; warhead = wh;
            }
            return best >= 0;
        }

        /// <summary>A mount of <paramref name="unit"/> that throws an indirect warhead by hand and still has rounds.</summary>
        private static bool Throwable(EntityRepository world, Entity unit, Entity mountEntity, out int index, out WeaponMountDto dto, out WarheadDto warhead)
        {
            index = -1; dto = null!; warhead = null!;
            if (!world.HasComponent<WeaponMountInfo>(mountEntity) || !world.HasComponent<WeaponState>(mountEntity)) return false;
            index = world.GetComponentRO<WeaponMountInfo>(mountEntity).MountIndex;
            var mount = CombatTkb.MountOf(world, unit, index);
            if (mount == null || mount.MuzzleVelocity <= 0f || mount.MuzzleVelocity >= CombatConstants.HighArcMinMuzzleVelocity) return false;
            if (CombatTkb.WarheadOf(world, mount) is not { Indirect: true } wh) return false;
            if (world.GetComponentRO<WeaponState>(mountEntity).Ammo == 0) return false;
            dto = mount; warhead = wh;
            return true;
        }

        /// <summary>The unit's first mount that throws an indirect warhead by hand and still has rounds (does he carry any throwable at all —
        /// the posture's approach gate; WHICH one is thrown is <see cref="ChooseThrow"/>'s).</summary>
        public static bool ThrowableMount(EntityRepository world, Entity unit, out Entity mountEntity, out int mountIndex,
                                          out WeaponMountDto mount, out WarheadDto warhead)
        {
            mountEntity = Entity.Null; mountIndex = -1; mount = null!; warhead = null!;
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return false;
            Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, unit, mounts);
            for (int i = 0; i < n; i++)
            {
                if (!Throwable(world, unit, mounts[i], out int index, out var dto, out var wh)) continue;
                mountEntity = mounts[i]; mountIndex = index; mount = dto; warhead = wh;
                return true;
            }
            return false;
        }

        /// <summary>Where <paramref name="unit"/>'s memory last placed <paramref name="target"/> — a live, identified slot only.</summary>
        public static bool LastKnown(EntityRepository world, Entity unit, Entity target, out Vector3 point)
            => LastKnown(world, unit, target, out point, out _);

        /// <summary>⭐ <c>CE-2326</c> (R-3030; Peek and Fire §11.6a) — the same, and the stance he was last SEEN in
        /// (<see cref="TargetMemory.SeenStance"/>: <c>StanceId + 1</c>, 0 = unknown), so a blind round is aimed as at that body.</summary>
        public static unsafe bool LastKnown(EntityRepository world, Entity unit, Entity target, out Vector3 point, out byte seenStance)
        {
            point = default;
            seenStance = 0;
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(unit)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(unit);
            long id = (long)target.PackedValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] != id || TargetMemory.IsAnonymous(in mem, i)) continue;
                // ⭐ CE-2371 (AQ104 D) — an ALERTED contact counts as live for the hunt: the man follows him to where he was last placed
                if (!ThreatFreshness.IsLive(world, unit, in mem, i)
                    && !Fdp.Toolkit.Perception.Components.Alerts.IsPinned(world, unit, id, world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d)) return false;
                point = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                seenStance = mem.SeenStance[i];
                return true;
            }
            return false;
        }

        /// <summary>L-2 — any entity of the thrower's force (the thrower too) within <paramref name="radius"/> of the burst.</summary>
        public static bool FriendlyInBurst(EntityRepository world, Entity unit, Vector3 point, float radius)
            => FriendlyInBurst(world, unit, point, radius, neutralsToo: false);

        /// <summary>⭐ <c>CE-2173</c> slice 6c (W3, R-2020; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9e) — the same test, and with
        /// <paramref name="neutralsToo"/> a <see cref="ForceId.Neutral"/> unit counts as well: the air attack's collateral check (a weapon
        /// is out when a friend OR a neutral stands within its burst).</summary>
        public static bool FriendlyInBurst(EntityRepository world, Entity unit, Vector3 point, float radius, bool neutralsToo)
        {
            if (radius <= 0f || !world.IsComponentTypeRegistered<EntityInfo>() || !world.HasComponent<EntityInfo>(unit)) return false;
            var force = world.GetComponentRO<EntityInfo>(unit).ForceId;
            float r2 = radius * radius;
            foreach (var e in world.Query().With<EntityInfo>().With<SimTransform>().Build())
            {
                var theirs = world.GetComponentRO<EntityInfo>(e).ForceId;
                if (theirs != force && !(neutralsToo && theirs == ForceId.Neutral)) continue;
                if (!CombatLife.IsAlive(world, e)) continue;
                var p = world.GetComponentRO<SimTransform>(e).Position;
                if (new Vector2(p.X - point.X, p.Y - point.Y).LengthSquared() < r2) return true;
            }
            return false;
        }
    }
}
