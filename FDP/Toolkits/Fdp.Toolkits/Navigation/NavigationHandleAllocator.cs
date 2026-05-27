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
    }
}
