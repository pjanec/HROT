namespace Fdp.Toolkit.Combat
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

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-8) — fire at a ground POINT (<c>FireAtPointExecutor</c>): a thrown grenade, a mortar.</summary>
        public const ushort ActionIdFireAtPoint = 2;

        /// <summary>⭐ WL-3 (DESIGN_Weapon_Layers W-1) — keep the gun on a target WITHOUT firing (<c>TrackTargetExecutor</c>).</summary>
        public const ushort ActionIdTrackTarget = 3;

        // ── Event IDs ─────────────────────────────────────────────────────────
        // Range 5001–5099 is reserved for combat-domain events.
        // 5001 was previously defined in FDP.Toolkit.Physics.PhysicsConstants.HitEventId.
        // It is preserved unchanged here so that any existing serialised data or protocol
        // contracts that reference the numeric ID continue to work.

        /// <summary>Event ID for <see cref="FDP.Toolkit.Combat.Contracts.HitEvent"/> (originally in FDP.Toolkit.Physics; moved to Fdp.Core in BATCH-10; moved to Combat.Contracts in DEBT-031).</summary>
        public const int HitEventId = 5001;

        /// <summary>Event ID for <see cref="Events.FireRequestEvent"/>.</summary>
        public const int FireRequestEventId = 5002;

        /// <summary>Event ID for <see cref="Events.WeaponFireIntent"/>.</summary>
        public const int WeaponFireIntentEventId = 5003;

        /// <summary>Event ID for <see cref="Events.WeaponFireNotification"/>.</summary>
        public const int WeaponFireNotificationEventId = 5004;

        /// <summary>Event ID for <see cref="global::Fdp.Toolkit.Combat.Contracts.DetonationNotification"/> (moved to Contracts in BS1-T010).</summary>
        public const int DetonationNotificationEventId = 5005;

        /// <summary>Event ID for <see cref="Events.DamageAssessedEvent"/>.</summary>
        public const int DamageAssessedEventId = 5006;

        /// <summary>⭐ <c>CE-3064</c> — event id of <see cref="Events.NearMissEvent"/>.</summary>
        public const int NearMissEventId = 5007;

        /// <summary>⭐ <c>CE-2189</c> (DESIGN_Missiles_And_Ammo §3c) — event id of <see cref="Events.MissileWarningEvent"/>.</summary>
        public const int MissileWarningEventId = 5008;

        /// <summary>⛔ RESERVED — was <c>DispenseCountermeasureIntent</c> (CE-2188), retired by BS-1 §5.4c: a release is an ordinary
        /// <see cref="Events.WeaponFireIntent"/> on the dispenser mount. ⚠ Do NOT reuse 5009.</summary>
        public const int RetiredDispenseCountermeasureEventId = 5009;

        /// <summary>⭐ <c>CE-1128</c> — <see cref="Contracts.RoundImpactNotification"/>: a round marked a surface (docs/DESIGN_Visual_Effects.md §10).</summary>
        public const int RoundImpactNotificationEventId = 5010;

        /// <summary>⭐ <c>CE-3235</c> (B-13) — <see cref="PickUpRoundEvent"/>: a man asks the round's owner for a lying round.</summary>
        public const int PickUpRoundEventId = 5011;

        /// <summary>⭐ <c>CE-3235</c> (B-13) — <see cref="PickUpGrantEvent"/>: the round's owner gives it to the man's brain owner.</summary>
        public const int PickUpGrantEventId = 5012;


        /// <summary>⭐ <c>CE-3064</c> (R-206) — a bullet passing within this distance (m) of a unit it was not fired by is a near miss.</summary>
        public const float NearMissRadius = 3f;

        // ── Bullet / projectile constants ─────────────────────────────────────

        /// <summary>Damage applied per bullet hit when the mount states none — ⭐ Stage 0: an alias of
        /// <see cref="Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.BulletDamage"/>, the one home of the engine defaults.</summary>
        public const float DefaultBulletDamage  = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.BulletDamage;

        /// <summary>Radius of the bounding-circle collider added to each bullet entity (metres).</summary>
        public const float BulletColliderRadius  = 0.1f;

        /// <summary>
        /// ⭐ <c>CE-3059</c> — how far along the aim line a bullet starts (metres), capped at half the distance to the
        /// target. A simplification (user, <c>2026-10-05</c>): squad-mates standing on the shooter's spot are not hit. ⚠ A
        /// friendly further out on the line is still hit; the hold-fire guard (<c>LineOfFire</c>) covers that.
        /// </summary>
        public const float MuzzleOffsetMeters    = 1.0f;

        /// <summary>
        /// Collision layer assigned to bullet entities (bit 2). A round's ray masks out exactly this bit, so rounds never meet
        /// each other and EVERY other layer — the entity layer (bit 0) and <see cref="CombatCollisionLayer"/> (bit 1) — is hit.
        /// ⭐ <c>CE-2182</c> (AT1, R-2021): it was bit 1, the SAME bit as the combat layer, so a body on the combat layer alone
        /// (every aircraft) could never be met by a round. 📄 DESIGN_Air_Operations §7c.
        /// </summary>
        public const int   BulletCollisionLayer  = 4;

        /// <summary>
        /// ⭐ <c>CE-2182</c> — the combat collision layer (bit 1): "a round can hit this". <c>CombatTkbTranslator</c> ORs it onto a
        /// combat platform's collider (or stamps a collider on it when no other translator did — an aircraft, a weapon system).
        /// </summary>
        public const int   CombatCollisionLayer  = 2;

        /// <summary>
        /// Maximum number of simulation ticks a bullet entity may live before being culled.
        /// At 60 Hz this is approximately 2 seconds.
        /// </summary>
        public const uint  BulletLifetimeTicks   = 120;

        /// <summary>⭐ R-217 — how long a round stopped by the terrain is kept (frozen at the wall) before it is destroyed: long enough
        /// for the raycasts of its last segments to resolve (they take three ticks), so a unit in front of the wall is still hit.</summary>
        public const uint  StoppedRoundGraceTicks = 8;

        /// <summary>⭐ <c>CE-2203</c> — how long an IMPACT round that stopped waits before it bursts there: the raycast lag (a segment's
        /// result resolves three ticks later, Building Interiors §3d) + one, so a contact hit on an earlier segment ends it first.</summary>
        public const uint  ContactHitWaitTicks = 4;

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>) — the lifetime of a WARHEAD round on an arc, or lying on the ground on a time fuze (a
        /// mortar bomb flies for tens of seconds; a thrown grenade lies until its fuze).</summary>
        public const uint  ArcRoundLifetimeTicks = 3600;

        /// <summary>⭐ <c>CE-1032</c> (W-8) — gravity on an arc round, m/s².</summary>
        public const float Gravity = 9.81f;

        /// <summary>⭐ <c>CE-1032</c> (W-8) — a mount at least this fast fires its arc HIGH (a mortar: steep, over walls and onto
        /// roofs); slower is a THROW (low: a grenade).</summary>
        public const float HighArcMinMuzzleVelocity = 40f;
    }
}
