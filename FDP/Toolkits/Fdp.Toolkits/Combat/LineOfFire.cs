using System.Numerics;
using Fdp.Core;
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
                if (e == shooter || e == target) continue;
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
    }
}
