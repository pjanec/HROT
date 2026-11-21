using Fdp.Core;
using Fdp.Toolkit.Determinism;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2323</c> (R-2063; 📄 docs/DESIGN_Weapon_Layers.md §5j) — each man's own fire rhythm. Every round in the sim is timed at
    /// two places, the brain's per-shot path (<c>AimAndFireExecutor</c>) and the muscle's turret clock (<c>WeaponTaskDispatcherSystem</c>);
    /// both scale their times here, so men ordered on the same tick neither open fire on one frame nor stay in step after. Seeded by
    /// <see cref="SimRng"/> with no time input: a scenario run gives the same rhythm every time.
    /// 🔒 User, 2026-10-20: <i>"they shoot in sync unisono … needed"</i> · <i>"also different gap on automatic weapons"</i>.
    /// </summary>
    public static class FirePacing
    {
        /// <summary>The first shot's aim time is scaled into [<see cref="AimMin"/>, <see cref="AimMax"/>).</summary>
        public const float AimMin = 0.7f, AimMax = 1.6f;
        /// <summary>Every later gap (asked, or the mount's own cyclic rate) is scaled into [<see cref="GapMin"/>, <see cref="GapMax"/>).</summary>
        public const float GapMin = 0.8f, GapMax = 1.25f;
        private const int AimSalt = 0x23230001, GapSalt = 0x23230002;

        /// <summary>The aim-time factor of <paramref name="shooter"/> at <paramref name="target"/> — one draw per pairing.</summary>
        public static float AimFactor(Entity shooter, Entity target)
        {
            var rng = SimRng.FromSim(shooter.Index, AimSalt ^ (target.Index * 7919), 0f);
            return AimMin + rng.NextSingle() * (AimMax - AimMin);
        }

        /// <summary><paramref name="interval"/> scaled for round <paramref name="round"/> of <paramref name="shooter"/>'s action
        /// <paramref name="action"/>; 0 stays 0 (no interval asked, none known).</summary>
        public static float Gap(float interval, Entity shooter, int round, uint action)
        {
            if (interval <= 0f) return interval;
            var rng = SimRng.FromSim(shooter.Index, GapSalt ^ (round * 7919) ^ unchecked((int)(action * 104729u)), 0f);
            return interval * (GapMin + rng.NextSingle() * (GapMax - GapMin));
        }
    }
}
