using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// Coarse classification of a detected danger-area feature.
    /// Extensible: new kinds may be added without breaking existing descriptors.
    /// </summary>
    public enum DangerAreaKind : byte
    {
        Unknown       = 0,
        OpenGround    = 1,
        StreetCrossing = 2,
        Intersection  = 3,
        ChokePoint    = 4,
        CrestLine     = 5
    }

    /// <summary>
    /// Lean 3D-native descriptor for a single danger area detected along the squad's route.
    /// Produced by the danger-area sensor; consumed by the squad HSM to select maneuver options.
    /// </summary>
    /// <remarks>
    /// Layout (sequential, all fields 4-byte aligned):
    /// <list type="bullet">
    ///   <item>FeatureId   (uint,   4 B) @ offset  0</item>
    ///   <item>ThreatRating(float,  4 B) @ offset  4</item>
    ///   <item>Kind        (byte,   1 B) @ offset  8</item>
    ///   <item>_pad0       (byte,   1 B) @ offset  9</item>
    ///   <item>_pad1       (ushort, 2 B) @ offset 10</item>
    ///   <item>Center      (Vector3,12 B)@ offset 12</item>
    ///   <item>ExtentsXY   (Vector2, 8 B)@ offset 24</item>
    ///   <item>AngleRad    (float,  4 B) @ offset 32</item>
    ///   <item>ZFloor      (float,  4 B) @ offset 36</item>
    ///   <item>ZCeiling    (float,  4 B) @ offset 40</item>
    ///   <item>NearSideHandle(Vector3,12 B)@offset 44</item>
    ///   <item>FarSideHandle (Vector3,12 B)@offset 56</item>
    ///   <item>Total = 68 B (pinned by <see cref="PinnedSize"/>)</item>
    /// </list>
    /// FeatureId is FNV-1a-32 of the core navmesh polygon id — stable across solver passes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct DangerAreaDescriptor
    {
        /// <summary>
        /// FNV-1a-32 hash of the core navmesh polygon id.
        /// Stable across solver passes; used by the squad HSM to re-match existing
        /// feature assignments when the sensor buffer refreshes.
        /// </summary>
        public uint FeatureId;

        /// <summary>Threat level in [0, 1]. Sets caution and role-bias weight.</summary>
        public float ThreatRating;

        /// <summary>Coarse feature classification.</summary>
        public DangerAreaKind Kind;
        private byte _pad0;
        private ushort _pad1;

        /// <summary>OBB footprint centre in world-space 3D.</summary>
        public Vector3 Center;

        /// <summary>Half-extents of the OBB footprint in the XY plane (half-width, half-length).</summary>
        public Vector2 ExtentsXY;

        /// <summary>OBB yaw orientation in radians.</summary>
        public float AngleRad;

        /// <summary>Z-coordinate of the bottom of the height band.</summary>
        public float ZFloor;

        /// <summary>Z-coordinate of the top of the height band.</summary>
        public float ZCeiling;

        /// <summary>
        /// 3D world-space handle where the crossing element forms up before traversing.
        /// Height is tactically decisive (bridge deck vs. street below = distinct areas).
        /// </summary>
        public Vector3 NearSideHandle;

        /// <summary>
        /// 3D world-space handle where the first-across element sets up to provide cover.
        /// </summary>
        public Vector3 FarSideHandle;

        /// <summary>
        /// Pinned expected size in bytes.  Verified by <c>DangerAreaProviderTests.DangerAreaDescriptor_PinnedSize_MatchesActual</c>.
        /// </summary>
        public const int PinnedSize = 68;
    }
}
