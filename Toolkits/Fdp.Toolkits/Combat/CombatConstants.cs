namespace FDP.Toolkit.Combat
{
    /// <summary>
    /// Compile-time constants for the Combat toolkit.
    /// </summary>
    public static class CombatConstants
    {
        // ── Weapon action IDs ─────────────────────────────────────────────────
        // Written into WeaponChannel.ActiveAction by BTree/HSM nodes.
        // Consumed by WeaponDispatcherSystem which routes to the registered IActionExecutor.

        /// <summary>
        /// Action ID for AimAndFire (registers <c>AimAndFireExecutor</c> at this slot).
        /// Value matches DESIGN.md §9.4: <c>static class CombatActions { const ushort AimAndFire = 1; }</c>
        /// </summary>
        public const ushort ActionIdAimAndFire = 1;

        // ── Event IDs ─────────────────────────────────────────────────────────
        // Range 5001–5099 is reserved for combat-domain events.
        // 5001 was previously defined in FDP.Toolkit.Physics.PhysicsConstants.HitEventId.
        // It is preserved unchanged here so that any existing serialised data or protocol
        // contracts that reference the numeric ID continue to work.

        /// <summary>Event ID for <see cref="FDP.Toolkit.Combat.Contracts.HitEvent"/> (originally in FDP.Toolkit.Physics; moved to Fdp.Kernel in BATCH-10; moved to Combat.Contracts in DEBT-031).</summary>
        public const int HitEventId = 5001;

        /// <summary>Event ID for <see cref="Events.FireRequestEvent"/>.</summary>
        public const int FireRequestEventId = 5002;

        /// <summary>Event ID for <see cref="Events.WeaponFireIntent"/>.</summary>
        public const int WeaponFireIntentEventId = 5003;

        /// <summary>Event ID for <see cref="Events.WeaponFireNotification"/>.</summary>
        public const int WeaponFireNotificationEventId = 5004;

        /// <summary>Event ID for <see cref="global::FDP.Toolkit.Combat.Contracts.DetonationNotification"/> (moved to Contracts in BS1-T010).</summary>
        public const int DetonationNotificationEventId = 5005;

        /// <summary>Event ID for <see cref="Events.DamageAssessedEvent"/>.</summary>
        public const int DamageAssessedEventId = 5006;

        // ── Bullet / projectile constants ─────────────────────────────────────

        /// <summary>Damage applied per bullet hit (sourced from BallisticProjectile.Damage on spawn).</summary>
        public const float DefaultBulletDamage  = 25f;

        /// <summary>Radius of the bounding-circle collider added to each bullet entity (metres).</summary>
        public const float BulletColliderRadius  = 0.1f;

        /// <summary>
        /// Collision layer assigned to bullet entities (bit 1).
        /// Distinct from the generic entity layer (bit 0) so bullets do not collide with each other.
        /// </summary>
        public const int   BulletCollisionLayer  = 2;

        /// <summary>
        /// Maximum number of simulation ticks a bullet entity may live before being culled.
        /// At 60 Hz this is approximately 2 seconds.
        /// </summary>
        public const uint  BulletLifetimeTicks   = 120;
    }
}
