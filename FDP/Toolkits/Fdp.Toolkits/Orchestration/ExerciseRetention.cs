using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ CE-3218 — keeps a scratch staging tree from filling the disk: at start-up, every <c>exercises</c> directory under the
    /// staging root (the shared one and each node's) keeps only its NEWEST exercises.
    /// <para>📌 Measured by the behaviors lane, 2026-10-16: every <c>--mode all</c> run leaves an exercise (per-node <c>.fdp</c> files
    /// and the archived copy); 318 of them (~18 GB) used up a cloud session's disk and killed a demo sweep (ENOSPC).</para>
    /// <para>⛔ Recordings are user data — a deployment keeps them until an operator deletes them
    /// (<c>docs/designs/file-sync-1/Detailed Design.md</c>: "Operator-triggered deletion only"). So pruning is ON by default
    /// only for a SCRATCH root: the platform temp directory, with no <c>FDP_STAGING_ROOT</c> set. Anywhere else it runs only when
    /// asked (<c>--keep-exercises N</c> or <c>FDP_KEEP_EXERCISES=N</c>).</para>
    /// </summary>
    public static class ExerciseRetention
    {
        /// <summary>How many exercises a scratch staging root keeps by default.</summary>
        public const int ScratchDefaultKeep = 20;

        /// <summary>The environment variable that sets how many to keep (any root). ≤ 0 keeps everything.</summary>
        public const string KeepVariable = "FDP_KEEP_EXERCISES";

        /// <summary>
        /// How many exercises to keep under <paramref name="stagingRoot"/>, or <see langword="null"/> to keep all:
        /// the command line first, then <see cref="KeepVariable"/>, then <see cref="ScratchDefaultKeep"/> for a scratch root.
        /// A value ≤ 0 means keep all.
        /// </summary>
        public static int? ResolveKeep(string stagingRoot, int? commandLine,
                                       Func<string, string?>? environment = null, string? tempPath = null)
        {
            environment ??= Environment.GetEnvironmentVariable;
            int? keep = commandLine;
            if (keep is null && int.TryParse(environment(KeepVariable), out var fromEnv)) keep = fromEnv;
            if (keep is null && IsScratchRoot(stagingRoot, environment, tempPath ?? Path.GetTempPath())) keep = ScratchDefaultKeep;
            return keep is > 0 ? keep : null;
        }

        /// <summary>A root the user did not choose: the platform temp directory, with no <c>FDP_STAGING_ROOT</c>.</summary>
        public static bool IsScratchRoot(string stagingRoot, Func<string, string?> environment, string tempPath)
        {
            if (!string.IsNullOrEmpty(environment("FDP_STAGING_ROOT"))) return false;
            var root = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar);
            var temp = Path.GetFullPath(tempPath).TrimEnd(Path.DirectorySeparatorChar);
            return root.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        /// <summary>
        /// Deletes all but the <paramref name="keepNewest"/> most recently written exercise directories in
        /// <c>{root}/exercises</c> and every <c>{root}/nodes/*/exercises</c>. Returns how many were deleted. A directory that
        /// cannot be deleted (in use) is skipped.
        /// </summary>
        public static int Prune(string stagingRoot, int keepNewest, Action<string>? log = null)
        {
            if (keepNewest <= 0 || !Directory.Exists(stagingRoot)) return 0;
            int removed = 0;
            foreach (var exercises in ExerciseDirectories(stagingRoot))
            {
                var stale = new DirectoryInfo(exercises).GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .Skip(keepNewest);
                foreach (var dir in stale)
                {
                    try { dir.Delete(recursive: true); removed++; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            if (removed > 0)
                log?.Invoke($"[Retention] removed {removed} old exercise recording(s) under {stagingRoot}, keeping the newest {keepNewest} per directory");
            return removed;
        }

        /// <summary>
        /// ⭐ CE-4090 (AQ108 D15) — deletes ONE exercise (<paramref name="exerciseId"/>) from <c>{root}/exercises</c> and every
        /// <c>{root}/nodes/*/exercises</c>: the same layout and the same skip-what-is-in-use rule as <see cref="Prune"/>, which is the only
        /// other deleter of recordings. Returns how many directories were removed (0 = it was not there).
        /// </summary>
        public static int Delete(string stagingRoot, Guid exerciseId, Action<string>? log = null)
        {
            if (exerciseId == Guid.Empty || !Directory.Exists(stagingRoot)) return 0;
            int removed = 0;
            foreach (var exercises in ExerciseDirectories(stagingRoot))
            {
                var dir = new DirectoryInfo(Path.Combine(exercises, exerciseId.ToString()));
                if (!dir.Exists) continue;
                try { dir.Delete(recursive: true); removed++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (removed > 0) log?.Invoke($"[Retention] discarded exercise {exerciseId}: {removed} directory(ies) under {stagingRoot}");
            return removed;
        }

        private static IEnumerable<string> ExerciseDirectories(string stagingRoot)
        {
            var shared = Path.Combine(stagingRoot, OrchestrationConstants.ExercisesDirectoryName);
            if (Directory.Exists(shared)) yield return shared;
            var nodes = Path.Combine(stagingRoot, "nodes");
            if (!Directory.Exists(nodes)) yield break;
            foreach (var node in Directory.GetDirectories(nodes))
            {
                var dir = Path.Combine(node, OrchestrationConstants.ExercisesDirectoryName);
                if (Directory.Exists(dir)) yield return dir;
            }
        }
    }
}
