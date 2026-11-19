using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-4635</c> FL-1 (R-3111; docs/designs/weapons/DESIGN_Flame_Weapons.md F-2) — THE flame cone, a pure law beside <see cref="AreaEffect"/>'s: a point is
    /// in a jet when it lies ahead of the muzzle within the reach and within the cone — never thinner than <see cref="StreamRadiusM"/> (the stream's own
    /// width near the muzzle). What STOPS the flame (a wall, a hull; an opening passes) is the sight line, asked by the applying system.
    /// </summary>
    public static class FlameCone
    {
        /// <summary>The stream's width near the muzzle (m): a point this close to the axis is in it however narrow the cone.</summary>
        public const float StreamRadiusM = 0.5f;

        /// <summary>Is <paramref name="point"/> inside the jet from <paramref name="origin"/> along the unit <paramref name="dir"/>?</summary>
        public static bool Contains(Vector3 origin, Vector3 dir, float rangeM, float halfAngleRad, Vector3 point)
        {
            var d = point - origin;
            float along = Vector3.Dot(d, dir);
            if (along <= 0f || along > rangeM) return false;
            float perp2 = MathF.Max(0f, d.LengthSquared() - along * along);
            float allowed = MathF.Max(StreamRadiusM, along * MathF.Tan(halfAngleRad));
            return perp2 <= allowed * allowed;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4635</c> / <c>CE-4637</c> FL-1 (R-3111, flame weapons F-3) — THE ignition rule, with no dice (R-212): a target's EXPOSURE (seconds in a flame ×
    /// its intensity) lights it once it reaches its kind's threshold. A man 0.3 s, a soft vehicle 1.5 s; an ARMOURED hull (side armour ≥ <see cref="ArmorModel.SoftSkinArmourMm"/>,
    /// the ONE soft-skin line the blast already uses) and aircraft never — the crew is frightened, the hull does not burn (an engine-deck fire is v2). Buildings and ground come with
    /// their holders (FL-3, FL-4).
    /// </summary>
    public static class Ignition
    {
        public enum Kind : byte { None = 0, Person = 1, SoftVehicle = 2 }

        /// <summary>The exposure (s × intensity) that lights a man.</summary>
        public const float PersonSeconds = 0.3f;
        /// <summary>The exposure that lights a soft vehicle (a car, a truck, a wagon, a light APC).</summary>
        public const float SoftVehicleSeconds = 1.5f;
        /// <summary>How long a man set alight burns (s) — rolling on the ground to put it out is v2.</summary>
        public const float BurningManSeconds = 6f;
        /// <summary>A vehicle's fire burns this long when the munition names no <see cref="WarheadDto.BurnSeconds"/> (s).</summary>
        public const float DefaultVehicleBurnSeconds = 120f;
        /// <summary>Exposure is a CONTINUOUS stay: out of every flame longer than this (s) and it starts again from 0.</summary>
        public const double ExposureGapSeconds = 1.0;

        /// <summary>What <paramref name="e"/> is to a flame.</summary>
        public static Kind Of(EntityRepository world, Entity e)
        {
            var loco = Fdp.Toolkit.Motion.Locomotions.Of(world, e);
            if (loco is LocomotionClass.Pedestrian or LocomotionClass.Quadruped) return Kind.Person;
            if (Fdp.Toolkit.Motion.Locomotions.IsAircraft(world, e) || !Fdp.Toolkit.Motion.Locomotions.IsVehicle(world, e)) return Kind.None;
            return ArmorModel.ArmourFor(CombatTkb.PlatformOf(world, e), Facing.Side) >= ArmorModel.SoftSkinArmourMm ? Kind.None : Kind.SoftVehicle;   // the one soft-skin line
        }

        /// <summary>The exposure that lights a <paramref name="kind"/>; +∞ for <see cref="Kind.None"/>.</summary>
        public static float Threshold(Kind kind) => kind switch
        {
            Kind.Person => PersonSeconds,
            Kind.SoftVehicle => SoftVehicleSeconds,
            _ => float.PositiveInfinity,
        };
    }
}
