using System;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-1358</c> (docs/DESIGN_Weapon_Layers.md §5k) — the BARREL HEAT rule, beside <see cref="Magazine"/>: every round that leaves
    /// (<see cref="Magazine.Spend"/>) heats the barrel by <see cref="WeaponState.HeatPerRound"/>; it cools linearly every frame
    /// (<c>WeaponDispatcherSystem</c>); at 1 it is TOO HOT and <see cref="Magazine.Ready"/> refuses every round until it cools to
    /// <see cref="ResumeFraction"/>. 🔒 User, 2026-10-25: <i>"gun fires at its fast automatic rate untit it gets too hot and that cooldown
    /// blocks netx bursts"</i> · <i>"barrel heat needs to be modelled also for single shot guns, because of thermal view simulation"</i>.
    /// </summary>
    public static class BarrelHeat
    {
        /// <summary>A too-hot barrel fires again once it has cooled to this share.</summary>
        public const float ResumeFraction = 0.5f;

        /// <summary>An automatic weapon at its cyclic rate is too hot after this many seconds from cold (ignoring the cooling meanwhile).</summary>
        public const float AutomaticBurstSeconds = 4f;
        /// <summary>An automatic weapon's cool time from too hot to cold.</summary>
        public const float AutomaticCoolSeconds = 12f;
        /// <summary>An automatic weapon that states no rate of fire.</summary>
        public const float AutomaticHeatPerRound = 0.02f;
        /// <summary>⭐ <c>CE-4144</c> (docs/DESIGN_Weapon_Layers.md §5k-1, H-1) — small arms (a rifle) are too hot after this many ROUNDS from cold,
        /// whatever their rate of fire: a slow rifle must not overheat faster than a fast one. 🔒 User, 2026-10-31: <i>"the overheating now
        /// happens way too quickly for a normal rifles … must be set with much higher limits (what a real rifle would stand)"</i>.</summary>
        public const int SmallArmsRoundsToHot = 150;
        /// <summary>Small arms' cool time from too hot to cold (<c>CE-4144</c> H-2); a too-hot rifle fires again at <see cref="ResumeFraction"/>.</summary>
        public const float SmallArmsCoolSeconds = 60f;

        /// <summary>A single-shot gun's heat per round (a tank gun, a howitzer, a launcher tube).</summary>
        public const float SingleShotHeatPerRound = 0.15f;
        /// <summary>A single-shot gun's cool time from too hot to cold.</summary>
        public const float SingleShotCoolSeconds = 60f;

        /// <summary>The mount's heat per round and cool time: its TKB values, else by its category (0, 0 = no barrel).</summary>
        public static (float PerRound, float CoolSeconds) For(WeaponMountDto? mount)
        {
            if (mount == null || mount.Hands) return (0f, 0f);
            // CE-4144 (§5k-1): small arms have their own defaults (by rounds), the machine gun and autocannon keep the 4-s-at-cyclic rule.
            bool smallArms = mount.Category == WeaponCategory.SmallArms;
            bool automatic = mount.Category is WeaponCategory.MachineGun or WeaponCategory.Autocannon;
            float perRound = mount.HeatPerRound > 0f ? mount.HeatPerRound : DefaultPerRound(mount, automatic, smallArms);
            if (perRound <= 0f) return (0f, 0f);
            float cool = mount.BarrelCoolSeconds > 0f ? mount.BarrelCoolSeconds
                : smallArms ? SmallArmsCoolSeconds : automatic ? AutomaticCoolSeconds : SingleShotCoolSeconds;
            return (perRound, cool);
        }

        private static float DefaultPerRound(WeaponMountDto mount, bool automatic, bool smallArms)
        {
            if (smallArms) return 1f / SmallArmsRoundsToHot;
            if (mount.Category is WeaponCategory.Grenade or WeaponCategory.Bomb or WeaponCategory.Countermeasure) return 0f;
            if (!automatic) return SingleShotHeatPerRound;
            return mount.RateOfFire > 0f ? 1f / (mount.RateOfFire / 60f * AutomaticBurstSeconds) : AutomaticHeatPerRound;
        }

        /// <summary>A freshly spawned weapon: a cold barrel with the mount's heat rule.</summary>
        public static void Load(ref WeaponState w, WeaponMountDto? mount)
        {
            var (perRound, cool) = For(mount);
            w.HeatPerRound = perRound;
            w.HeatCoolPerSecond = cool > 0f ? 1f / cool : 0f;
            w.Heat = 0f;
            w.Overheated = 0;
        }

        /// <summary>One round leaves (called by <see cref="Magazine.Spend"/>): the barrel heats; reaching 1 latches too hot.</summary>
        public static void Fire(ref WeaponState w)
        {
            if (w.HeatPerRound <= 0f) return;
            w.Heat = MathF.Min(1f, w.Heat + w.HeatPerRound);
            if (w.Heat >= 1f - 1e-4f) w.Overheated = 1;
        }

        /// <summary>The barrel cools for <paramref name="dt"/>; a too-hot one opens again at <see cref="ResumeFraction"/>.</summary>
        public static void Tick(ref WeaponState w, float dt)
        {
            if (w.Heat <= 0f) return;
            w.Heat = MathF.Max(0f, w.Heat - w.HeatCoolPerSecond * dt);
            if (w.Overheated != 0 && w.Heat <= ResumeFraction) w.Overheated = 0;
        }

        /// <summary>True while the barrel is too hot to fire.</summary>
        public static bool IsOverheated(in WeaponState w) => w.Overheated != 0;

        /// <summary>Rounds the barrel takes before it is too hot (0 while it is; <see cref="int.MaxValue"/> with no barrel model) — the
        /// muscle's grant stops at it, so a burst never overshoots.</summary>
        public static int RoundsLeft(in WeaponState w)
        {
            if (w.Overheated != 0) return 0;
            if (w.HeatPerRound <= 0f) return int.MaxValue;
            return Math.Max(1, (int)MathF.Ceiling((1f - w.Heat) / w.HeatPerRound - 1e-4f));
        }
    }
}
