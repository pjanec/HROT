using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>AQ107 C</c> (R-4094) — the cluster master decided the origin of the NED wire's local frame (a load's terrain origin, or a
    /// replay's recording origin): the DDS writer (<c>ClusterOpMasterTranslator</c>) puts it on the retained <c>GeoReference</c> topic.
    /// ⭐ Published ONLY when the origin or the terrain name differs from the last sample, so a re-load of the same terrain writes nothing;
    /// <see cref="Generation"/> is already final (+1 per DIFFERENT origin). ⛔ Nothing in HROT reads it back — every node holds the terrain.
    /// 📄 docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md §4 row C.
    /// </summary>
    [EventId(9073)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct GeoReferencePublishedEvent
    {
        /// <summary>Geodetic latitude of the local frame's origin, degrees (WGS84).</summary>
        public double OriginLatitudeDeg;
        /// <summary>Geodetic longitude of the local frame's origin, degrees (WGS84).</summary>
        public double OriginLongitudeDeg;
        /// <summary>Altitude of the local frame's origin above the WGS84 ellipsoid, metres.</summary>
        public double OriginAltitudeM;
        /// <summary>The terrain the origin belongs to; empty when none.</summary>
        public string TerrainName;
        /// <summary>+1 per different origin; the first publish is 1.</summary>
        public int Generation;
    }
}
