using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-321</c> — <b>a unit never fires through a friendly.</b> 🔒 User, <c>2026-10-04</c>: "A+B approved" (A = this hold-fire
    /// rule). A friendly is any OTHER entity of the shooter's force (<see cref="EntityInfo.ForceId"/>) with a
    /// <see cref="PhysicsCollider"/> — alive or wrecked (a dead hull still stops a round). The test is the same flat
    /// segment-against-circle sweep the bullet's own raycast resolves against.
    /// <para>📌 Found by <c>UrbanCombatNew</c>: dismounted soldiers emptied their rifles into their own APC (hp 475 → 0).</para>
    /// </summary>
    public static class LineOfFire
    {
        /// <summary>True when a friendly collider lies on the line from <paramref name="shooter"/> to <paramref name="target"/>.
        /// False when either end has no position or the shooter has no force (nothing to be friendly with).</summary>
        public static bool BlockedByFriendly(EntityRepository world, Entity shooter, Entity target)
        {
            if (!world.HasComponent<SimTransform>(target)) return false;
            return Blocked(world, shooter, target, world.GetComponentRO<SimTransform>(target).Position);
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G3 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — the same test to a POINT: a burst at a remembered spot, a
        /// round at a ground point. ⚠ XY only, like the entity form (a friend below a shot on a slope counts as blocking —
        /// conservative); both move behind <c>IWorldQuery.Trace(Fire)</c> with WQ-F (§10.8).
        /// </summary>
        public static bool BlockedByFriendly(EntityRepository world, Entity shooter, Vector3 point)
            => Blocked(world, shooter, Entity.Null, point);

        /// <summary>
        /// ⭐ <c>AW-20</c> (R-2026; docs/DESIGN_Civilians_And_Crowd_Control.md §5h) — <b>a neutral in the line of fire or within the weapon's
        /// spread holds the shot.</b> A neutral is a living PERSON (<see cref="PhysicsColliderReaders.IsPerson"/>) of
        /// <see cref="ForceId.Neutral"/> other than the target, when the shooter is not neutral himself (then he is a friend, and
        /// <see cref="BlockedByFriendly(EntityRepository, Entity, Entity)"/> holds). His circle is widened by the spread at his range:
        /// <paramref name="sigma"/> (<see cref="HitModel.Sigma"/>, the half-width of the uniform cone) × his distance along the line.
        /// <para>⭐ AW-24: the one new rule a soldier obeys, and it costs him one pass over the colliders — empty of neutrals in a
        /// military fight. ⛔ A wall, a wreck, a dropped weapon (default <see cref="ForceId.Neutral"/>) never holds it: people only.</para>
        /// </summary>
        public static bool BlockedByNeutral(EntityRepository world, Entity shooter, Entity target, float sigma)
        {
            if (!world.HasComponent<SimTransform>(target)) return false;
            return NeutralOnLine(world, shooter, target, world.GetComponentRO<SimTransform>(target).Position, sigma);
        }

        /// <summary>⭐ <c>AW-20</c> — the same hold toward a POINT (a burst at a remembered spot).</summary>
        public static bool BlockedByNeutral(EntityRepository world, Entity shooter, Vector3 point, float sigma)
            => NeutralOnLine(world, shooter, Entity.Null, point, sigma);

        /// <summary>A round that is not a warhead is held when the terrain stops it this close to the muzzle — the frame he leans out of.</summary>
        public const float MuzzleClearanceM = 2f;

        /// <summary>
        /// ⭐ <c>CE-2218</c> — <b>a shooter never fires into the wall in front of his own muzzle.</b> True when the terrain (a wall, a window
        /// frame, a floor) STOPS the round — by the one penetration rule (<see cref="TerrainPenetration.Carry"/>) — within its danger
        /// distance of where it leaves (<see cref="ShotOrigin"/>): <see cref="MuzzleClearanceM"/>, or a warhead's blast-injury radius (it would
        /// burst on him). The eye's line passing the window does not make the barrel's.
        /// <para>📐 Live <c>ua-rpg-window</c>: an RPG man at an upper window aimed steeply down at an IFV 10 m off; the rocket stopped 5 cm
        /// from the muzzle, burst, and killed him. ⛔ Not for an ARC (the throw already lobs over what stops it — <c>ThrowArc</c>) and not for
        /// a bomb; with no terrain, never.</para>
        /// </summary>
        public static bool MuzzleMasked(EntityRepository world, Entity shooter, int weaponIndex, long loadedAmmo, Vector3 aim)
        {
            if (Fdp.Toolkit.World.WorldQuery.Of(world) is not { } terrain || !world.HasComponent<SimTransform>(shooter)) return false;
            var mount = CombatTkb.MountOf(world, shooter, weaponIndex);
            long ammo = loadedAmmo != 0 ? loadedAmmo : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            var warhead = CombatTkb.WarheadOf(world, ammo);
            if (warhead is { Indirect: true } || mount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb) return false;

            var eye = ShotOrigin.Eye(world, shooter, out _);
            var origin = ShotOrigin.From(world, shooter, weaponIndex, eye, aim, out _);
            return Stops(world, terrain, mount, ammo, warhead, origin, aim);
        }

        /// <summary>
        /// ⭐ <c>CE-2327</c> (R-2064) — the same hold for a round that WOULD leave from <paramref name="origin"/> (an eye at a spot he is not
        /// at yet: the firing-position search marks the spots a round clears from). ⛔ The barrel is not placed — a spot has no pose yet; a
        /// rifleman's round leaves from his eye anyway (<see cref="ShotOrigin"/>).
        /// </summary>
        public static bool MaskedFrom(EntityRepository world, Fdp.Toolkit.World.IWorldQuery terrain, Entity shooter, int weaponIndex,
                                      Vector3 origin, Vector3 aim)
        {
            var mount = CombatTkb.MountOf(world, shooter, weaponIndex);
            long ammo = mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            var warhead = CombatTkb.WarheadOf(world, ammo);
            if (warhead is { Indirect: true } || mount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb) return false;
            return Stops(world, terrain, mount, ammo, warhead, origin, aim);
        }

        /// <summary>The terrain stops the round within its danger distance of <paramref name="origin"/> on its way to <paramref name="aim"/>.</summary>
        private static bool Stops(EntityRepository world, Fdp.Toolkit.World.IWorldQuery terrain, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount, long ammo,
                                  Fdp.Toolkit.Tkb.Domain.WarheadDto? warhead, Vector3 origin, Vector3 aim)
        {
            var line = aim - origin;
            float length = line.Length();
            if (length < 1e-3f) return false;
            float danger = MathF.Max(MuzzleClearanceM, warhead is { HasAreaEffect: true } ? warhead.BlastInjuryRadiusM : 0f);
            var end = origin + line * (MathF.Min(danger, length) / length);

            var (penetration, _) = CombatTkb.RoundOf(world, mount, ammo);
            float damage = 1f;
            return TerrainPenetration.Carry(terrain, origin, end, ref damage, ref penetration, out _);
        }

        private static bool Blocked(EntityRepository world, Entity shooter, Entity target, Vector3 b3)
        {
            if (!world.IsComponentTypeRegistered<PhysicsCollider>() || !world.IsComponentTypeRegistered<EntityInfo>()) return false;
            if (!world.HasComponent<SimTransform>(shooter)) return false;
            if (!world.HasComponent<EntityInfo>(shooter)) return false;
            var force = world.GetComponentRO<EntityInfo>(shooter).ForceId;
            var a3 = world.GetComponentRO<SimTransform>(shooter).Position;
            var a = new Vector2(a3.X, a3.Y);
            var ab = new Vector2(b3.X, b3.Y) - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-6f) return false;

            foreach (var e in world.Query().With<PhysicsCollider>().With<SimTransform>().With<EntityInfo>().Build())
            {
                if (e == shooter || e == target || RidesWith(world, shooter, e)) continue;
                if (world.GetComponentRO<EntityInfo>(e).ForceId != force) continue;
                float r = world.GetComponentRO<PhysicsCollider>(e).Radius;
                if (r <= 0f) continue;
                var p3 = world.GetComponentRO<SimTransform>(e).Position;
                var ap = new Vector2(p3.X, p3.Y) - a;
                float t = Vector2.Dot(ap, ab) / len2;
                if (t <= 0f || t >= 1f) continue;                 // behind the shooter or beyond the target
                var closest = ab * t;
                if (Vector2.DistanceSquared(closest, ap) < r * r) return true;
            }
            return false;
        }

        private static bool NeutralOnLine(EntityRepository world, Entity shooter, Entity target, Vector3 b3, float sigma)
        {
            if (!world.IsComponentTypeRegistered<PhysicsCollider>() || !world.IsComponentTypeRegistered<EntityInfo>()) return false;
            if (!world.HasComponent<SimTransform>(shooter) || !world.HasComponent<EntityInfo>(shooter)) return false;
            if (world.GetComponentRO<EntityInfo>(shooter).ForceId == ForceId.Neutral) return false;   // a friend to them: BlockedByFriendly
            var a3 = world.GetComponentRO<SimTransform>(shooter).Position;
            var a = new Vector2(a3.X, a3.Y);
            var ab = new Vector2(b3.X, b3.Y) - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-6f) return false;
            float len = MathF.Sqrt(len2);
            bool health = world.IsComponentTypeRegistered<Health>();

            foreach (var e in world.Query().With<PhysicsCollider>().With<SimTransform>().With<EntityInfo>().Build())
            {
                if (e == shooter || e == target) continue;
                if (!Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(world, e) || !PhysicsColliderReaders.IsPerson(world, e)) continue;
                if (health && world.HasComponent<Health>(e) && world.GetComponentRO<Health>(e).Current <= 0f) continue;
                var p3 = world.GetComponentRO<SimTransform>(e).Position;
                var ap = new Vector2(p3.X, p3.Y) - a;
                float t = Vector2.Dot(ap, ab) / len2;
                if (t <= 0f || t >= 1f) continue;                 // behind the shooter or beyond the target
                float r = world.GetComponentRO<PhysicsCollider>(e).Radius + MathF.Max(0f, sigma) * t * len;
                if (Vector2.DistanceSquared(ab * t, ap) < r * r) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ Weapon systems WS-3 — <paramref name="e"/> rides with <paramref name="shooter"/>: the vehicle it is aboard, a passenger of it, or
        /// a fellow passenger of its vehicle (the pickup, its gunner, the M2 in its bed). They move as one body and are never "on the line":
        /// a station weapon stands off its host's reference point, where the crew rides.
        /// </summary>
        /// <summary>⭐ Also the impact rule's (<c>DroneImpactSystem</c>, CE-2173 slice 6d): a carrier and its riders never meet each other.</summary>
        internal static bool RidesWith(EntityRepository world, Entity shooter, Entity e)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()) return false;
            Entity shooterHost = world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(shooter)
                ? world.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(shooter).VehicleEntity : Entity.Null;
            if (!shooterHost.IsNull && e == shooterHost) return true;
            if (!world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e)) return false;
            var host = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e).VehicleEntity;
            return host == shooter || (!shooterHost.IsNull && host == shooterHost);
        }
    }
}
