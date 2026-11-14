using System;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4612</c> (R-2090; 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §15) — an ORDER's identity is its request id (CE-4306 F5's, one id,
    /// never a second). A station mints its own (<c>Guid.NewGuid</c>, as today); a COMMANDER mints one here from what is already deterministic —
    /// itself, its run, the member, the sim time — so the same world gives the same ids at any thread count (no shared counter, no shared RNG).
    /// </summary>
    public static class RequestIds
    {
        /// <summary>The request id an issuer gives its order to <paramref name="member"/> at <paramref name="now"/> (sim s) from its run <paramref name="run"/>.</summary>
        public static Guid For(Entity issuer, uint run, Entity member, double now)
            => Of((ulong)issuer.PackedValue, run, (ulong)member.PackedValue, (ulong)BitConverter.DoubleToInt64Bits(now));

        /// <summary>The request id of an order that arrived without one (a mission phase, the debug API's direct assign), minted at its start.</summary>
        public static Guid ForStart(Entity entity, uint instance) => Of((ulong)entity.PackedValue, instance, 0x9E3779B97F4A7C15UL, 1UL);

        private static Guid Of(ulong a, ulong b, ulong c, ulong d)
        {
            ulong h1 = Mix(a, b, c, d, 1469598103934665603UL), h2 = Mix(d, c, b, a, 0x84222325CBF29CE4UL);
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, h1);
            BitConverter.TryWriteBytes(bytes.Slice(8), h2 == 0 && h1 == 0 ? 1UL : h2);
            return new Guid(bytes);
        }

        private static ulong Mix(ulong a, ulong b, ulong c, ulong d, ulong h)
        {
            h = (h ^ a) * 1099511628211UL; h = (h ^ b) * 1099511628211UL; h = (h ^ c) * 1099511628211UL; h = (h ^ d) * 1099511628211UL;
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
            return h;
        }
    }
}
