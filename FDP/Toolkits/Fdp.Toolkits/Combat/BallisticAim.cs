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
        /// <para>⭐ ui handoff P2 (DESIGN_Indirect_Fire §3.9) — a mount that declares an <see cref="IndirectArc"/> aims along the FIRE
        /// SOLUTION the brain fires with (<see cref="FireSolution.For"/>: the lowest charge of <paramref name="ammo"/> that reaches on
        /// <paramref name="arc"/>), so the laid elevation is the fired one. Solved without the mask check (per frame, cheap); the brain
        /// already chose the arc with it. ⛔ Only for a LAID order (<paramref name="arc"/> Low / High): an Engage order (Auto) fires rounds
        /// that fly the mount's one speed, so it keeps aiming along that one-speed arc. No solution ⇒ the one-speed arc below.</para>
        /// </summary>
        public static (float Azimuth, float Elevation) Solve(EntityRepository world, Entity hullEntity, TurretDto turret,
                                                              WeaponMountDto? mount, Vector3 aimPoint,
                                                              ArcPreference arc = ArcPreference.Auto, long ammo = 0L)
        {
            var hull = world.GetComponent<SimTransform>(hullEntity);
            var pivot = PivotOf(hull, turret);
            var direction = aimPoint - pivot;
            if (arc != ArcPreference.Auto && mount != null && mount.Arc != IndirectArc.Unset)
            {
                var db = world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
                if (ammo == 0L) ammo = unchecked((long)mount.AmmoGuid);
                var solution = FireSolution.For(null, pivot, aimPoint, mount, FireSolution.ChargesOf(db, mount, ammo), arc);
                if (solution.Ok) return AnglesOf(hull, solution.Launch);
            }
            if (mount != null && IsIndirect(world, mount))
            {
                float speed = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocityOrFallback(mount.MuzzleVelocity);
                direction = FireProcessingSystem.ArcLaunch(pivot, aimPoint, speed, high: speed >= CombatConstants.HighArcMinMuzzleVelocity);
            }
            return AnglesOf(hull, direction.LengthSquared() > 1e-8f ? direction : Vector3.Transform(Vector3.UnitX, hull.Rotation));
        }

        /// <summary>
        /// ⭐ <c>CE-1307</c> (docs/DESIGN_Weapon_Layers.md W-8b) — the OPERATOR's point moved to where turret <paramref name="turret"/> can
        /// actually point: a direct-fire aim outside its limits becomes the point at the same range along the nearest reachable line
        /// (<see cref="TurretSlew.ClampToArc"/>), so the turret traverses and stops at its limit instead of refusing. An aim inside the
        /// arc, and an indirect (arcing) mount — whose angles are a lob, not a line — come back unchanged.
        /// </summary>
        public static Vector3 Reachable(EntityRepository world, Entity hullEntity, TurretDto turret, WeaponMountDto? mount, Vector3 aimPoint)
        {
            if (!world.HasComponent<SimTransform>(hullEntity) || (mount != null && IsIndirect(world, mount))) return aimPoint;
            var (azimuth, elevation) = Solve(world, hullEntity, turret, mount, aimPoint);
            if (TurretSlew.InArc(turret, azimuth, elevation)) return aimPoint;
            var (az, el) = TurretSlew.ClampToArc(turret, azimuth, elevation);
            var hull = world.GetComponent<SimTransform>(hullEntity);
            var pivot = PivotOf(hull, turret);
            var local = new Vector3(MathF.Cos(el) * MathF.Cos(az), MathF.Cos(el) * MathF.Sin(az), MathF.Sin(el));
            return pivot + Vector3.Transform(local, hull.Rotation) * Vector3.Distance(pivot, aimPoint);
        }

        /// <summary>⭐ <c>CE-1359</c> (docs/DESIGN_Weapon_Layers.md W-8c) — how far past a LIMITED traverse the operator's crosshair may be and
        /// the gun still fire (the yellow barrel ring then sits next to the crosshair).</summary>
        public const float OperatorTraverseToleranceDeg = 3f;

        /// <summary>
        /// ⭐ <c>CE-1359</c> (W-8c) — the operator's point lies beyond turret <paramref name="turret"/>'s TRAVERSE (a door gun's arc) by more
        /// than <see cref="OperatorTraverseToleranceDeg"/>: the gun turns to its limit (<see cref="Reachable"/>) but must not fire — its
        /// rounds would go somewhere the operator is not aiming. ⛔ Never true for a full-circle turret (a tank turns to the point and
        /// fires), nor for an elevation beyond the limits (W-8b: the round leaves along the limit), nor for an indirect mount.
        /// 🔒 User, 2026-10-25: <i>"wepon should fire only if its barrel aiming point is close to the mouse driven aim point"</i> ·
        /// <i>"tank turret should keep working as now"</i>.
        /// </summary>
        public static bool BeyondTraverse(EntityRepository world, Entity hullEntity, TurretDto turret, WeaponMountDto? mount, Vector3 aimPoint)
        {
            if (TurretSlew.IsFullCircle(turret) || !world.HasComponent<SimTransform>(hullEntity) || (mount != null && IsIndirect(world, mount)))
                return false;
            var (azimuth, _) = Solve(world, hullEntity, turret, mount, aimPoint);
            const float deg = MathF.PI / 180f;
            float az = TurretSlew.Wrap(azimuth), lo = turret.TraverseMinDeg * deg, hi = turret.TraverseMaxDeg * deg;
            if (az >= lo && az <= hi) return false;
            float off = MathF.Min(MathF.Abs(TurretSlew.Wrap(az - lo)), MathF.Abs(TurretSlew.Wrap(az - hi)));
            return off > OperatorTraverseToleranceDeg * deg;
        }

        /// <summary>True when the mount's round is an indirect (arcing) warhead — <see cref="CombatTkb.WarheadOf"/>, the one lookup
        /// <see cref="FireProcessingSystem"/> reads too.</summary>
        public static bool IsIndirect(EntityRepository world, WeaponMountDto mount) => CombatTkb.WarheadOf(world, mount) is { Indirect: true };
    }
}
