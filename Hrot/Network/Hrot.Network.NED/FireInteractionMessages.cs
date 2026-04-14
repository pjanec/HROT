using CycloneDDS.Schema;
using Hrot.NED.Common;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// Transient combat interaction event published by SimHost and consumed by IG.
    /// </summary>
    [DdsTopic("FireInteractionEvent")]
    [DdsIdlFile("hrot-sim-msgs")]
    public partial struct FireInteractionEvent
    {
        public float ShooterX;
        public float ShooterY;
        public float TargetX;
        public float TargetY;
    }

    // ── WeaponFire Pipeline (POC simplified) ──────────────────────────────────

    /// <summary>
    /// DDS message published by the Brain node when it issues a weapon-fire command.
    /// Consumed by the Muscle node's <c>WeaponFireRequestIngressTranslator</c>, which
    /// re-emits a local <see cref="global::Fdp.Toolkit.Combat.Events.WeaponFireIntent"/>
    /// on the Muscle's ECS event bus.
    /// </summary>
    [DdsTopic("WeaponFireRequest")]
    public partial struct WeaponFireRequest
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public long TargetEntityId;

        /// <summary>Zero-based weapon slot index (POC: always 0).</summary>
        public int WeaponIndex;
    }

    /// <summary>
    /// DDS message published by the Muscle node after a bullet has been spawned.
    /// Consumed by the IG to trigger a muzzle-flash visual effect.
    /// </summary>
    [DdsTopic("WeaponFire")]
    public partial struct WeaponFire
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public long TargetEntityId;

        /// <summary>Zero-based weapon slot index.</summary>
        public int WeaponIndex;
    }

    // ── Detonation / Damage Pipeline (POC simplified) ─────────────────────────

    /// <summary>
    /// DDS message published by the Muscle node when a bullet impact is resolved.
    /// Consumed by the IG (explosion particle) and the Damage Assessment Module.
    /// </summary>
    [DdsTopic("MunitionDetonation")]
    public partial struct MunitionDetonation
    {
        /// <summary>Network entity ID of the shooter.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the struck entity.</summary>
        public long HitEntityId;

        /// <summary>World-space X coordinate of the hit position.</summary>
        public float HitX;

        /// <summary>World-space Y coordinate of the hit position.</summary>
        public float HitY;

        /// <summary>World-space Z coordinate of the hit position.</summary>
        public float HitZ;
    }

    /// <summary>
    /// DDS message published by the Damage Assessment Module after HP loss has been
    /// computed for a bullet impact.  The authoritative node applies the damage to the
    /// entity's <c>Health</c> component upon receiving this message.
    /// </summary>
    [DdsTopic("EntityHitDamage")]
    public partial struct EntityHitDamage
    {
        /// <summary>Network entity ID of the struck entity.</summary>
        public long HitEntityId;

        /// <summary>Total computed HP loss.</summary>
        public float TotalDamage;
    }
}
