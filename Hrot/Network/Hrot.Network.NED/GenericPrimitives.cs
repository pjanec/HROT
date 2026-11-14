using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    // ===================================================================================
    // ATTR2 BINARY CONTRACT: GEOMETRIC PRIMITIVE IDL TYPES
    // ===================================================================================
    // Shared vector primitives used by AttributeValueUnion and anywhere a compact
    // 3- or 4-component floating-point tuple is required on the DDS wire.
    // Extracted from GenericMessages.cs per ATTR2-DEBT-02.
    // ⭐ AQ107 B/D: these three live in the LEAF IDL file `hrot-common` (not `hrot-generic-msgs`): the position descriptors
    //   (`hrot-sim-desc`, `hrot-map-desc`) now use Vec3d/Vec3f, and `hrot-generic-msgs` already depends on them through
    //   `hrot-all-desc` — keeping the vectors there made the IDL include graph circular (the code generator refuses it).
    //   The IDL TYPE name (Hrot::NED::Messages::Vec3d) is by namespace, so the wire identity does not change.
    // ===================================================================================

    /// <summary>3-component single-precision vector (x, y, z). Value type — zero allocation.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[{X:0.000:Number}, {Y:0.000:Number}, {Z:0.000:Number}]")]
    public partial struct Vec3f
    {
        /// <summary>X component.</summary>
        public float X;
        /// <summary>Y component.</summary>
        public float Y;
        /// <summary>Z component.</summary>
        public float Z;
    }

    /// <summary>
    /// An orientation on a position wire: a unit quaternion's VECTOR part <c>(x, y, z)</c>, local ENU axes (X east, Y north, Z up).
    /// <c>w</c> is implied: <c>w = +sqrt(max(0, 1 - |v|^2))</c>; the sender canonicalises <c>w &gt;= 0</c> (negates q when w &lt; 0), so an
    /// ALL-ZERO struct is the identity rotation, never NaN. Same layout and bytes as <see cref="Vec3f"/>; it is a type of its own only so a
    /// reader of <c>ddsmonitor</c> sees <c>HPR [045.0, 3.2, -1.0] deg, Quat [0.010, 0.030, 0.380, 0.924]</c> instead of an opaque
    /// [x, y, z] (the display is HAND-WRITTEN below — <c>[DdsTypeFormat]</c> can only interpolate stored members, and the code generator
    /// treats every property as a wire field). JSON capture still shows the raw <c>{"X","Y","Z"}</c>.
    /// 📐 ONE codec: <c>Fdp.Toolkit.Replication.Components.QuaternionWireCodec</c>; ONE display: <c>OrientationDisplay</c>.
    /// 📄 <c>docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md</c> D.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    public partial struct OrientationQuat
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

    /// <summary>3-component double-precision vector (x, y, z). Value type — zero allocation.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[{X:0.000:Number}, {Y:0.000:Number}, {Z:0.000:Number}]")]
	public partial struct Vec3d
    {
        /// <summary>X component.</summary>
        public double X;
        /// <summary>Y component.</summary>
        public double Y;
        /// <summary>Z component.</summary>
        public double Z;
    }

    /// <summary>4-component single-precision vector (x, y, z, w). Value type — zero allocation.</summary>
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[{X:0.000:Number}, {Y:0.000:Number}, {Z:0.000:Number}, {W:0.000:Number}]")]
    public partial struct Vec4f
    {
        /// <summary>X component.</summary>
        public float X;
        /// <summary>Y component.</summary>
        public float Y;
        /// <summary>Z component.</summary>
        public float Z;
        /// <summary>W component.</summary>
        public float W;
    }
}
