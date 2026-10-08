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
    // ⭐ CE-3095 — events, so Reliable + KeepAll: with no [DdsQos] the reader kept ONE sample, and two shots landing in the same
    //   take overwrote each other. 📐 Measured on WeaponFireRequest (ua-universal-soldier): the rifleman's shot and a hostile's,
    //   both at f5008 — his never reached SimHost. The other fire-pipeline events share the same failure mode.
    [DdsTopic("WeaponFireRequest")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct WeaponFireRequest
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target; ⭐ <c>CE-1032</c> (W-8) 0 = fire at the point (<see cref="PointX"/>…).</summary>
        public long TargetEntityId;

        /// <summary>Zero-based weapon slot index (POC: always 0).</summary>
        public int WeaponIndex;

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-8) — the aim point of a point fire (a thrown grenade, a mortar), world metres.</summary>
        public float PointX;
        public float PointY;
        public float PointZ;
    }

    /// <summary>
    /// DDS message published by the Muscle node after a bullet has been spawned.
    /// Consumed by the IG to trigger a muzzle-flash visual effect.
    /// </summary>
    [DdsTopic("WeaponFire")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]   // CE-3095
    public partial struct WeaponFire
    {
        /// <summary>Network entity ID of the firing entity.</summary>
        public long ShooterEntityId;

        /// <summary>Network entity ID of the intended target.</summary>
        public long TargetEntityId;

        /// <summary>Zero-based weapon slot index.</summary>
        public int WeaponIndex;
    }

    // ── Near miss (CE-3064, R-206) ─────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>CE-3064</c> — a round passed close to a unit without being fired by its side (Muscle → Brain): the unit is being
    /// shot at. ⛔ No shooter on purpose. docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 G.
    /// </summary>
    [DdsTopic("NearMiss")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 8)]
    public partial struct NearMiss
    {
        /// <summary>Network entity ID of the unit the round passed.</summary>
        public long UnitEntityId;

        /// <summary>The closest point of the round's path to the unit.</summary>
        public float X;
        public float Y;
        public float Z;
    }

    // ── Detonation / Damage Pipeline (POC simplified) ─────────────────────────

    /// <summary>
    /// DDS message published by the Muscle node when a bullet impact is resolved.
    /// Consumed by the IG (explosion particle) and the Damage Assessment Module.
    /// </summary>
    [DdsTopic("MunitionDetonation")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]   // CE-3095
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

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-2) — the TKB type id of the munition (0 = unknown); a receiver looks its warhead up.
        /// ⛔ Never the warhead numbers (§3e — one source). <see cref="HitEntityId"/> 0 = it detonated on the terrain.</summary>
        public long MunitionType;
    }

    /// <summary>
    /// DDS message published by the Damage Assessment Module after HP loss has been
    /// computed for a bullet impact.  The authoritative node applies the damage to the
    /// entity's <c>Health</c> component upon receiving this message.
    /// </summary>
    [DdsTopic("EntityHitDamage")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]   // CE-3095
    public partial struct EntityHitDamage
    {
        /// <summary>Network entity ID of the struck entity.</summary>
        public long HitEntityId;

        /// <summary>Total computed HP loss.</summary>
        public float TotalDamage;
    }
}
