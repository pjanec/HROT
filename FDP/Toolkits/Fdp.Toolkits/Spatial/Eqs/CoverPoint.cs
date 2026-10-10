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

        /// <summary>⭐ Stage 7a — what the point is for (<see cref="CoverKind"/>): cover, or a window to fire from. A former padding
        /// byte, so the struct stays 28 bytes. 📄 docs/DESIGN_Building_Interiors.md §3l C4.</summary>
        public CoverKind Kind;

        // Explicit padding to reach 28 bytes and maintain 4-byte alignment
        // (6 floats = 24 + 1-byte stance + 1-byte kind + 2 bytes padding).
        private ushort _pad1;
    }

    /// <summary>⭐ Stage 7a — what a <see cref="CoverPoint"/> is for. 📄 docs/DESIGN_Building_Interiors.md §3l C4.</summary>
    public enum CoverKind : byte
    {
        /// <summary>Behind a wall, a low wall or a window's sill — hidden from the side it faces.</summary>
        Cover = 0,
        /// <summary>Inside a window, facing out — a position to fire from, at <see cref="CoverPoint.StanceHeight"/>.</summary>
        WindowFiring = 1,
    }
}
