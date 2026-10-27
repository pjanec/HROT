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
