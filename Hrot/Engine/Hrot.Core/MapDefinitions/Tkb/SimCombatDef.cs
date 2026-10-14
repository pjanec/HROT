using Fdp.Core;

namespace Hrot.Map.Definitions.Tkb
{
    public struct WeaponMount
    {
        /// <summary>
        /// Weapon type identifier (e.g., "120mm_APFSDS", "7.62mm_MG").
        /// </summary>
        public string WeaponType { get; set; }
        
        /// <summary>
        /// Initial ammunition count.
        /// </summary>
        public int Ammunition { get; set; }
        
        /// <summary>
        /// Effective range in meters.
        /// </summary>
        public float Range { get; set; }
        
        /// <summary>
        /// Rate of fire in rounds per minute.
        /// </summary>
        public float RateOfFire { get; set; }

        /// <summary>⭐ <c>CE-3071</c> — armour penetration of the round, mm RHA.</summary>
        public float Penetration { get; set; }

        /// <summary>⭐ <c>CE-3071</c> — damage of one penetrating hit.</summary>
        public float DamagePerHit { get; set; }

        /// <summary>⭐ AQ85 C (R-216) — aim dispersion, mils; 0 = exact aim. Copied onto <c>WeaponMountDto.DispersionMils</c>.</summary>
        public float DispersionMils { get; set; }

        /// <summary>⭐ WL (DESIGN_Weapon_Layers W-5) — the weapon's category; selects its weapon-brain profile. Copied onto
        /// <c>WeaponMountDto.Category</c>.</summary>
        public Fdp.Toolkit.Tkb.Domain.WeaponCategory Category { get; set; }

        /// <summary>⭐ WL-1 (W-9) — the turret this weapon sits on (<c>NedTkbBuilder.WithTurrets</c> order); null = fixed to the hull.</summary>
        public int? TurretIndex { get; set; }

        /// <summary>⭐ E4 (DESIGN_Visual_Effects VE-K) — the muzzle from the turret's trunnion (or from the hull's reference point when
        /// fixed), metres, X forward / Y left / Z up. Copied onto <c>WeaponMountDto.MuzzleOffsetX/Y/Z</c>.</summary>
        public System.Numerics.Vector3 MuzzleOffset { get; set; }

        /// <summary>⭐ LR-1 (DESIGN_Visual_Effects.md §8.5, R-271: "Every ammo needs its tkb type, always") — the munition type it fires.</summary>
        public long AmmoTkb { get; set; }

        /// <summary>⭐ LR-1 — the tracer munition mixed in, one in <see cref="TracerEvery"/> (0 = none).</summary>
        public long TracerAmmoTkb { get; set; }

        /// <inheritdoc cref="TracerAmmoTkb"/>
        public int TracerEvery { get; set; }

        /// <summary>⭐ M5 (DESIGN_Missiles_And_Ammo §2) — the rounds this mount carries, by munition type: the weapon brain picks one
        /// per target (<c>AmmoChoice</c>). Empty = the mount's one round (<see cref="AmmoTkb"/>). Copied onto <c>WeaponMountDto.Loadout</c>.</summary>
        public List<(long AmmoTkb, int Rounds)> Loadout { get; set; }

        /// <summary>⭐ M5 / D-L — may it fire while the unit moves; null = by category (an ATGM or a mortar stands). <c>WeaponMountDto.FireOnMove</c>.</summary>
        public bool? FireOnMove { get; set; }

        /// <summary>⭐ M5 — the aim (or lock) time, s; 0 = the engine fallback. <c>WeaponMountDto.AimSeconds</c>.</summary>
        public float AimSeconds { get; set; }

        /// <summary>⭐ M5 — rounds per magazine / belt (0 = no magazine) and the change time, s. <c>WeaponMountDto.MagazineSize</c> /
        /// <c>ReloadSeconds</c>.</summary>
        public int MagazineSize { get; set; }

        /// <inheritdoc cref="MagazineSize"/>
        public float ReloadSeconds { get; set; }

        /// <summary>⭐ Air Ops slice 6a (AW-1) — a FIXED mount's nose cone, degrees off the body's forward (0 = no cone).
        /// <c>WeaponMountDto.FixedConeDeg</c>.</summary>
        public float FixedConeDeg { get; set; }
    }
    
    /// <summary>
    /// Combat properties (weapons, armor, sensors).
    /// NOTE: Stubbed for future combat module integration.
    /// </summary>
    [ComponentId(GlobalComponentIds.SimCombatDef)]
    public class SimCombatDef
    {
        /// <summary>
        /// Frontal armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorFront { get; set; } // mm RHA
        
        /// <summary>
        /// Side armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorSide { get; set; }
        
        /// <summary>
        /// Rear armor thickness in mm RHA equivalent.
        /// </summary>
        public float ArmorRear { get; set; }

        /// <summary>⭐ M6 (DESIGN_Armour_Hit_Model §2) — the roof, mm RHA; 0 ⇒ the rear value.</summary>
        public float ArmorTop { get; set; }

        /// <summary>⭐ M6 — each face against a SHAPED CHARGE (composite, ERA), mm RHA; 0 ⇒ that face's kinetic value.</summary>
        public float ArmorFrontCe { get; set; }
        /// <inheritdoc cref="ArmorFrontCe"/>
        public float ArmorSideCe { get; set; }
        /// <inheritdoc cref="ArmorFrontCe"/>
        public float ArmorRearCe { get; set; }
        /// <inheritdoc cref="ArmorFrontCe"/>
        public float ArmorTopCe { get; set; }

        /// <summary>⭐ M6 (D-F) — the hit points, stated; 0 ⇒ the front × 5 fallback (<c>EngineFallbacks.HealthFromArmour</c>).</summary>
        public float MaxHealth { get; set; }

        /// <summary>⭐ M6 — light vehicles opt into a mobility kill below this fraction of health (0 = none). <c>CombatPlatformDefDto</c>.</summary>
        public float MobilityKillBelowFraction { get; set; }

        /// <summary>⭐ M5 — where the crew sees from, m above the ground (the sight / the commander's hatch); 0 ⇒ a standing man's.</summary>
        public float EyeHeight { get; set; }

        /// <summary>
        /// Weapon systems mounted on vehicle.
        /// </summary>
        public List<WeaponMount> Weapons { get; set; } = new();
        
        /// <summary>
        /// Sensor detection range in meters.
        /// </summary>
        public float SensorRange { get; set; } // meters
        
        /// <summary>
        /// Whether entity can engage threats autonomously.
        /// </summary>
        public bool AutonomousEngagement { get; set; } = false;
    }
}
