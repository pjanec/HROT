using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Represents a single static cover node in the environment.
    /// Strictly unmanaged (24 bytes) so the generator can use stackalloc.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CoverPoint
    {
        // World-space ground-plane coordinates.
        public float PositionX;
        public float PositionY;

        // Normalized direction this cover faces (direction of protection).
        public float DirectionX;
        public float DirectionY;

        // Pre-annotated quality multiplier (1.0 = concrete, 0.5 = wood).
        public float Quality;

        // 0 = Prone, 1 = Crouch, 2 = Stand.
        public byte StanceHeight;

        // Explicit padding to reach 24 bytes and maintain 4-byte alignment.
        private byte _pad0;
        private ushort _pad1;
    }
}
