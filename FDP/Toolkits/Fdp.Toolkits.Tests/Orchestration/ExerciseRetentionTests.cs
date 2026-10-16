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
    }
}
