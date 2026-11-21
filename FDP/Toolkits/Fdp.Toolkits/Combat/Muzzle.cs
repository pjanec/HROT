using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ E4 (docs/DESIGN_Visual_Effects.md VE-K, §6b) — <b>where a weapon's muzzle IS, the ONE answer</b>: the round
    /// (<c>FireProcessingSystem</c>), the muzzle flash and the tracer (<c>EffectLayer</c>) and the drawn barrel tip all take it from here.
    /// <para>The mount's TKB muzzle offset is measured from its turret's trunnion (pivot + <c>TurretDto.Trunnion</c>), elevated about
    /// the trunnion by the turret part's <c>TurretPose.Elevation</c>, then turned about the pivot's vertical axis by its azimuth — the
    /// same two rotations, in the same order, the 3-D kit applies to its gun part — and placed by the hull's <see cref="SimTransform"/>.
    /// A mount on no turret: its offset from the hull's reference point. No muzzle data (a rifleman): false, and the caller keeps its
    /// own answer.</para>
    /// </summary>
    public static class Muzzle
    {
        /// <summary>The muzzle of <paramref name="shooter"/>'s mount <paramref name="weaponIndex"/> in world metres, if its type says.</summary>
        public static bool TryOf(EntityRepository world, Entity shooter, int weaponIndex, out Vector3 muzzle)
        {
            muzzle = default;
            if (!world.IsAlive(shooter) || !world.HasComponent<SimTransform>(shooter)) return false;
            if (!TryLocal(world, shooter, weaponIndex, out var local)) return false;
            var hull = world.GetComponentRO<SimTransform>(shooter);
            muzzle = hull.Position + Vector3.Transform(local, hull.Rotation);
            return true;
        }

        /// <summary>
        /// ⭐ CE-1070 — the muzzle AND the way the barrel points there (world, unit length): the mount's own forward axis, posed by the
        /// same elevation and azimuth as the muzzle, turned by the hull. A muzzle flash blooms along it, out of the barrel.
        /// </summary>
        public static bool TryOf(EntityRepository world, Entity shooter, int weaponIndex, out Vector3 muzzle, out Vector3 forward)
        {
            forward = default;
            if (!TryOf(world, shooter, weaponIndex, out muzzle)) return false;
            var mount = CombatTkb.MountOf(world, shooter, weaponIndex)!;
            var offset = new Vector3(mount.MuzzleOffsetX, mount.MuzzleOffsetY, mount.MuzzleOffsetZ);
            var turret = mount.TurretIndex >= 0 ? CombatTkb.TurretOf(world, shooter, mount.TurretIndex) : null;
            var local = Vector3.UnitX;
            if (turret != null)
            {
                var pose = TurretParts.PoseOf(world, shooter, mount.TurretIndex);
                local = Posed(turret, offset + Vector3.UnitX, pose.Azimuth, pose.Elevation) - Posed(turret, offset, pose.Azimuth, pose.Elevation);
            }
            forward = Vector3.Normalize(Vector3.Transform(local, world.GetComponentRO<SimTransform>(shooter).Rotation));
            return true;
        }

        /// <summary>The muzzle in the hull frame (metres from the reference point; X forward, Y left, Z up), posed.</summary>
        public static bool TryLocal(EntityRepository world, Entity shooter, int weaponIndex, out Vector3 local)
        {
            local = default;
            var mount = CombatTkb.MountOf(world, shooter, weaponIndex);
            if (mount == null) return false;
            var offset = new Vector3(mount.MuzzleOffsetX, mount.MuzzleOffsetY, mount.MuzzleOffsetZ);
            var turret = mount.TurretIndex >= 0 ? CombatTkb.TurretOf(world, shooter, mount.TurretIndex) : null;
            if (turret == null)
            {
                if (offset == Vector3.Zero) return false;
                local = offset;
                return true;
            }
            var pose = TurretParts.PoseOf(world, shooter, mount.TurretIndex);
            local = Posed(turret, offset, pose.Azimuth, pose.Elevation);
            return true;
        }

        /// <summary>
        /// The muzzle in the hull frame for a turret posed at (<paramref name="azimuth"/>, <paramref name="elevation"/>): elevation about
        /// the trunnion's lateral axis (nose up), then azimuth about the pivot's vertical axis — <c>KitPart.BodyTransform</c>'s order.
        /// </summary>
        public static Vector3 Posed(Tkb.Domain.TurretDto turret, Vector3 offset, float azimuth, float elevation)
        {
            var axis = new Vector3(turret.PivotX, turret.PivotY, 0f);
            var trunnion = new Vector3(turret.PivotX + turret.TrunnionX, turret.PivotY, turret.PivotZ + turret.TrunnionZ);
            var p = trunnion + offset;
            p = Vector3.Transform(p, Matrix4x4.CreateTranslation(-trunnion) * Matrix4x4.CreateRotationY(-elevation) * Matrix4x4.CreateTranslation(trunnion));
            p = Vector3.Transform(p, Matrix4x4.CreateTranslation(-axis) * Matrix4x4.CreateRotationZ(azimuth) * Matrix4x4.CreateTranslation(axis));
            return p;
        }
    }
}
