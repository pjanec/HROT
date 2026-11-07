using System;
using System.Collections.Generic;
using System.IO;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkits.Tests.Orchestration
{
    /// <summary>⭐ CE-3218 — a scratch staging tree keeps only its newest exercise recordings; a deployment's tree is left alone.</summary>
    public class ExerciseRetentionTests : IDisposable
    {
        private readonly string _temp = Path.Combine(Path.GetTempPath(), "ce3218-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
        }

        private static Func<string, string?> Env(Dictionary<string, string> vars) => k => vars.TryGetValue(k, out var v) ? v : null;

        private string Exercise(string root, string exercisesDir, int ageMinutes)
        {
            var dir = Path.Combine(root, exercisesDir, Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "node_1.fdp"), "x");
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddMinutes(-ageMinutes));
            return dir;
        }

        [Fact]
        public void CE3218_Prune_KeepsTheNewestN_InTheSharedAndEveryNodeDirectory()
        {
            var shared = new List<string>();
            var node = new List<string>();
            for (int i = 0; i < 5; i++) shared.Add(Exercise(_temp, "exercises", ageMinutes: i));
            for (int i = 0; i < 4; i++) node.Add(Exercise(_temp, Path.Combine("nodes", "node-400", "exercises"), ageMinutes: i));

            int removed = ExerciseRetention.Prune(_temp, keepNewest: 2);

            Assert.Equal(3 + 2, removed);
            Assert.True(Directory.Exists(shared[0]) && Directory.Exists(shared[1]));      // the two newest stay
            Assert.False(Directory.Exists(shared[2]) || Directory.Exists(shared[3]) || Directory.Exists(shared[4]));
            Assert.True(Directory.Exists(node[0]) && Directory.Exists(node[1]));
            Assert.False(Directory.Exists(node[2]) || Directory.Exists(node[3]));
        }

        /// <summary>⭐ CE-4090 (AQ108 D15) — discarding ONE recording is the retention code's own delete: that exercise's directory goes from the shared
        /// <c>exercises</c> and from every node's; every other exercise stays; an id nothing holds deletes 0.</summary>
        [Fact]
        public void CE4090_Delete_RemovesOneExercise_FromTheSharedAndEveryNodeDirectory_AndNothingElse()
        {
            var id = Guid.NewGuid();
            var doomed = new[]
            {
                Path.Combine(_temp, "exercises", id.ToString()),
                Path.Combine(_temp, "nodes", "node-400", "exercises", id.ToString()),
                Path.Combine(_temp, "nodes", "node-401", "exercises", id.ToString()),
            };
            foreach (var d in doomed) { Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "node.fdp"), "x"); }
            var other = Exercise(_temp, "exercises", ageMinutes: 1);
            var otherOnNode = Exercise(_temp, Path.Combine("nodes", "node-400", "exercises"), ageMinutes: 1);

            Assert.Equal(3, ExerciseRetention.Delete(_temp, id));

            Assert.All(doomed, d => Assert.False(Directory.Exists(d)));
            Assert.True(Directory.Exists(other) && Directory.Exists(otherOnNode));
            Assert.Equal(0, ExerciseRetention.Delete(_temp, id));                // gone: nothing left to delete
            Assert.Equal(0, ExerciseRetention.Delete(_temp, Guid.NewGuid()));    // a stranger
            Assert.Equal(0, ExerciseRetention.Delete(_temp, Guid.Empty));        // never the whole folder
            Assert.True(Directory.Exists(other));
        }

        [Fact]
        public void CE3218_Prune_WithZero_KeepsEverything()
        {
            for (int i = 0; i < 3; i++) Exercise(_temp, "exercises", ageMinutes: i);
            Assert.Equal(0, ExerciseRetention.Prune(_temp, keepNewest: 0));
            Assert.Equal(3, Directory.GetDirectories(Path.Combine(_temp, "exercises")).Length);
        }

        [Fact]
        public void CE3218_ResolveKeep_AScratchRoot_DefaultsToTwenty_ADeploymentRoot_KeepsAll()
        {
            var none = Env(new());
            var tempPath = Path.GetTempPath();

            // the platform temp dir, no FDP_STAGING_ROOT: a scratch tree
            Assert.Equal(ExerciseRetention.ScratchDefaultKeep,
                ExerciseRetention.ResolveKeep(Path.Combine(tempPath, "FDP_Temp"), null, none, tempPath));

            // a root the user chose (FDP_STAGING_ROOT) or a path outside temp: keep everything unless asked
            Assert.Null(ExerciseRetention.ResolveKeep(Path.Combine(tempPath, "FDP_Temp"), null,
                Env(new() { ["FDP_STAGING_ROOT"] = "/nas" }), tempPath));
            Assert.Null(ExerciseRetention.ResolveKeep("/srv/fdp", null, none, tempPath));

            // asked: the command line wins, then the variable; <= 0 keeps all
            Assert.Equal(5, ExerciseRetention.ResolveKeep("/srv/fdp", 5, Env(new() { [ExerciseRetention.KeepVariable] = "7" }), tempPath));
            Assert.Equal(7, ExerciseRetention.ResolveKeep("/srv/fdp", null, Env(new() { [ExerciseRetention.KeepVariable] = "7" }), tempPath));
            Assert.Null(ExerciseRetention.ResolveKeep(Path.Combine(tempPath, "FDP_Temp"), 0, none, tempPath));
        }

        /// <summary>⭐⭐ <c>CE-4324</c> (lifecycle LC-21) — recordings default to a FIXED data folder beside the scenarios, not TEMP: with no
        /// <c>FDP_STAGING_ROOT</c> a dev build's root is <c>&lt;repo&gt;/data</c> (git-ignored), never under the temp folder; that folder is still
        /// pruned like a scratch root (dev runs are test output — CE-3218's 18 GB); an operator's chosen root keeps everything.
        /// ✅ Red-proof: the old temp default ⇒ the first asserts red.</summary>
        [Fact]
        public void CE4324_TheDefaultRoot_IsTheDataFolderBesideTheScenarios_NotTemp_AndStillPrunedInADevTree()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FDP_STAGING_ROOT"))) return;   // a run that chose its root
            var tree = ContentLayout.TryFindSourceTree();
            Assert.NotNull(tree);
            string root = OrchestrationConstants.ResolveStagingRoot();
            Assert.Equal(Path.Combine(tree!, OrchestrationConstants.DataDirectoryName), root);
            Assert.True(Directory.Exists(ContentLayout.Scenarios(tree!)));                                   // beside the scenarios
            Assert.False(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal));
            Assert.Equal(ExerciseRetention.ScratchDefaultKeep, ExerciseRetention.ResolveKeep(root, null, Env(new()), Path.GetTempPath()));
            Assert.Null(ExerciseRetention.ResolveKeep(root, null, Env(new() { ["FDP_STAGING_ROOT"] = root }), Path.GetTempPath()));
        }
    }
}
