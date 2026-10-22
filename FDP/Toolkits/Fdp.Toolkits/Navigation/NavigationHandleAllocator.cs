using System.Threading;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Thread-safe allocator for Muscle-private route handles (section 6.3).
    /// All allocated handles are &gt;= <see cref="MuscleHandleBase"/>, ensuring they
    /// do not overlap Brain-allocated handles which occupy the lower range.
    /// </summary>
    public static class NavigationHandleAllocator
    {
        /// <summary>First Muscle-private handle value (0x40000000).</summary>
        public const int MuscleHandleBase = 0x40000000;

        private static int _counter = MuscleHandleBase - 1;

        /// <summary>
        /// Allocates and returns a monotone-increasing Muscle-private handle.
        /// Safe to call from multiple threads simultaneously.
        /// </summary>
        public static int Allocate() => Interlocked.Increment(ref _counter);

        /// <summary>
        /// ⭐ <c>R-3039</c> S2 §7d — the next handle is above <paramref name="handle"/>: after a world was put back (a restore into a fresh
        /// process starts this counter over), no new path may take a handle a restored mover still drives. Muscle handles only.
        /// </summary>
        public static void EnsureAbove(int handle)
        {
            if (handle < MuscleHandleBase) return;
            int seen;
            do { seen = Volatile.Read(ref _counter); if (seen >= handle) return; }
            while (Interlocked.CompareExchange(ref _counter, handle, seen) != seen);
        }
    }
}
