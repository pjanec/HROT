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

	float Damage; // total damage level of the whole entity 0=healthy, 100 = fully destroyed/dead

    }

}
