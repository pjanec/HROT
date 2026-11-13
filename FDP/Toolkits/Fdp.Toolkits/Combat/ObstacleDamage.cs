using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-6003</c> (city battle O-1; 📄 docs/DESIGN_Peek_And_Fire.md §9.9) — what damages a destructible STATIC OBSTACLE and the seam every
    /// remover calls. ⭐ An obstacle is TERRAIN (R-243): the person / hull law of <c>AreaEffectSystem</c> does not fit it — its body points lie inside its
    /// own prism (the fragment trace crosses its own concrete) and the generic blast damage is the same for every warhead at contact (a grenade would
    /// erase a concrete block as an 8 kg charge does). So a blast drains an obstacle by the CHARGE and the CLOSENESS, the scaled-distance form the wall
    /// breach already uses (<see cref="BreachRule"/>: Z = d / ∛kg): <see cref="BlastDamage"/>. Fragments and bullets do nothing to it.
    /// <para>⭐ The damage still travels the ONE path: a <see cref="DamageAssessedEvent"/> to its <see cref="Health"/> owner (R-171), which
    /// <c>HealthApplicationSystem</c> applies; at 0 <c>ObstacleRemovalSystem</c> removes the entity. A bulldozer's push (<c>CE-7012</c>) calls
    /// <see cref="Damage"/> once per tick of contact — the same event, no new path.</para>
    /// </summary>
    public static class ObstacleDamage
    {
        /// <summary>Hit points one kilogram of explosive (TNT-equivalent) takes off an obstacle at contact. ⚠ A placeholder engineering value
        /// (the obstacle types' <c>MaxHealth</c> is stated in the same unit: a concrete block's 600 = a 6 kg contact charge).</summary>
        public const float DamagePerKg = 100f;

        /// <summary>Scaled distance (m / kg^⅓) up to which a charge does its full damage (contact / tamped).</summary>
        public const float FullZ = 0.5f;

        /// <summary>Scaled distance (m / kg^⅓) beyond which a charge does nothing to an obstacle (linear between).</summary>
        public const float NoneZ = 1.5f;

        /// <summary>
        /// The hit points a burst of <paramref name="explosiveKg"/> takes off an obstacle whose nearest face is <paramref name="distanceToFace"/> m away:
        /// <c>DamagePerKg · kg · f(Z)</c>, Z = d / ∛kg, f = 1 up to <see cref="FullZ"/>, 0 from <see cref="NoneZ"/>. Pure, no allocation.
        /// </summary>
        public static float BlastDamage(float explosiveKg, float distanceToFace)
        {
            if (explosiveKg <= 0f) return 0f;
            float z = MathF.Max(0f, distanceToFace) / MathF.Cbrt(explosiveKg);
            if (z >= NoneZ) return 0f;
            float f = z <= FullZ ? 1f : (NoneZ - z) / (NoneZ - FullZ);
            return DamagePerKg * explosiveKg * f;
        }

        /// <summary>The point of the obstacle's box (<see cref="TerrainObstacles.PrismOf"/>'s: <paramref name="shape"/> from its base at
        /// <paramref name="position"/>, length along <paramref name="yaw"/>) nearest to <paramref name="q"/> — <paramref name="q"/> itself when inside.</summary>
        public static Vector3 NearestPoint(Vector3 position, float yaw, in ObstacleShape shape, Vector3 q)
        {
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            var d = q - position;
            float lx = d.X * c + d.Y * s, ly = -d.X * s + d.Y * c;
            float hl = MathF.Max(shape.Length, 0.01f) * 0.5f, hw = MathF.Max(shape.Width, 0.01f) * 0.5f;
            float cx = Math.Clamp(lx, -hl, hl), cy = Math.Clamp(ly, -hw, hw);
            float cz = Math.Clamp(q.Z, position.Z, position.Z + MathF.Max(shape.Height, 0.01f));
            return new Vector3(position.X + cx * c - cy * s, position.Y + cx * s + cy * c, cz);
        }

        /// <summary>True when <paramref name="e"/> is a static obstacle that can be destroyed (it carries <see cref="Health"/>).</summary>
        public static bool IsDestructible(EntityRepository repo, Entity e)
            => repo.IsAlive(e) && TerrainObstacles.IsObstacle(repo, e) && repo.IsComponentTypeRegistered<Health>() && repo.HasComponent<Health>(e);

        /// <summary>
        /// ⭐⭐ THE SEAM a remover calls (the bulldozer's blade, <c>CE-7012</c>: <i>"a push is damage per second while in contact"</i> ⇒ call this every
        /// tick of contact with <c>rate × dt</c>): ONE <see cref="DamageAssessedEvent"/> of <paramref name="damage"/> on <paramref name="obstacle"/>, named
        /// as <paramref name="by"/>'s — its Health owner applies it wherever it runs, and removes the obstacle at 0. False (nothing sent) when it is not
        /// a destructible obstacle, the damage is not positive, or this world carries no damage event.
        /// </summary>
        public static bool Damage(EntityRepository repo, Entity obstacle, float damage, Entity by)
        {
            if (!(damage > 0f) || !repo.Bus.IsRegistered<DamageAssessedEvent>() || !IsDestructible(repo, obstacle)) return false;
            repo.Bus.Publish(new DamageAssessedEvent { HitEntity = obstacle, TotalDamage = damage, Source = by, OriginalSource = by });
            return true;
        }
    }
}
