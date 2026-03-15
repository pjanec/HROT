using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Bagira.DDS.DM;

namespace Bagira.BDC.SSTD
{

    // Entity position/orientation NOT including any dead reckoning information.
    [DdsTopic("GeoSpatial")]
    [DdsIdlFile("bdc-sst-sim-desc")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GeoSpatial
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public int EntityId;

        public DateTime Time;  // Exercise time stamp the following values are valid for (same as CGFX time, same as windows FILETIME). 0 is reserved for unspecified time.
        public GeoPosition Pos;  // Latitude [deg], longitude [deg], altitude [m] above WGS84 ellipsoid
        public OrientationHPR Rot;  // Heading [deg], pitch [deg], roll [deg]
    }

    // Entity velocity/acc allowing for the dead reckoning and smoothing.
    // Less frequent updates (compared to GeoSpatial) are expected since velocity doesn't change as often as position.
    // To be used in place of GeoSpatial if dead reckoning is desired.
    [DdsTopic("GeoSpatialDR")]
    [DdsIdlFile("bdc-sst-sim-desc")]
    [DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct GeoSpatialDR
    {
        // Primary Key: Which entity is being modified?
        [DdsKey]
        public int EntityId;

        public DateTime Time; // Exercise time stamp the following values are valid for (same as CGFX time, same as windows FILETIME). 0 is reserved for unspecified time.
        public DAL3 Vel;  // Velocity vector Azim=heading in degrees, Elev=pitch in degrees, Length=speed in m/s
        public DAL3 Acc; // Acceleration vector [m/s^2]; same coordinate system as Vel; full vector used to support turning entities
        public OrientationHPR RotVel; // Angular Velocity; Heading [deg/s], pitch [deg/s], roll [deg/s]
    }



    // Overall damage level of the whole entity
    [DdsTopic("EntityDamage")]
    [DdsIdlFile("bdc-sst-sim-desc")]
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
    /// Translators in the Bagira layer map between the two representations.
    /// </summary>
    public enum ENavigationMode : byte
    {
        NAV_NONE            = 0,
        NAV_DIRECT_POINT    = 1,
        NAV_FOLLOW_ROUTE    = 2,
        NAV_JOIN_FORMATION  = 3,
    }

    /// <summary>
    /// DDS wire enum mirroring <c>FDP.Toolkit.Navigation.NavigationResult</c>.
    /// Translators in the Bagira layer map between the two representations.
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
    /// <c>FinalDestination</c> is encoded as <see cref="GeoPosition"/> (WGS-84);
    /// a translator converts from the ECS <c>Vector2</c> Cartesian representation.
    /// </summary>
    [DdsTopic("NavigationIntent")]
    [DdsIdlFile("bdc-sst-sim-desc")]
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
        public GeoPosition FinalDestination;

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
    [DdsIdlFile("bdc-sst-sim-desc")]
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
    }

}
