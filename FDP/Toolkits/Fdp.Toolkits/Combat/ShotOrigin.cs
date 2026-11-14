using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2218</c> — where a straight round STARTS, the one rule: the shooter's EYE (the sight line the aim is chosen on — Buildings
    /// §3d P2, R-217; a carried / station weapon system from its carrier's eye, WS-3), and the BARREL when the type says where it is
    /// (<see cref="Muzzle.TryOf(EntityRepository, Entity, int, out Vector3)"/>, E4 — never more than half way to the aim, CE-3059).
    /// <para>Read by <c>FireProcessingSystem</c> (the round itself) and by <see cref="LineOfFire.MuzzleMasked"/> (the brain's hold before
    /// release), so the line the brain tests is the line the round flies.</para>
    /// </summary>
    public static class ShotOrigin
    {
        /// <summary>The eye the shot is aimed from, and the body it belongs to (the carrier of a carried / station weapon system).</summary>
        public static Vector3 Eye(EntityRepository world, Entity shooter, out Entity sightBody)
        {
            sightBody = HitModel.BodyOf(world, shooter);
            // ⭐ CE-3257 (BR-5, LN-3) — the ONE eye: a man leaning round a corner shoots from where he sees
            return Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeOf(world, sightBody, HitModel.LogicalStance(world, sightBody));
        }

        /// <summary>Where the round leaves for <paramref name="aim"/>: the barrel (<paramref name="fromBarrel"/>) when the type places one no
        /// more than half way to the aim, else <paramref name="eye"/>.</summary>
        public static Vector3 From(EntityRepository world, Entity shooter, int weaponIndex, Vector3 eye, Vector3 aim, out bool fromBarrel)
        {
            fromBarrel = Muzzle.TryOf(world, shooter, weaponIndex, out var barrel)
                         && Vector3.Distance(barrel, eye) <= 0.5f * Vector3.Distance(aim, eye);
            return fromBarrel ? barrel : eye;
        }
    }
}
