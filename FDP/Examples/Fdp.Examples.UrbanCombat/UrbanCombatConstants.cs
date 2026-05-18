namespace Fdp.Examples.UrbanCombat
{
    /// <summary>
    /// Compile-time constants for the Urban Ambush scenario.
    /// Centralised here so a single edit propagates to all blueprint and spawn sites.
    /// See CODE-STANDARDS.md §1 (No magic numbers in production code).
    /// </summary>
    public static class UrbanCombatConstants
    {
        // ── Faction IDs (DESIGN.md §4.1) ─────────────────────────────────────────
        /// <summary>Neutral faction — civilians, environmental entities.</summary>
        public const byte FactionNeutral = 0;
        /// <summary>Blue force — friendly military (APC, infantry soldiers).</summary>
        public const byte FactionBlue    = 1;
        /// <summary>Red force — adversary (insurgents).</summary>
        public const byte FactionRed     = 2;

        // ── Collider radii (meters) ───────────────────────────────────────────────
        /// <summary>Collision radius for humanoid entities (soldiers, civilians, insurgents).</summary>
        public const float HumanoidColliderRadius = 0.4f;
        /// <summary>Collision radius for civilian car entities.</summary>
        public const float CarColliderRadius      = 2.0f;
        /// <summary>Collision radius for Military APC entities.</summary>
        public const float ApcColliderRadius      = 3.5f;

        // ── Health ────────────────────────────────────────────────────────────────
        /// <summary>Starting and maximum hit-points for the Military APC.</summary>
        public const float ApcMaxHealth     = 500f;
        /// <summary>Starting and maximum hit-points for infantry soldiers and insurgents.</summary>
        public const float SoldierMaxHealth = 100f;

        // ── Weapon stats: Rifle (InfantrySoldier) ────────────────────────────────
        /// <summary>Magazine capacity for the standard infantry rifle.</summary>
        public const int   RifleAmmo           = 30;
        /// <summary>Muzzle velocity of the standard infantry rifle (m/s).</summary>
        public const float RifleMuzzleVelocity = 800f;

        // ── Weapon stats: RPG (Insurgent) ─────────────────────────────────────────
        /// <summary>Single-round capacity of the insurgent RPG launcher.</summary>
        public const int   RpgAmmo           = 1;
        /// <summary>Projectile speed of the RPG round (m/s).</summary>
        public const float RpgMuzzleVelocity = 300f;

        // ── Perception ranges (meters) ────────────────────────────────────────────
        /// <summary>Vision range for civilian pedestrians.</summary>
        public const float CivilianVisionRange  = 30f;
        /// <summary>Hearing range for civilian pedestrians.</summary>
        public const float CivilianHearingRange = 100f;
        /// <summary>Vision range for military soldiers and insurgents.</summary>
        public const float SoldierVisionRange   = 150f;
        /// <summary>Hearing range for military soldiers and insurgents.</summary>
        public const float SoldierHearingRange  = 200f;
    }
}
