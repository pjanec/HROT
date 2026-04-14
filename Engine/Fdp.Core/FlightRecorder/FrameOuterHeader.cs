using System.Runtime.InteropServices;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// Binary contract for the outer frame header written to .fdp recording files.
    /// Pack = 1 prevents hidden padding — bytes on disk match field order exactly.
    /// Layout: [CompressedSize:4][UncompressedSize:4][Tick:8][FrameType:1][WallClockTicks:8] = 25 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FrameOuterHeader
    {
        public int CompressedSize;
        public int UncompressedSize;
        public ulong Tick;
        public byte FrameType;
        public long WallClockTicks;

        /// <summary>
        /// Compile-time size of the header in bytes. Replaces all magic numbers (25, 17, etc.).
        /// </summary>
        public static unsafe int Size => sizeof(FrameOuterHeader);
    }
}
