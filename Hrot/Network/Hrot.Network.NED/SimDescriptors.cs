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
