using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ Indirect fire IF-1 (docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.3, IF-C) — which arc the caller asks for.</summary>
    public enum ArcPreference : byte
    {
        /// <summary>Low when the low arc clears to the point, else high (the mount's <see cref="IndirectArc"/> still limits it).</summary>
        Auto = 0,
        Low = 1,
        High = 2,
    }

    /// <summary>⭐ IF-1 — why <see cref="FireSolution.For"/> found no solution.</summary>
    public enum FireRefusal : byte
    {
        None = 0,
        /// <summary>No charge reaches the point on an allowed arc.</summary>
        TooFar = 1,
        /// <summary>Every charge that reaches needs an elevation outside the mount's limits (the point is inside the minimum range).</summary>
        TooClose = 2,
        /// <summary>A charge reaches, but the terrain or a building stops every allowed arc on the way.</summary>
        Masked = 3,
    }

    /// <summary>⭐ IF-1 — a fire solution: the charge and arc to fire at, the launch velocity and how long the round flies.</summary>
    public struct Solution
    {
        public bool Ok;
        public FireRefusal Refusal;
        /// <summary>1-based charge index into the round's <see cref="AmmoWeaponBallisticsDto.ChargeSpeedsMps"/>; 0 = the mount's single
        /// muzzle velocity (no charges declared).</summary>
        public byte Charge;
        public bool High;
        public Vector3 Launch;
        public float TimeOfFlight;
        /// <summary>The launch elevation, degrees above the horizontal.</summary>
        public float Elevation;
    }

    /// <summary>
    /// ⭐⭐ Indirect fire IF-1 (docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.3, IF-B, IF-C) — THE gun's fire solution, beside
    /// <see cref="ThrowArc"/> (which stays the hand throw's rule): the LOWEST charge that reaches on the chosen arc. <b>Auto</b> = low when
    /// the low arc clears the terrain and buildings to the point (<see cref="ClearsDensely"/>), else high within the mount's
    /// <see cref="WeaponMountDto.MaxElevationDeg"/>, else refused with a reason. Pure: the brain decides once and the intent carries the
    /// charge and the arc (<c>WeaponFireIntent.Charge / HighArc</c>); the SimHost flies exactly that (no second decision).
    /// <para>The closed form is <see cref="FireProcessingSystem.ArcLaunch"/>'s (gravity, no drag) — the charge speeds are vacuum-equivalent
    /// (<see cref="AmmoWeaponBallisticsDto.ChargeSpeedsMps"/>), so the RANGE is right and the time of flight is short of a real one.</para>
    /// </summary>
    public static class FireSolution
    {
        /// <summary>The arc is checked in segments of about this length (m) — a 5 km arc is ~200 segments, a 20 m throw still 12.</summary>
        public const float SampleSpacingMetres = 25f;

        /// <summary>The most segments one arc is checked in.</summary>
        public const int MaxSamples = 400;

        /// <summary>The charges a mount firing <paramref name="ammo"/> has: the round's profile for this weapon, else its generic one;
        /// none ⇒ the mount's single muzzle velocity (charge 0).</summary>
        public static IReadOnlyList<float>? ChargesOf(Fdp.Interfaces.ITkbDatabase? db, WeaponMountDto mount, long ammo)
        {
            if (db == null || ammo == 0 || !db.TryGetByType(ammo, out var t) || t == null) return null;
            IReadOnlyList<float>? generic = null;
            foreach (var (type, _, data) in t.GetAllDescriptors())
            {
                if (type != typeof(AmmoWeaponBallisticsDto) || data is not AmmoWeaponBallisticsDto b || b.ChargeSpeedsMps.Count == 0) continue;
                if (b.WeaponGuid != 0 && unchecked((ulong)b.WeaponGuid) == mount.WeaponGuid) return b.ChargeSpeedsMps;
                if (b.WeaponGuid == 0 && generic == null) generic = b.ChargeSpeedsMps;
            }
            return generic;
        }

        /// <summary>
        /// The solution for a round from <paramref name="from"/> to <paramref name="to"/> fired from <paramref name="mount"/> at
        /// <paramref name="charges"/> (ascending speeds, m/s; null or empty ⇒ the mount's <c>MuzzleVelocity</c> as charge 0), preferring
        /// <paramref name="preference"/>. <paramref name="world"/> null ⇒ nothing masks (every arc clears).
        /// </summary>
        public static Solution For(Fdp.Toolkit.World.IWorldQuery? world, Vector3 from, Vector3 to, WeaponMountDto mount,
                                   IReadOnlyList<float>? charges, ArcPreference preference = ArcPreference.Auto, float penetration = 0f)
        {
            bool lowAllowed = mount.Arc != IndirectArc.HighOnly && preference != ArcPreference.High;
            bool highAllowed = mount.Arc != IndirectArc.LowOnly && preference != ArcPreference.Low;
            bool reached = false, inLimits = false;
            int n = charges == null || charges.Count == 0 ? 1 : charges.Count;

            // ① low (shortest flight) when allowed; ② high
            foreach (bool high in new[] { false, true })
            {
                if (high ? !highAllowed : !lowAllowed) continue;
                for (int i = 0; i < n; i++)
                {
                    float speed = charges == null || charges.Count == 0 ? mount.MuzzleVelocity : charges[i];
                    if (speed <= 0f || !TryArc(from, to, speed, high, out var launch, out float elevation, out float tof)) continue;
                    reached = true;
                    if (!WithinElevation(mount, elevation)) continue;
                    inLimits = true;
                    // masked: a faster charge flies a FLATTER low arc (masked too) but a STEEPER high one (it may clear)
                    if (world != null && !ClearsDensely(world, from, to, launch, penetration)) { if (high) continue; break; }
                    return new Solution
                    {
                        Ok = true, Charge = (byte)(charges == null || charges.Count == 0 ? 0 : i + 1), High = high,
                        Launch = launch, TimeOfFlight = tof, Elevation = elevation,
                    };
                }
            }
            return new Solution { Refusal = !reached ? FireRefusal.TooFar : !inLimits ? FireRefusal.TooClose : FireRefusal.Masked };
        }

        /// <summary>The arc at <paramref name="speed"/>: false when it does not reach. The elevation in degrees; the time of flight from the
        /// horizontal speed over the flat distance.</summary>
        public static bool TryArc(Vector3 from, Vector3 to, float speed, bool high, out Vector3 launch, out float elevationDeg, out float timeOfFlight)
        {
            launch = default; elevationDeg = 0f; timeOfFlight = 0f;
            var flat = new Vector2(to.X - from.X, to.Y - from.Y);
            float d = flat.Length(), h = to.Z - from.Z, g = CombatConstants.Gravity, v2 = speed * speed;
            if (d < 1e-3f) return false;
            float disc = v2 * v2 - g * (g * d * d + 2f * h * v2);
            if (disc < 0f) return false;
            launch = FireProcessingSystem.ArcLaunch(from, to, speed, high);
            float horizontal = new Vector2(launch.X, launch.Y).Length();
            elevationDeg = MathF.Atan2(launch.Z, horizontal) * 180f / MathF.PI;
            timeOfFlight = horizontal > 1e-3f ? d / horizontal : 0f;
            return true;
        }

        /// <summary>Within the mount's elevation limits (0 = no limit on that side).</summary>
        public static bool WithinElevation(WeaponMountDto mount, float elevationDeg)
            => (mount.MinElevationDeg == 0f || elevationDeg >= mount.MinElevationDeg)
               && (mount.MaxElevationDeg == 0f || elevationDeg <= mount.MaxElevationDeg);

        /// <summary><see cref="ThrowArc.Clears"/> generalised for kilometres: the same terrain rule, in segments of
        /// <see cref="SampleSpacingMetres"/> (12 at least, <see cref="MaxSamples"/> at most).</summary>
        public static bool ClearsDensely(Fdp.Toolkit.World.IWorldQuery world, Vector3 from, Vector3 to, Vector3 velocity, float penetration)
        {
            float distance = new Vector2(to.X - from.X, to.Y - from.Y).Length();
            int samples = Math.Clamp((int)MathF.Ceiling(distance / SampleSpacingMetres), ThrowArc.Samples, MaxSamples);
            return ThrowArc.Clears(world, from, to, velocity, penetration, samples);
        }
    }
}
