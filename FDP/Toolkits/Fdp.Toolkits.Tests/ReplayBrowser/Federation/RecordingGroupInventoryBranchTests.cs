using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// ⭐ CE-4097 (AQ108 D5) — a branched exercise's parent and branch point come off the recordings' sidecars through ONE reader
    /// (<see cref="ExerciseBranch"/>), and the Replay Browser's exercise groups carry them.
    /// </summary>
    public sealed class RecordingGroupInventoryBranchTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fdp_ce4097_rgi_" + Guid.NewGuid().ToString("N"));

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

        private void Record(Guid exercise, int node, Guid parent, long branchTicks)
        {
            var dir = Path.Combine(_root, "nodes", $"node-{node}", "exercises", exercise.ToString());
            Directory.CreateDirectory(dir);
            var fdp = Path.Combine(dir, $"node_{node}.fdp");
            File.WriteAllText(fdp, "fdp");
            File.WriteAllText(fdp + ".meta.json", MetadataSerializer.Serialize(new RecordingMetadata
            {
                ExerciseId = exercise, NodeId = node, ParentExerciseId = parent, BranchWallTicks = branchTicks, Duration = TimeSpan.FromSeconds(10),
            }));
        }

        [Fact]
        public void The_exercise_groups_carry_the_sidecars_parent_and_the_earliest_branch_point()
        {
            Guid parent = Guid.NewGuid(), branched = Guid.NewGuid(), loaded = Guid.NewGuid();
            Record(branched, 1, parent, 900);
            Record(branched, 2, parent, 850);   // the earliest node's point
            Record(loaded, 1, Guid.Empty, 0);

            var groups = RecordingGroupInventory.Scan(_root);

            var b = Assert.Single(groups, g => g.ExerciseId == branched);
            Assert.Equal((parent, 850L), (b.ParentExerciseId, b.BranchWallTicks));
            var l = Assert.Single(groups, g => g.ExerciseId == loaded);
            Assert.Equal((Guid.Empty, 0L), (l.ParentExerciseId, l.BranchWallTicks));
        }

        [Fact]
        public void ExerciseBranch_ignores_recordings_that_did_not_branch_and_reads_nothing_as_not_a_branch()
        {
            Assert.Equal((Guid.Empty, 0L), ExerciseBranch.Of(Array.Empty<RecordingMetadata?>()));
            var parent = Guid.NewGuid();
            var metas = new RecordingMetadata?[]
            {
                null,
                new RecordingMetadata { ParentExerciseId = Guid.Empty, BranchWallTicks = 5 },   // not a branch: its ticks mean nothing
                new RecordingMetadata { ParentExerciseId = parent, BranchWallTicks = 700 },
                new RecordingMetadata { ParentExerciseId = parent, BranchWallTicks = 600 },
            };
            Assert.Equal((parent, 600L), ExerciseBranch.Of(metas));
        }
    }
}
