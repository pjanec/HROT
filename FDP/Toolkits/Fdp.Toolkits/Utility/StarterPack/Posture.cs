namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Starter-pack tactical postures for the CombatPosture decision.
    /// Stored as a byte in <see cref="UtilityResultEntry.WinningPostureId"/>.
    /// OptionId values must fit in a byte (0-255).
    /// </summary>
    public enum Posture : byte
    {
        /// <summary>Move toward the enemy and engage.</summary>
        AdvanceAndAttack = 1,
        /// <summary>Use nearby cover and engage.</summary>
        TakeCover        = 2,
        /// <summary>Lay down fire to suppress the enemy.</summary>
        Suppress         = 3,
        /// <summary>Fall back to a safe position.</summary>
        Flee             = 4,
        /// <summary>Maintain current position.</summary>
        Hold             = 5,
        /// <summary>⭐ <c>CE-3090</c> — stay put lying down and return fire: a WOUNDED unit with no cover (fleeing in the open would make it
        /// an easy target — 🔒 user <c>2026-10-07</c>).</summary>
        HoldProne        = 6
    }
}
