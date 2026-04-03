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



    // Overall damage level of the whole entity
    [DdsTopic("EntityDamage")]
    [DdsIdlFile("hrot-sim-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityDamage
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public int EntityId;

	    public float Damage; // total damage level of the whole entity 0=healthy, 100 = fully destroyed/dead

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
    }

    /// <summary>
    /// DDS wire enum mirroring <c>FDP.Toolkit.Navigation.NavigationResult</c>.
    /// Translators in the Hrot layer map between the two representations.
    /// </summary>
    public enum ENavigationResult : byte
    {
        RES_IN_PROGRESS      = 0,
        RES_ARRIVED          = 1,
        RES_FAILED_BLOCKED   = 2,
        RES_FAILED_UNREACHABLE = 3,
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

}
