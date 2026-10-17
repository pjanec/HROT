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

        /// <summary>⭐ M8 (D-I, R-169) — the munition type of the LOADED round (the weapon brain's choice from the mount's loadout);
        /// 0 = the mount's own <c>AmmoGuid</c>.</summary>
        public long AmmoGuid;

        /// <summary>⭐ Indirect fire IF-1 (I3, R-169 — APPENDED) — the brain's fire solution: the charge (1-based; 0 = none, today's arc)
        /// and the arc (1 = high). The SimHost flies exactly this (no second decision).</summary>
        public byte Charge;
        public byte HighArc;

        /// <summary>⭐ <c>CE-2180</c> (Air Ops §8a, R-169 — APPENDED) — the synced sim time (s) the brain raised the shot; 0 = now. A bomb
        /// starts from its carrier as it was then, flown forward to the SimHost's now.</summary>
        public double RaisedAt;

        /// <summary>⭐ <c>CE-3235</c> (B-13, §7.10 PU-6, R-169 — APPENDED) — the fuze a time-fuzed round flies with (s); 0 = its warhead's
        /// own. A live round picked up and thrown back keeps what it had left.</summary>
        public float FuzeSeconds;
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

        /// <summary>⭐ CE-1111 / CE-2188 (BS-1-DESIGN §5.4b F1, §5.4c; R-169 — APPENDED) — the munition type FIRED (a flare / chaff for a
        /// dispenser); 0 = unknown.</summary>
        public long AmmoGuid;

        /// <summary>⭐ CE-1111 — the network id of the round this shot spawned (a replicated round); 0 = none (a bullet, a decoy).</summary>
        public long RoundEntityId;

        /// <summary>⭐ CE-1111 — where it left, world metres.</summary>
        public float OriginX;
        public float OriginY;
        public float OriginZ;

        /// <summary>⭐ CE-1111 — its velocity at launch (for a decoy, the aircraft's), m/s.</summary>
        public float VelocityX;
        public float VelocityY;
        public float VelocityZ;

        /// <summary>⭐ CE-1111 — rounds this message stands for (1 per shot, 1 per decoy).</summary>
        public int Quantity;

        /// <summary>⭐ CE-1111 — the synced sim time (s) it was fired.</summary>
        public double FiredAt;

        /// <summary>Node that wrote this sample (CE-1148 S2, Node Roles §3.5): every node reads the topic and drops its own. 0 = unknown.</summary>
        public long SourceNodeId;
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

    // ── Round impact (CE-1128, DESIGN_Visual_Effects §10) ──────────────────

    /// <summary>
    /// ⭐ <c>CE-1128</c> — a round marked a surface of the world (Muscle → every map host): entered or left a piece it passed through, or
    /// stopped on one. Cosmetic ⇒ BestEffort: a lost puff is better than a backlog. ⛔ Not <see cref="MunitionDetonation"/> — that is a combat
    /// message (area effect, damage, the observer's burst memory). docs/DESIGN_Visual_Effects.md §10 IM-3.
    /// </summary>
    [DdsTopic("RoundImpact")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 64)]
    public partial struct RoundImpact
    {
        /// <summary>Network entity ID of the shooter (0 = unknown).</summary>
        public long ShooterEntityId;

        /// <summary>Where the round met the surface.</summary>
        public float X;
        public float Y;
        public float Z;

        /// <summary>The face's outward normal (all 0 = unknown).</summary>
        public float NormalX;
        public float NormalY;
        public float NormalZ;

        /// <summary>The round's travel direction (any length; all 0 = unknown).</summary>
        public float TravelX;
        public float TravelY;
        public float TravelZ;

        /// <summary>The TKB type id of the munition (0 = unknown).</summary>
        public long MunitionType;

        /// <summary>0 entered, 1 left, 2 stopped (<c>ImpactKind</c>).</summary>
        public byte Kind;

        /// <summary>Node that wrote this sample (CE-1148 S2, Node Roles §3.5): every node reads the topic and drops its own. 0 = unknown.</summary>
        public long SourceNodeId;
    }

    // ── Air defence (CE-2189, DESIGN_Missiles_And_Ammo §3c) ───────────

    /// <summary>
    /// ⭐ <c>CE-2189</c> — a guided missile was launched at an aircraft (Muscle → its Brain): where from, its seeker, the flight time to it.
    /// The near-miss pattern; the brain turns it into <c>SensorChange.MissileWarning</c>.
    /// </summary>
    [DdsTopic("MissileWarning")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 8)]
    public partial struct MissileWarning
    {
        /// <summary>Network entity ID of the aircraft the missile was launched at.</summary>
        public long UnitEntityId;
        public float FromX;
        public float FromY;
        public float FromZ;
        /// <summary>The seeker (<c>GuidanceKind</c>).</summary>
        public byte Seeker;
        public float TimeToImpactS;
    }

    // ⛔ CE-2188's DispenseCountermeasure (dt 134) was RETIRED by BS-1-DESIGN §5.4c: a release is an ordinary WeaponFireRequest on the
    //   dispenser mount, and the SimHost reports it as a WeaponFire.

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

        /// <summary>Node that wrote this sample (CE-1148 S2, Node Roles §3.5): every node reads the topic and drops its own. 0 = unknown.</summary>
        public long SourceNodeId;
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

        /// <summary>⭐ <c>CE-3210</c> (hit location HL-6, R-169 — APPENDED) — the body part it landed on, as its id + 1 (a person's
        /// <c>PersonPart</c>); ⭐ 0 = none (an area effect, a vehicle until S2) — so a sender that never sets it reads as "no part", not
        /// as part 0.</summary>
        public byte BodyPart;

        /// <summary>⭐ <c>CE-3219</c> (civilians B-1, CV-6 — APPENDED) — the hit's effect kind (<c>EffectKind</c>; 0 = kinetic, damage only).</summary>
        public byte EffectKind;

        /// <summary>⭐ <c>CE-3219</c> — how long the effect lasts (s).</summary>
        public float EffectSeconds;
    }
}
