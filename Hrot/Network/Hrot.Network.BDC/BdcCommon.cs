using CycloneDDS.Schema;

namespace Hrot.BDC.Common
{
    // Unique identifier of a BDC participating node
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcNodeId
    {
        public int AppDomainId;
        public int AppInstanceId;
    }

    // ⭐ AQ107 F — the BDC stack follows the NED wire's rules: every position / velocity is in the LOCAL ENU frame
    //   (X east, Y north, Z up, metres at the terrain origin — the ECS SimTransform frame), orientation is a quaternion.
    //   Replaces BdcGeoPoint (lat/lon), BdcEulerOri (Euler degrees) and BdcAngularVector (azimuth/elevation/length).

    // 3-component double vector: a position in metres, local ENU
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcVec3d
    {
        public double X;
        public double Y;
        public double Z;
    }

    // 3-component float vector: a velocity (m/s) or an angular velocity (rad/s). (An orientation has its own type: BdcOrientationQuat.)
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcVec3f
    {
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>
    /// An orientation on a BDC position wire: a unit quaternion's VECTOR part <c>(x, y, z)</c>, local ENU axes (X east, Y north, Z up).
    /// <c>w</c> is implied: <c>w = +sqrt(max(0, 1 - |v|^2))</c>; the sender canonicalises <c>w &gt;= 0</c>, so an ALL-ZERO struct is the
    /// identity rotation, never NaN. Same layout and bytes as <see cref="BdcVec3f"/> (BDC keeps its own primitives, like
    /// <c>Hrot.NED.Messages.OrientationQuat</c> on the NED stack); a type of its own only so <c>ddsmonitor</c> shows
    /// <c>HPR [045.0, 3.2, -1.0] deg, Quat [0.010, 0.030, 0.380, 0.924]</c> instead of an opaque [x, y, z]. The display is HAND-WRITTEN
    /// (<c>[DdsTypeFormat]</c> can only interpolate stored members, and the code generator treats every property as a wire field).
    /// 📐 ONE codec: <c>QuaternionWireCodec</c>; ONE display: <c>OrientationDisplay</c> (both in Fdp.Toolkits Replication).
    /// 📄 <c>docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md</c> D / F.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcOrientationQuat
    {
        /// <summary>Quaternion x (vector part).</summary>
        public float X;
        /// <summary>Quaternion y (vector part).</summary>
        public float Y;
        /// <summary>Quaternion z (vector part).</summary>
        public float Z;

        /// <summary>Heading/pitch/roll in degrees plus the full quaternion — see <c>OrientationDisplay</c>.</summary>
        public override string ToString() => Fdp.Toolkit.Replication.Components.OrientationDisplay.Format(X, Y, Z);

        /// <summary>The same text as <see cref="ToString"/>, as the coloured tokens ddsmonitor's detail panel looks up by reflection.</summary>
        public System.Collections.Generic.IEnumerable<CycloneDDS.Schema.Formatting.FormattedToken> GetFormatTokens()
            => Fdp.Toolkit.Replication.Components.OrientationDisplay.Tokens(X, Y, Z);
    }
}
