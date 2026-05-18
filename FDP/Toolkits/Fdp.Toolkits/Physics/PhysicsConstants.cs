namespace Fdp.Toolkit.Physics
{
    /// <summary>
    /// Shared numeric constants for the Physics toolkit.
    /// Using named constants throughout ensures a single point of truth for all
    /// magic numbers; raw literals in production code are forbidden.
    /// </summary>
    public static class PhysicsConstants
    {
        // ── Raycast batch limits ──────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of ray requests that can be batched per frame.
        /// Pre-allocated at module init; do not exceed this limit per frame.
        /// </summary>
        public const int RaycastBatchCapacity = 4096;

        /// <summary>
        /// Maximum number of broadphase candidates inspected per ray per frame.
        /// Entities beyond this limit are silently dropped from narrow-phase testing.
        /// In practise 64 is sufficient for typical scenario densities; raise if you
        /// observe missed hits in high-density areas.
        /// </summary>
        public const int MaxBroadphaseCandidates = 64;

        /// <summary>
        /// Extra expansion radius (metres) added to the AABB radius when querying the
        /// spatial hash grid for broadphase candidate entities. Ensures that entities
        /// whose bounding circle extends into the ray's axis-aligned bounding box are
        /// included even if their centre is slightly outside the ray's tight bounding box.
        /// </summary>
        public const float QueryExpansionRadius = 5f;

        // ── Collision layers ──────────────────────────────────────────────────

        /// <summary>
        /// CollisionLayer bitmask for all physical (non-bullet) entities.
        /// Rays fired at layer mask <c>EntityCollisionLayer</c> will hit soldiers, vehicles, etc.
        /// Distinct from <see cref="CombatConstants.BulletCollisionLayer"/> (bit 1) defined in
        /// <c>FDP.Toolkit.Combat</c>.
        /// </summary>
        public const int EntityCollisionLayer = 1;

        // ── Event IDs ─────────────────────────────────────────────────────────────
        // Range 5001–5099 is reserved for FDP.Toolkit.Physics events.

        /// <summary>
        /// Event ID for <c>FDP.Toolkit.Combat.Events.HitEvent</c> (migrated from Physics in BATCH-09).
        /// Retained here for backward-compatibility of RayId packing/unpacking logic.
        /// Must equal <c>CombatConstants.HitEventId</c>.
        /// </summary>
        public const int HitEventId = 5001;

        // ── RayId encoding convention ─────────────────────────────────────────────
        // Bit 63 (sign bit) selects the ray type:
        //   0 → LOS check:  high 32 bits = ObserverEntityIndex, low 32 bits = TargetEntityIndex
        //   1 → Bullet ray: high 31 bits = BulletEntityIndex (sign bit cleared before extracting)

        /// <summary>Packs observer and target indices into a LOS RayId (bit 63 = 0).</summary>
        public static long PackLosRayId(int observerIndex, int targetIndex)
            => ((long)observerIndex << 32) | (uint)targetIndex;

        /// <summary>Packs a bullet entity index into a bullet RayId (bit 63 = 1).</summary>
        public static long PackBulletRayId(int bulletEntityIndex)
            => (1L << 63) | (uint)bulletEntityIndex;

        /// <summary>Returns true when the RayId represents a bullet ray (bit 63 set).</summary>
        public static bool IsBulletRay(long rayId) => (rayId & (1L << 63)) != 0;
    }
}
