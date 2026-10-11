using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ WL-2 (docs/DESIGN_Weapon_Layers.md §5c, W-8, R-252) — the turret angles that put a mount's round on an aim point, solved on
    /// the MUSCLE (the brain never computes an angle, M24). Angles are in the HULL frame (the hull's rotation undone), so a hull on a
    /// slope tilts its turret plane with it: azimuth counter-clockwise from the hull's forward (+X), elevation up from the turret
    /// plane — the <c>TurretPose</c> convention (M23).
    /// <para>Direct fire aims along the line from the turret pivot to the point, as the round flies today (no gravity on direct
    /// rounds); an INDIRECT warhead aims along <see cref="FireProcessingSystem.ArcLaunch"/> — the arc the round will fly.</para>
    /// </summary>
    public static class BallisticAim
    {
        /// <summary>The turret pivot in world space: the hull position plus the TKB pivot offset turned with the hull.</summary>
        public static Vector3 PivotOf(in SimTransform hull, TurretDto turret)
            => hull.Position + Vector3.Transform(new Vector3(turret.PivotX, turret.PivotY, turret.PivotZ), hull.Rotation);

        /// <summary>The hull-frame (azimuth, elevation) of the world direction <paramref name="worldDirection"/>.</summary>
        public static (float Azimuth, float Elevation) AnglesOf(in SimTransform hull, Vector3 worldDirection)
        {
            var local = Vector3.Transform(worldDirection, Quaternion.Inverse(hull.Rotation));
            float flat = MathF.Sqrt(local.X * local.X + local.Y * local.Y);
            return (MathF.Atan2(local.Y, local.X), MathF.Atan2(local.Z, flat));
        }

        /// <summary>
        /// The angles that aim turret <paramref name="turret"/> of <paramref name="hullEntity"/> at <paramref name="aimPoint"/> for
        /// <paramref name="mount"/>'s round (null mount ⇒ direct).
        /// </summary>
        public static (float Azimuth, float Elevation) Solve(EntityRepository world, Entity hullEntity, TurretDto turret,
                                                              WeaponMountDto? mount, Vector3 aimPoint)
        {
            var hull = world.GetComponent<SimTransform>(hullEntity);
            var pivot = PivotOf(hull, turret);
            var direction = aimPoint - pivot;
            if (mount != null && IsIndirect(world, mount))
            {
                float speed = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocityOrFallback(mount.MuzzleVelocity);
                direction = FireProcessingSystem.ArcLaunch(pivot, aimPoint, speed, high: speed >= CombatConstants.HighArcMinMuzzleVelocity);
            }
            return AnglesOf(hull, direction.LengthSquared() > 1e-8f ? direction : Vector3.Transform(Vector3.UnitX, hull.Rotation));
        }

        /// <summary>True when the mount's round is an indirect (arcing) warhead — <see cref="CombatTkb.WarheadOf"/>, the one lookup
        /// <see cref="FireProcessingSystem"/> reads too.</summary>
        public static bool IsIndirect(EntityRepository world, WeaponMountDto mount) => CombatTkb.WarheadOf(world, mount) is { Indirect: true };
    }
}
