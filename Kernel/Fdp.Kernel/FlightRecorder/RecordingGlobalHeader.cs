using System.Runtime.InteropServices;

namespace Fdp.Kernel.FlightRecorder
{
    /// <summary>
    /// Binary contract for the global recording header written at the start of every .fdp file.
    /// Pack = 1 prevents hidden padding. Layout: [Magic:6][FormatVersion:4][Timestamp:8] = 18 bytes.
    /// Using RecordingGlobalHeader.Size instead of the hardcoded value 18 ensures tests and readers
    /// automatically adapt when the global header is extended.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct RecordingGlobalHeader
    {
        public fixed byte Magic[6];
        public uint FormatVersion;
        public long Timestamp;

        /// <summary>Compile-time size of the global header in bytes.</summary>
        public static int Size => sizeof(RecordingGlobalHeader);
    }
}
