using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Hrot.NED.Common;

namespace Hrot.NED.Descriptors
{

    // Merged entity spatial topic: position, orientation, velocity, and angular velocity.
    // Replaces the former separate GeoSpatial and GeoSpatialDR topics into a single unified source of truth.
    [DdsTopic("WorldPos")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct WorldPos
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public int EntityId;

        public DateTime Time;         // Sync timestamp (exercise FILETIME). 0 = unspecified.
        public GeoPoint Pos;          // Latitude [deg], longitude [deg], altitude [m] above WGS84 ellipsoid
        public EulerOri Ori;          // Static orientation: Heading [deg], pitch [deg], roll [deg]
        public AngularVector Vel;     // Velocity vector: Azim=heading [deg], Elev=pitch [deg], Length=speed [m/s]
        public AngularVector Acc;     // Acceleration vector [m/s²]; same coordinate system as Vel
        public EulerRate RotVel;      // Angular velocity: Heading [deg/s], Pitch [deg/s], Roll [deg/s]
    }



    // The authoritative node's Health for the whole entity.
    //
    // ⭐⭐⭐ CARRIES Current AND Max — NOT A PRECALCULATED PERCENTAGE.
    //    🔒 User ruling, 2026-09-05: "Max Health (== Max Damage) ... is usually selectable in scenario
    //    and TKB should be just a fallback if not set in scenario, so having both Max and Current makes
    //    sense as ECS component AND network descriptor, no precalculated percentages."
    //
    // ⛔ WHY Max MUST TRAVEL, and it is the whole reason this descriptor changed.
    //    `Health` is seeded per-node by CombatTkbTranslator:40 from the TKB platform definition, and a
    //    scenario-authored override (hill-attack sets {50,50}) exists ONLY on the node that loaded the
    //    scenario. 📐 Measured 2026-09-05 on a live --mode all: entity 1006 read Health 50/50 on CGF
    //    (the Brain, which applies damage) and 3000/3000 on SimHost and IG. ⇒ a percentage computed
    //    against a per-node Max is meaningless across nodes; shipping the pair makes the receiver's copy
    //    identical to the authority's by construction.
    //
    // ⚠ The topic and descriptor ordinal keep the historical `EntityDamage` name so the wire identity
    //   (dtEntityDamage = 30) is unchanged. The PAYLOAD is health, not damage.
    // 📄 docs/designs/brain-split/BS-1-DESIGN.md §6.5 · Blueprint_Issues_Tracker CE-196.
    [DdsTopic("EntityDamage")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityDamage
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public int EntityId;

        /// <summary>Current hit points on the authoritative node. 0 = destroyed.</summary>
        public float Current;

        /// <summary>Maximum hit points — the scenario's value when it authored one, else the TKB default.</summary>
        public float Max;
    }

    // ⭐ Buildings Stage 5b — a terrain DOOR's live state, published by the door's owner, applied on every other node, and mirrored
    //   into each node's TerrainWorld (sight, fire, the 5c path filter). 📄 docs/DESIGN_Building_Interiors.md §3a, §3j.
    //   ⭐ The KEY rides along: it is static, but a replica needs it to know WHICH terrain door the entity stands for (§3b K2 — a
    //   string, never a number). TransientLocal + KeepLast(1) keyed by entity: a late joiner gets every door's current state.
    [DdsTopic("EntityDoorState")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    [DdsManaged]
    public partial struct EntityDoorState
    {
        [DdsKey]
        public int EntityId;

        /// <summary><c>TerrainDoorState</c>: 0 Open · 1 Closed · 2 Locked · 3 Destroyed.</summary>
        public byte State;

        /// <summary>The terrain-object key, <c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>.</summary>
        public string Key;
    }

    // ⭐ Weapon systems WS-1 (W-C, R-136; docs/designs/weapons/DESIGN_Weapon_Systems.md §2) — a weapon system's deploy state (Stowed ·
    //   Deploying · Deployed · Packing), published by the node owning dtWeaponSystemState (its brain group) on change and applied on every
    //   other node (the drawing and the fire gate read it). Reliable + TransientLocal + KeepLast(1) keyed by entity, like the door: a late
    //   joiner gets every weapon's current state.
    [DdsTopic("EntityWeaponSystemState")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityWeaponSystemState
    {
        [DdsKey]
        public int EntityId;

        /// <summary><c>WeaponDeploy</c>: 0 Stowed · 1 Deploying · 2 Deployed · 3 Packing.</summary>
        public byte Deploy;

        /// <summary>Sim time (s) a Deploying / Packing ends.</summary>
        public double Until;
    }

    // ⭐ CE-3216 S1-0 (R-3012; docs/designs/weather/DESIGN_Weather_And_Time.md W-4, W-8) — the scenario clock (the authored START TIME)
    //   on the World Environment entity, published by its owner (the creating Brain) on change and applied on every other node. Every
    //   node then derives UTC = start + its own synced sim time (ScenarioTime.Utc) — nothing is sent per frame. Reliable +
    //   TransientLocal + KeepLast(1) keyed by entity, like the door: a late joiner gets the current start time.
    // ⭐ AQ93 §12 N4 (R-4056) — the scenario's TRAFFIC SETTINGS (TrafficDemand), published by the owner of the Traffic group (the
    //   traffic node the creating Brain granted it to; the Brain itself when no traffic node exists) on change, applied everywhere else.
    //   Reliable + TransientLocal + KeepLast(1) keyed by entity, like the scenario clock: a late joiner gets the current settings.
    [DdsTopic("EntityTrafficDemand")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityTrafficDemand
    {
        [DdsKey]
        public int EntityId;

        /// <summary><c>TrafficDemand.TargetCars</c>.</summary>
        public int TargetCars;

        /// <summary><c>TrafficDemand.Seed</c>.</summary>
        public uint Seed;

        /// <summary><c>TrafficDemand.StepSeconds</c>.</summary>
        public float StepSeconds;

        /// <summary>⭐ AQ93 §15 K1 — <c>TrafficDemand.TargetPedestrians</c>.</summary>
        public int TargetPedestrians;

        /// <summary><c>TrafficDemand.ShotPanicRadius</c>.</summary>
        public float ShotPanicRadius;

        /// <summary><c>TrafficDemand.BlastPanicRadius</c>.</summary>
        public float BlastPanicRadius;

        /// <summary><c>TrafficDemand.MaxConverted</c>.</summary>
        public int MaxConverted;
    }

    [DdsTopic("EntityScenarioClock")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityScenarioClock
    {
        [DdsKey]
        public int EntityId;

        /// <summary>UTC ticks at sim time 0 (<c>ScenarioClock.StartUtcTicks</c>).</summary>
        public long StartUtcTicks;

        /// <summary>The display offset from UTC, minutes (<c>ScenarioClock.DisplayUtcOffsetMinutes</c>).</summary>
        public short DisplayUtcOffsetMinutes;
    }

    // ⭐ CE-3216 S1 (R-3012; docs/designs/weather/DESIGN_Weather_And_Time.md §1, W-0…W-3) — AUTHORED weather (WeatherConditions): the global
    //   weather on the World Environment, a regional one on a Weather Region. Published by the owner (the creating Brain) on change, applied
    //   on every other node; every node derives the weather at any point itself (WeatherField). Flat fields (the dtPartDamage shape): 8 wind
    //   levels, 4 cloud layers. Reliable + TransientLocal + KeepLast(1) keyed by entity: a late joiner gets the current weather.
    [DdsTopic("EntityWeather")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityWeather
    {
        [DdsKey]
        public int EntityId;

        public float GroundTemperatureC;
        public float LapseRateKPerKm;
        public float SeaLevelPressureHpa;
        public float RelativeHumidity;
        public float VisibilityM;
        public float FogTopM;
        public float FogVisibilityM;
        public byte Precipitation;
        public float PrecipitationMmPerHour;
        public float FeatherM;

        /// <summary>Wind levels used (≤ 8): altitude MSL m · from ° · speed m/s · gust m/s.</summary>
        public byte WindCount;
        public float W0Alt; public float W0From; public float W0Speed; public float W0Gust;
        public float W1Alt; public float W1From; public float W1Speed; public float W1Gust;
        public float W2Alt; public float W2From; public float W2Speed; public float W2Gust;
        public float W3Alt; public float W3From; public float W3Speed; public float W3Gust;
        public float W4Alt; public float W4From; public float W4Speed; public float W4Gust;
        public float W5Alt; public float W5From; public float W5Speed; public float W5Gust;
        public float W6Alt; public float W6From; public float W6Speed; public float W6Gust;
        public float W7Alt; public float W7From; public float W7Speed; public float W7Gust;

        /// <summary>Cloud layers used (≤ 4): base MSL m · thickness m · octas · kind.</summary>
        public byte CloudCount;
        public float C0Base; public float C0Thick; public byte C0Octas; public byte C0Kind;
        public float C1Base; public float C1Thick; public byte C1Octas; public byte C1Kind;
        public float C2Base; public float C2Thick; public byte C2Octas; public byte C2Kind;
        public float C3Base; public float C3Thick; public byte C3Octas; public byte C3Kind;
    }

    // ⭐ WL-1b (CE-1049) — a turret's pose (its part child's TurretPose), published by the node owning dtTurretPose (the Muscle) and
    //   applied on every other node's turret part, so a viewer — IG included — sees the turret turn. 📄 docs/DESIGN_Weapon_Layers.md
    //   §5b, W-9, W-10. Keyed (hull network id, part id = 2000 + turret index) — the EQS sensor key shape (EqsSensorConfigTopic).
    //   BestEffort + TransientLocal + KeepLast 1 like WorldPos: a lost sample is repaired by the next change or the heartbeat.
    [DdsTopic("EntityTurretPose")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityTurretPose
    {
        /// <summary>Network id of the hull (the part's <c>PartMetadata.ParentEntity</c>).</summary>
        [DdsKey]
        public long ParentNetworkId;

        /// <summary>The turret part's <c>PartMetadata.InstanceId</c> (<c>TurretParts.FirstPartId</c> + turret index).</summary>
        [DdsKey]
        public int PartId;

        /// <summary>Traverse, radians, counter-clockwise from the hull's forward.</summary>
        public float Azimuth;

        /// <summary>Gun elevation, radians, up from the turret plane.</summary>
        public float Elevation;

        /// <summary>⭐ W-10 — traverse and elevation rates (rad/s) for receiver dead reckoning. Written 0 until W-10 is built.</summary>
        public float AzimuthRate;
        public float ElevationRate;

        /// <summary>The cluster sim time of the sample (<c>SimStampCodec</c>); 0 = unspecified.</summary>
        public DateTime Time;
    }

    // ⭐ WL-2b (CE-1049) — the weapon brain's standing order to ONE turret (W-2, W-11 / R-267), Brain → Muscle, keyed like
    //   EntityTurretPose. Reliable + TransientLocal + KeepLast 1: an order is sent once per change and must arrive.
    //   📄 docs/DESIGN_Weapon_Layers.md §5d.
    [DdsTopic("EntityWeaponTask")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityWeaponTask
    {
        [DdsKey] public long ParentNetworkId;
        [DdsKey] public int PartId;
        /// <summary><c>WeaponTaskMode</c>: 0 Idle · 1 Track · 2 Engage.</summary>
        public byte Mode;
        /// <summary>The target's network id; 0 = aim at the point.</summary>
        public long TargetNetworkId;
        public float PointX;
        public float PointY;
        public float PointZ;
        public byte MountIndex;
        public ushort RoundsGranted;
        public ulong AmmoGuid;
        /// <summary>⭐ WL-3 — seconds between rounds.</summary>
        public float CooldownSeconds;
        public uint Epoch;
        /// <summary>⭐ ui handoff P2 (DESIGN_Indirect_Fire §3.9) — <c>ArcPreference</c> the laid indirect mount's solution chose (APPENDED, R-169).</summary>
        public byte Arc;
    }

    // ⭐ WL-2b (CE-1049) — the weapon muscle's report on that order (W-3), Muscle → Brain, same key.
    [DdsTopic("EntityWeaponTaskStatus")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityWeaponTaskStatus
    {
        [DdsKey] public long ParentNetworkId;
        [DdsKey] public int PartId;
        public uint Epoch;
        /// <summary><c>WeaponTaskState</c>.</summary>
        public byte State;
        /// <summary><c>WeaponTaskFailure</c>.</summary>
        public byte Reason;
        public ushort RoundsFired;
    }

    // ⭐ WL-6 (W-13d, R-272) — the weapon brain's posture of a unit's weapon (at ease / ready / aimed), Brain → every node, keyed by the
    //   unit. Reliable + TransientLocal + KeepLast 1: it changes a few times per engagement and a late joiner must see it.
    //   📄 docs/DESIGN_Weapon_Layers.md §5h.
    [DdsTopic("EntityWeaponPosture")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityWeaponPosture
    {
        [DdsKey] public long EntityId;
        /// <summary><c>WeaponPostureKind</c>: 0 AtEase · 1 Ready · 2 Aimed.</summary>
        public byte Kind;
        /// <summary>The target's network id; 0 = the point (or nothing).</summary>
        public long TargetNetworkId;
        public float PointX;
        public float PointY;
        public float PointZ;
        /// <summary>⭐ ui handoff P4 (W-13e) — the mount the weapon brain fires with (APPENDED, R-169).</summary>
        public byte ActiveMount;
        /// <summary>⭐ CE-3234 (civilians B-12, HI-1) — <c>HeldItemKind</c>: what is in his hand (APPENDED, R-169).</summary>
        public byte HeldItem;
    }

    // ⭐ CE-3173 S4e (docs/DESIGN_Entity_Control.md §8c, M-1) — an operator's motion axes for a body in Manual, Brain → the Muscle,
    //   keyed by the unit. BestEffort + TransientLocal + KeepLast 1 like WorldPos: it changes every frame while driven, only the newest
    //   matters (M-6), and a lost sample is repaired by the next one.
    [DdsTopic("EntityManualDrive")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityManualDrive
    {
        [DdsKey] public long EntityId;
        public float Forward;
        public float Lateral;
        public float Steer;
        public float Yaw;
        public byte Gait;
        public uint Seq;
        public float Vertical;   // ⭐ CE-2168 (Motion Domains MD-D) — a flyer's climb axis; appended (the handoff's one allowed wire change)
    }

    // ⭐ CE-3172 S4e (§8c, C-3) — which station holds a unit, Brain → every node, keyed by the unit. Reliable + TransientLocal +
    //   KeepLast 1: a late joiner must see it; Held = 0 is the release (no revoke message exists to get lost).
    [DdsTopic("EntityPossessedBy")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityPossessedBy
    {
        [DdsKey] public long EntityId;
        /// <summary>1 = held, 0 = released.</summary>
        public byte Held;
        public long StationNodeId;
        /// <summary><c>PossessionMode</c>: 1 Fps.</summary>
        public byte Mode;
        public uint Epoch;
    }

    // ⭐ CE-3174 (§8d, C-7) — a unit's ammunition per mount, Brain → every node, keyed by the unit. Reliable + TransientLocal +
    //   KeepLast 1: it changes per round fired, and a station that possesses the unit later must see it.
    [DdsTopic("EntityWeaponAmmo")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityWeaponAmmo
    {
        [DdsKey] public long EntityId;
        public byte Count;
        public int Ammo0;
        public int Ammo1;
        public int Ammo2;
        public int Ammo3;
        public int Max0;
        public int Max1;
        public int Max2;
        public int Max3;
        public int Mag0;
        public int Mag1;
        public int Mag2;
        public int Mag3;
        // ⭐ CE-1079 (appended) — the breech per mount, its rounds, and the reload/switch bits (DESIGN_Entity_Control §8e)
        public long Load0;
        public long Load1;
        public long Load2;
        public long Load3;
        public int LoadRounds0;
        public int LoadRounds1;
        public int LoadRounds2;
        public int LoadRounds3;
        public byte Busy;
        // ⭐ CE-3232 (B-10, appended) — bit i: mount i is LAID DOWN (cannot fire, arms nobody)
        public byte LaidDown;
    }

    // ⭐ CE-1153 (docs/DESIGN_Entity_Control.md §8h.6) — the brain's verdict on the operator's last use press, Brain → every node, keyed by
    //   the unit. Reliable + TransientLocal + KeepLast 1 (the dtWeaponAmmo shape): STATE, so a lost sample cannot leave the HUD stale.
    [DdsTopic("EntityOperatorUseStatus")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityOperatorUseStatus
    {
        [DdsKey] public long EntityId;
        public byte UseSeq;
        public ushort ActionId;
        public byte Outcome;
        public byte Reason;
    }

    // ⭐ CE-3219 (civilians B-1, CV-6; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3) — a person's stored non-lethal effects (sim seconds),
    //   Brain → every node, keyed by the entity. Reliable + TransientLocal + KeepLast 1: STATE, like dtOperatorUseStatus.
    [DdsTopic("EntityEffectState")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityEffectState
    {
        [DdsKey] public long EntityId;
        public double StunnedUntil;
        public double IrritatedUntil;
        public double KnockedDownUntil;
        public byte Cowering;   // ⭐ ui finding F-3 (civilians design §7.6) — appended: 1 while the behaviour has the person cower
    }

    // ⭐ CE-3224 (civilians B-6, CV-13) — a person's gesture, the brain owner → every node. Reliable + TransientLocal + KeepLast 1,
    //   like EntityEffectState: a late joiner must see who has his hands up.
    [DdsTopic("EntityGesture")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityGesture
    {
        [DdsKey] public long EntityId;
        public byte Kind;
    }

    // ⭐ CE-3238 (civilians B-16, AW-22) — a man's hostile act, the brain owner → every node. Reliable + TransientLocal + KeepLast 1, like
    //   EntityGesture: a late joiner must know who has acted, and how gravely.
    [DdsTopic("EntityHostileAct")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityHostileAct
    {
        [DdsKey] public long EntityId;
        public double SinceSimTime;
        public byte Kind;
    }

    // ⭐ CE-3219 part 3 (civilians B-1, R-3014) — a gas / smoke cloud, its creator → every node. Reliable + TransientLocal + KeepLast 1:
    //   a late joiner must see a cloud still hanging there. Its position is the entity's ordinary transform.
    [DdsTopic("EntityAreaEffectCloud")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityAreaEffectCloud
    {
        [DdsKey] public long EntityId;
        public byte Kind;
        public float Radius;
        public float Height;
        public float SightTransmittance;
        public float IrritantSeconds;
        public double BornAtSimSeconds;
        public float Seconds;
        /// <summary>⭐ CE-3236 (B-14, VC-4) — the venting canister it sits on; 0 = free (APPENDED, R-169).</summary>
        public long SourceNetworkId;
    }

    // ⭐ CE-3210 (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-6) — the damage level per body part, the Health owner
    //   (Brain group) → every node, keyed by the entity. Reliable + TransientLocal + KeepLast 1, like EntityWeaponAmmo: it changes per hit,
    //   and a late joiner must see a man's wounds.
    [DdsTopic("EntityPartDamage")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityPartDamage
    {
        [DdsKey] public long EntityId;
        public byte Plan;
        public byte L0; public byte L1; public byte L2; public byte L3; public byte L4; public byte L5; public byte L6; public byte L7;
        public byte L8; public byte L9; public byte L10; public byte L11; public byte L12; public byte L13; public byte L14; public byte L15;
    }

    /// <summary>⭐ CE-3210 (HL-8) — one mark of <see cref="EntityHitMarks"/>: the part, the point in it (quantised), the face, the kind and
    /// its sequence.</summary>
    [DdsStruct]
    public partial struct DdsHitMark
    {
        public ushort Seq;
        public byte Part;
        public short X;
        public short Y;
        public short Z;
        public byte Face;
        public byte Kind;
    }

    // ⭐ CE-3210 (HL-8; user: "owned by muscle") — where rounds struck a body, the Muscle (MuscleGround group) → every node, keyed by the
    //   entity: the marks still in the ring and the next sequence. BestEffort + TransientLocal + KeepLast 1, like EntityTurretPose: a lost
    //   sample is repaired by the next hit, a late joiner gets the ring.
    [DdsTopic("EntityHitMarks")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityHitMarks
    {
        [DdsKey] public long EntityId;
        public ushort NextSeq;
        [DdsManaged] public List<DdsHitMark> Marks;
    }

    // ⭐ Airborne insertion B-1 (docs/designs/airborne/DESIGN_Airborne_Insertion.md D-1) — a body coming down a rope or under a canopy,
    //   the node that owns his position (MuscleGround group) → every node, keyed by the entity; Kind 0 = his descent is over (the
    //   component removed). Reliable + TransientLocal + KeepLast 1 (the EntityWeaponPosture QoS): it changes a few times per descent and a
    //   late joiner must see it.
    [DdsTopic("EntityDescent")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityDescent
    {
        [DdsKey] public long EntityId;
        /// <summary><c>DescentKind</c>: 0 none (over) · 1 FastRope · 2 StaticLine · 3 FreeFall · 4 Ejection.</summary>
        public byte Kind;
        /// <summary><c>DescentPhase</c> — the animation key (D-5).</summary>
        public byte Phase;
        /// <summary><c>CanopyKind</c>: 0 none · 1 Round · 2 RamAir.</summary>
        public byte Canopy;
        public byte AnchorIndex;
        /// <summary>The carrier he hangs from or left, network id (0 = none).</summary>
        public long AnchorNetworkId;
        public double PhaseSince;
        public float TargetX;
        public float TargetY;
        public float TargetZ;
    }

    // ⭐ CE-3110 I-2b (R-224, 📄 docs/DESIGN_Entity_Interactions.md §7.1c) — a vehicle's passengers, the vehicle's owner (Brain group) →
    //   every node, keyed by the vehicle. Twelve scalar network ids (the EntityWeaponAmmo shape; PassengerBuffer.Capacity = 12 — seats 8–11 appended, F4).
    //   Reliable + TransientLocal + KeepLast 1: a late joiner must know who is aboard.
    [DdsTopic("EntityPassengers")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityPassengers
    {
        [DdsKey] public long EntityId;
        public byte Count;
        public long Passenger0;
        public long Passenger1;
        public long Passenger2;
        public long Passenger3;
        public long Passenger4;
        public long Passenger5;
        public long Passenger6;
        public long Passenger7;
        /// <summary>⭐ <c>CE-2152</c> C-a (R-2011 C5) — each seat's <c>CarryMode</c>, 2 bits per seat (the list's own <c>Modes</c>).</summary>
        public ushort Modes;
        /// <summary>⭐ Team carry T-a (R-2017 T1) — a person carrier's second man (<c>PassengerBuffer.Assistant</c>), network id; 0 = none.
        /// APPENDED (R-169).</summary>
        public long Assistant;
        /// <summary>⭐ F4 (Air Ops §7a, 🔒 user `2026-10-14` "extend the wire capacity") — seats 8–11 (the list is 12 wide) and their carry
        /// modes (bits 16–23 of the list's <c>Modes</c>, as seat i at bits 2(i−8)). APPENDED (R-169).</summary>
        public long Passenger8;
        public long Passenger9;
        public long Passenger10;
        public long Passenger11;
        public ushort ModesHigh;
    }

    // ⭐ CE-3136 P-7a (O3, R-242/R-243) — a static obstacle's box (the TKB's size or a scenario's per-instance one), published by the
    //   obstacle's creator, applied on every other node BEFORE its ghost promotes ([PerInstanceValue] ObstacleShape), so every node bakes
    //   the same box into its terrain. TransientLocal + KeepLast(1) keyed by entity: a late joiner gets every obstacle.
    //   📄 docs/DESIGN_Peek_And_Fire.md §9.
    [DdsTopic("EntityObstacleShape")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityObstacleShape
    {
        [DdsKey]
        public int EntityId;

        /// <summary>Box length along the heading (m).</summary>
        public float Length;

        /// <summary>Box width (m).</summary>
        public float Width;

        /// <summary>Box height (m).</summary>
        public float Height;
    }

    // ── Navigation CQRS descriptors (MOD1-P1T1) ──────────────────────────────
    // These are the DDS wire representations of the engine-side NavigationIntent and
    // NavigationStatus ECS components.  The engine-side enums (NavigationMode,
    // NavigationResult) are duplicated here as byte-backed wire enums (ENavigationMode,
    // ENavigationResult) — see the Dual-Enum Pattern (MOD1-DESIGN §3.1.1a).

    /// <summary>
    /// DDS wire enum mirroring <c>FDP.Toolkit.Navigation.NavigationMode</c>.
    /// Translators in the Hrot layer map between the two representations.
    /// </summary>
    public enum ENavigationMode : byte
    {
        NAV_NONE            = 0,
        NAV_DIRECT_POINT    = 1,
        NAV_FOLLOW_ROUTE    = 2,
        NAV_JOIN_FORMATION  = 3,
        NAV_ROAD_GRAPH      = 4,
        NAV_PATH_TO_POINT   = 5,   // CE-3026 — find a path, then follow it (R-42: permanent)
        NAV_MANUAL          = 6,   // ⭐ CE-3173 — the operator drives the body (ManualDrive); entered once
        NAV_TRAFFIC         = 7,   // ⭐ AQ93 R-4023 — SUMO drives the body (a shadow car); the owner follows it
    }

    /// <summary>
    /// DDS wire enum mirroring <c>FDP.Toolkit.Navigation.NavigationResult</c>.
    /// Translators in the Hrot layer map between the two representations.
    /// </summary>
    public enum ENavigationResult : byte
    {
        RES_IN_PROGRESS       = 0,
        RES_ARRIVED           = 1,
        RES_FAILED_BLOCKED    = 2,
        RES_FAILED_UNREACHABLE = 3,
        // NAV-P0-T4: nav subsystem v2 result codes.
        RES_PATH_FOUND        = 4,
        RES_NO_PATH           = 5,
        RES_FAILED_NO_LAYER   = 6,
        RES_FAILED_INVALID_HANDLE = 7,
    }

    /// <summary>
    /// DDS descriptor for the CQRS navigation command.  Owned by the Brain node.
    /// <c>FinalDestination</c> is encoded as <see cref="GeoPoint"/> (WGS-84);
    /// a translator converts from the ECS <c>Vector2</c> Cartesian representation.
    /// </summary>
    [DdsTopic("NavigationIntent")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NavigationIntent
    {
        /// <summary>Which entity this command targets.</summary>
        [DdsKey]
        public int EntityId;

        /// <summary>Monotonically increasing order identifier; echoed by <see cref="NavigationStatus.IntentId"/>.</summary>
        public uint IntentId;

        /// <summary>Active navigation mode wire value.</summary>
        public ENavigationMode Mode;

        /// <summary>
        /// Destination in WGS-84 geographic coordinates.
        /// Translators convert the ECS Cartesian <c>Vector2</c> to/from this field.
        /// </summary>
        public GeoPoint FinalDestination;

        /// <summary>Desired travel speed (m/s).</summary>
        public float TargetSpeed;

        /// <summary>Arrival tolerance radius (metres).</summary>
        public float ArrivalRadius;

        /// <summary>Route handle allocated by the nav subsystem v2 solver; 0 = none.</summary>
        public int RouteHandle;

        // ⭐ CE-3026 — the rest of the MoveTo order, so the vehicle side plans what the Brain asked for on a cluster exactly
        //   as in the editor (before, these stayed on the Brain's node and a cluster lost them silently).
        /// <summary>Navmesh layer mask; 0 = the entity's own layer.</summary>
        public uint LayerMask;
        /// <summary>Backend force (0 Auto, 1 NavMesh, 2 RoadGraph, 3 Volumetric).</summary>
        public byte BackendForce;
        /// <summary>Behaviour flags (AllowReplan, AutoSendPathOnReplan, corridor preview …).</summary>
        public byte Flags;
        /// <summary>Max internal replans (0 = default).</summary>
        public byte MaxReplans;
        /// <summary>1 = reverse driving allowed.</summary>
        public byte ReverseAllowed;
        /// <summary>⭐⭐ CE-2171 / Air Ops B1 (R-169: APPENDED, reported) — the route ENTITY a FollowRoute follows (network id); the
        /// motion side resolves its own compiled path from it. 0 = none.</summary>
        public long RouteEntityId;
    }

    /// <summary>
    /// DDS descriptor for the CQRS navigation status.  Owned by the Muscle node.
    /// Updated by <c>NavigationExecutionSystem</c> each kinematics tick.
    /// </summary>
    [DdsTopic("NavigationStatus")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct NavigationStatus
    {
        /// <summary>Which entity's navigation state is reported.</summary>
        [DdsKey]
        public int EntityId;

        /// <summary>Echoes <see cref="NavigationIntent.IntentId"/> of the command being executed.</summary>
        public uint IntentId;

        /// <summary>Current result of the active navigation command.</summary>
        public ENavigationResult Result;

        /// <summary>
        /// Arc-length progress along the active route (metres from start).
        /// Mirrors <c>NavState.ProgressS</c> on the Muscle node; used by Brain nodes that do
        /// not hold <c>NavState</c> directly (CQRS feedback channel, PACK-N001).
        /// </summary>
        public float ProgressS;

        // NAV-P0-T4: nav subsystem v2 extended status fields.
        /// <summary>Current execution phase (maps to <c>NavigationPhase</c>).</summary>
        public byte Phase;

        /// <summary>Number of times the path has been replanned for the current intent.</summary>
        public ushort ReplanCount;

        /// <summary>Route handle currently being followed; 0 = none.</summary>
        public int RouteHandle;

        /// <summary>Navmesh version observed when the current path was planned.</summary>
        public uint NavmeshVersionObserved;
    }

    // ── Shared coordinate helper (MOD1-P6T2) ──────────────────────────────────────────

    /// <summary>ENU relative vector used by the raycast and pathfinding pipelines.
    /// Expressed in metres relative to <c>BatchOrigin</c> to limit floating-point error over large maps.</summary>
    [DdsStruct]
    public partial struct RelativeVector3
    {
        /// <summary>Eastward component (metres).</summary>
        public float East;
        /// <summary>Northward component (metres).</summary>
        public float North;
        /// <summary>Upward component (metres).</summary>
        public float Up;
    }

    // ── Dumb Raycast pipeline (MOD1-P6T2) ──────────────────────────────────────────

    /// <summary>A single ray cast request submitted to the Navigation Solver node.</summary>
    [DdsStruct]
    public partial struct DdsRaycastRequest
    {
        public long           RayId;
        public RelativeVector3 Start;
        public RelativeVector3 End;
        public int            LayerMask;
        public long           IgnoreEntityId;
    }

    /// <summary>Batched raycast request published by a Brain node toward the Navigation Solver.</summary>
    [DdsTopic("RaycastRequestBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct RaycastRequestBatch
    {
        [DdsKey] public int          SourceNodeId;
        public uint                   BatchCorrelationId;
        public GeoPoint            BatchOrigin;
        [DdsManaged] public List<DdsRaycastRequest> Requests;
    }

    /// <summary>Hit result for one ray in a batch response.</summary>
    [DdsStruct]
    public partial struct DdsRaycastHit
    {
        public long  RayId;
        public bool  HasHit;
        public long  HitEntityId;
        public float HitT;
    }

    /// <summary>Batched raycast response sent from the Navigation Solver back to the requesting node.</summary>
    [DdsTopic("RaycastResponseBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct RaycastResponseBatch
    {
        [DdsKey] public int          TargetNodeId;
        public uint                   BatchCorrelationId;
        [DdsManaged] public List<DdsRaycastHit> Hits;
    }

    // ── Smart Sensor pipeline (MOD1-P6T2) ─────────────────────────────────────────

    /// <summary>Sensor configuration broadcast for an observer entity. Key = EntityId.</summary>
    [DdsTopic("SensorConfig")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct SensorConfig
    {
        [DdsKey] public long  EntityId;
        public float          VisionRange;
        public float          HearingRange;
        public float          FovDegrees;
    }

    /// <summary>One tracked target entry in a <see cref="SensorTargets"/> sample.</summary>
    [DdsStruct]
    public partial struct DdsTrackedTarget
    {
        public long  TargetEntityId;
        public float ThreatScore;
        public float Distance;
        public float BearingDegrees;
    }

    /// <summary>Per-observer snapshot of currently detected targets. Best-effort, volatile.</summary>
    [DdsTopic("SensorTargets")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile)]
    public partial struct SensorTargets
    {
        [DdsKey] public long ObserverEntityId;
        public uint          Tick;
        [DdsManaged] public List<DdsTrackedTarget> Targets;
    }

    /// <summary>
    /// Discrete state-change event published by the Perception Solver when a new target
    /// enters or leaves an observer's sensor coverage after debounce filtering.
    /// Uses Reliable / TransientLocal QoS so no events are dropped even under transient
    /// congestion and new Brain nodes receive the last state for each observer/target pair.
    /// </summary>
    [DdsTopic("SensorTrackState")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct SensorTrackState
    {
        /// <summary>ECS/network ID of the observer entity that holds the sensor.</summary>
        [DdsKey] public long ObserverEntityId;
        /// <summary>ECS/network ID of the detected (or lost) target entity.</summary>
        [DdsKey] public long TargetEntityId;
        /// <summary>
        /// <c>1</c> = contact Acquired (target entered sensor coverage);
        /// <c>0</c> = contact Lost (target left coverage after hysteresis hold-off).
        /// </summary>
        public byte State;
        /// <summary>Last known X position (metres, ground plane) at the time of the state change.</summary>
        public float PositionX;
        /// <summary>Last known Y position (metres, ground plane) at the time of the state change.</summary>
        public float PositionY;
        /// <summary>Simulation tick when the state change was detected.</summary>
        public uint Tick;
        /// <summary>⭐ <c>CE-3060</c> — the <c>SensorModality</c> kinds holding the target (OR); 0 = an older writer (Visual).</summary>
        public byte Modality;
    }

    // ── Pathfinding pipeline (MOD1-P6T2) ───────────────────────────────────────────

    /// <summary>A single path request submitted by a Brain node.</summary>
    [DdsStruct]
    public partial struct DdsPathRequest
    {
        public long           RequestId;
        public RelativeVector3 Start;
        public RelativeVector3 End;
        /// <summary>0=Wheeled, 1=Tracked, 2=Infantry.</summary>
        public byte           MobilityProfile;
        /// <summary>⭐ CE-3129 — the request's forced backend (<c>NavigationBackend</c>; 0 = Auto).</summary>
        public byte           BackendForce;
        /// <summary>⭐ CE-3129 — the request's navmesh layer mask (<c>NavLayerMask</c> bits; 0 = all layers).</summary>
        public int            NavLayerMask;
        /// <summary>⭐ CE-3129 — the actor's road use (<c>RoadUse</c>; 0 = Unspecified), R-230.</summary>
        public byte           RoadUse;
    }

    /// <summary>Batched path requests published by a Brain node toward the Navigation Solver.</summary>
    [DdsTopic("PathRequestBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct PathRequestBatch
    {
        [DdsKey] public int          SourceNodeId;
        public GeoPoint            BatchOrigin;
        [DdsManaged] public List<DdsPathRequest> Requests;
    }

    /// <summary>Computed path result for one request.</summary>
    [DdsStruct]
    public partial struct DdsPathResult
    {
        public long   RequestId;
        public bool   IsReachable;
        public float  TotalDistanceMeters;
        public int    RouteHandle;
        [DdsManaged] public List<RelativeVector3> CoarseWaypoints;
    }

    /// <summary>Batched path results returned by the Navigation Solver to the requesting node.</summary>
    [DdsTopic("PathResponseBatch")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct PathResponseBatch
    {
        [DdsKey] public int          TargetNodeId;
        /// <summary>High-precision geographic anchor for coordinate reconstruction on the Brain node.</summary>
        public GeoPoint              BatchOrigin;
        [DdsManaged] public List<DdsPathResult> Results;
    }

    // ── Ground Clamping IG contract (MOD1-P7T1) ──────────────────────────────

    /// <summary>
    /// Wire-format enumeration controlling per-entity terrain clamping on IG nodes.
    /// Mirrors the engine-side <c>Fdp.Modules.Geographic.EClampingMode</c> enum;
    /// kept separate per the Dual-Enum Pattern (MOD1-DESIGN §2.5) so the DDS layer
    /// never takes a compile dependency on the FDP geographic toolkit.
    /// </summary>
    public enum EClampingMode : byte
    {
        /// <summary>Engine decides: grounded vehicle = clamped, airborne = unclamped.</summary>
        CLAMP_DEFAULT   = 0,
        /// <summary>Explicitly clamped — e.g. taxiing aircraft, editor drag-and-drop on terrain.</summary>
        CLAMP_FORCE_ON  = 1,
        /// <summary>Explicitly unclamped — e.g. in-flight, editor aerial drag.</summary>
        CLAMP_FORCE_OFF = 2,
    }

    /// <summary>
    /// Dynamic per-entity clamping override published by SimHost flight-dynamics
    /// and the ExCon editor.  <c>TransientLocal</c> durability guarantees late-joining
    /// IG nodes immediately receive the current state without a republish.
    /// </summary>
    [DdsTopic("GroundClampingOverride")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GroundClampingOverride
    {
        /// <summary>Network entity ID matching <see cref="WorldPos.EntityId"/>.</summary>
        [DdsKey] public int EntityId;

        /// <summary>Desired clamping mode for this entity.</summary>
        public EClampingMode Mode;
    }

    // ── Perception CQRS messages ────────────────────────────────────────────────

    /// <summary>
    /// DDS wire message carrying one thing a unit HEARD, from the node that solves its acoustic sensor to the Brain.
    /// ⭐ <c>CE-3062</c> — ANONYMOUS by design (R-205, "shot from the north"): an estimated position and an uncertainty radius,
    /// never the source's identity (the old <c>SourceEntityIndex</c> is gone). docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.1.
    /// </summary>
    // ⭐ CE-2106 — depth 1 on an UNKEYED topic is ONE instance: an acoustic solve writes one sample per heard sound for every
    //   listener in the same frame, and each overwrote the one before (measured: the rifleman's shot never reached the Brain,
    //   only the last listener's sample did). Same collapse as CE-3023's SysOpStatus. ⚠ BOUNDED, not KeepAll: Volatile and
    //   BestEffort, so the depth only has to cover one solve's burst.
    [DdsTopic("AudioTargetDetected")]
    [DdsIdlFile("hrot-sim-msg")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 64)]
    public partial struct AudioTargetDetected
    {
        public long  ListenerEntityId;
        public float OriginX;
        public float OriginY;
        public float OriginZ;
        /// <summary>Uncertainty radius of the estimate (metres).</summary>
        public float Radius;
        /// <summary>What was heard (<c>SoundKind</c>: 1 movement, 2 shot, 3 detonation).</summary>
        public byte  Kind;
        /// <summary>⭐ CE-3063 (R-207) — what it sounded like (<c>SoundSourceClass</c>), never the source's identity.</summary>
        public byte  SourceClass;
    }

}
