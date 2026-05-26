using System;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// Encodes a <see cref="long"/> <c>NetworkIdentity.Value</c> as a deterministic
    /// <see cref="Guid"/> suitable for use as a JSON entity key in a merged-view DOM.
    /// Encoding: the 8 bytes of the long are written into the first 8 bytes of the Guid's
    /// raw in-memory layout (little-endian on x86); the remaining 8 bytes are zero.
    /// Round-trips perfectly: <c>ToLong(From(v)) == v</c>.
    /// </summary>
    public static class NetworkIdGuid
    {
        /// <summary>
        /// Packs <paramref name="value"/> into the first 8 bytes of a <see cref="Guid"/>.
        /// The resulting Guid is always parseable by <see cref="Guid.TryParse"/>.
        /// </summary>
        public static Guid From(long value)
        {
            Guid g = default;
            Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref g, 1));
            MemoryMarshal.Write(bytes, in value);
            return g;
        }

        /// <summary>
        /// Extracts the <see cref="long"/> packed into the first 8 bytes of <paramref name="g"/>
        /// by <see cref="From"/>.
        /// </summary>
        public static long ToLong(Guid g)
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref g, 1));
            return MemoryMarshal.Read<long>(bytes);
        }
    }
}
