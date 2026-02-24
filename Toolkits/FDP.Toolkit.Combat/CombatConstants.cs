namespace FDP.Toolkit.Combat
{
    /// <summary>
    /// Compile-time constants for the Combat toolkit.
    /// </summary>
    public static class CombatConstants
    {
        // ── Event IDs ─────────────────────────────────────────────────────────
        // Range 5001–5099 is reserved for combat-domain events.
        // 5001 was previously defined in FDP.Toolkit.Physics.PhysicsConstants.HitEventId.
        // It is preserved unchanged here so that any existing serialised data or protocol
        // contracts that reference the numeric ID continue to work.

        /// <summary>Event ID for <see cref="Events.HitEvent"/> (migrated from FDP.Toolkit.Physics).</summary>
        public const int HitEventId = 5001;

        /// <summary>Event ID for <see cref="Events.FireRequestEvent"/>.</summary>
        public const int FireRequestEventId = 5002;
    }
}
