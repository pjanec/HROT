using System;
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

        /// <summary>⭐ <c>CE-3318</c> — the last handle allocated (what the world mirrors in <see cref="NavigationHandleMark"/>).</summary>
        public static int Last => Volatile.Read(ref _counter);

        /// <summary>
        /// ⭐ <c>CE-3318</c> (restore design §1m) — a CHECKPOINT was put back whose world had allocated up to <paramref name="last"/>: the next
        /// handle is <paramref name="last"/> + 1, exactly as the straight run's (a fresh process starts this counter over; a restore in this
        /// process may LOWER it — a handle of the abandoned timeline is reused, and the trajectory pool's keyed entry is simply replaced).
        /// </summary>
        public static void ResumeAt(int last) => Volatile.Write(ref _counter, Math.Max(last, MuscleHandleBase - 1));

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

    /// <summary>
    /// ⭐ <c>CE-3318</c> (restore design §1m) — the world's copy of <see cref="NavigationHandleAllocator.Last"/>, mirrored every frame it moves
    /// (<c>TrajectoryRestoreSystem</c>): a checkpoint carries it, so a restore numbers its next path where the straight run did. The counter
    /// itself stays a process static (the pathfinding solver allocates on a background thread); a checkpoint is written on a step with no
    /// such run in flight (CE-3316), so the copy is exact there. <see cref="At"/>: a dry run puts no singleton back — a copy newer than the
    /// restored clock is the abandoned future's and is not used.
    /// </summary>
    [Fdp.Core.ComponentId(Fdp.Core.GlobalComponentIds.NavigationHandleMark)]
    [Fdp.Core.DataPolicy(Fdp.Core.DataPolicy.NoScenario)]
    public struct NavigationHandleMark
    {
        public int Last;
        public double At;
    }
}
