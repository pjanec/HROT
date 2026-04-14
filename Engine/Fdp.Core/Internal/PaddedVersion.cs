using System.Runtime.InteropServices;

namespace Fdp.Kernel.Internal
{
    /// <summary>
    /// Padded version number to prevent false sharing in multi-threaded scenarios.
    /// When multiple threads update chunk versions in adjacent array elements,
    /// they can invalidate each other's CPU cache lines, causing severe performance degradation.
    /// By padding to 64 bytes (typical cache line size), each version gets its own cache line.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct PaddedVersion
    {
        /// <summary>
        /// The actual version number.
        /// </summary>
        [FieldOffset(0)]
        public uint Value;
        
        // Remaining 60 bytes are padding to force this struct to occupy
        // an entire 64-byte cache line, preventing false sharing when
        // multiple threads write to adjacent array elements.
    }
}
