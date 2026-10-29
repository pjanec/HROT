using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder.Metadata;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ CE-4097 (AQ108 D5) — <b>the ONE reader of "where did this exercise branch from"</b> off the recordings' sidecars: every inventory
    /// filler (the gateway's scans, the Replay Browser's groups) goes through it, so the inventory never keeps a second record of the branch.
    /// </summary>
    /// <remarks>An exercise has one recording per node; each carries the same parent, and a branch point that can differ by a frame's
    /// residual between nodes. The EARLIEST non-empty one is taken — the same rule the master's <c>LiveBranchAggregator</c> applies to the live run.</remarks>
    public static class ExerciseBranch
    {
        /// <summary>The parent and branch point (wall ticks) of the exercise whose sidecars are <paramref name="metas"/>; <c>(Empty, 0)</c> when none branched.</summary>
        public static (Guid ParentExerciseId, long BranchWallTicks) Of(IEnumerable<RecordingMetadata?> metas)
        {
            Guid parent = Guid.Empty;
            long ticks = 0;
            foreach (var m in metas)
            {
                if (m is null || m.ParentExerciseId == Guid.Empty) continue;
                if (parent == Guid.Empty || (m.BranchWallTicks > 0 && (ticks == 0 || m.BranchWallTicks < ticks)))
                {
                    parent = m.ParentExerciseId;
                    ticks = m.BranchWallTicks;
                }
            }
            return (parent, ticks);
        }
    }
}
