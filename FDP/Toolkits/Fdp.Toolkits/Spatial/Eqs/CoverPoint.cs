using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Represents a single static cover node in the environment.
    /// Strictly unmanaged (28 bytes) so the generator can use stackalloc.
    ///
    /// <para>Since the 3D Cognitive Spatial Awareness promotion (P3D-204) the node carries a
    /// world-space altitude (<see cref="PositionZ"/>, Sim Z-up) so cover under a bridge and on
    /// the deck above can be disambiguated.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CoverPoint
    {
        // World-space ground-plane coordinates.
        public float PositionX;
        public float PositionY;

        // World-space altitude (Sim Z-up). 3D Cognitive Spatial Awareness promotion (P3D-204).
        public float PositionZ;

        // Normalized direction this cover faces (direction of protection).
        public float DirectionX;
        public float DirectionY;

        // Pre-annotated quality multiplier (1.0 = concrete, 0.5 = wood).
        public float Quality;

        // 0 = Prone, 1 = Crouch, 2 = Stand.
        public byte StanceHeight;

        // Explicit padding to reach 28 bytes and maintain 4-byte alignment
        // (6 floats = 24 + 1-byte stance + 3 bytes padding).
        private byte _pad0;
        private ushort _pad1;
    }
}
