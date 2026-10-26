using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Toolkit.ReplayBrowser.Federation
{
    /// <summary>
    /// One exercise as the Replay Browser can open it: one recording file per node, every one carrying the same exercise id in its
    /// sidecar — exactly what <see cref="FederatedReplayManager.LoadGroup"/> accepts.
    /// </summary>
    public sealed record RecordingGroup(Guid ExerciseId, DateTime StartTimeUtc, TimeSpan Duration, IReadOnlyList<string> Paths);

    /// <summary>
    /// ⭐ RB-1 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — <b>the exercises a standalone Replay Browser can open</b>: every
    /// recorded node file under the staging root, where each node records (<see cref="OrchestrationConstants.GetNodeExercisesRoot(string, int)"/>,
    /// <c>nodes/node-N/exercises/&lt;id&gt;/node_N.fdp</c>) and where the archive lands (<c>shared/exercises/&lt;id&gt;/</c>), grouped by
    /// the exercise id their sidecars carry.
    /// </summary>
    /// <remarks>
    /// <para>⭐ Keyed on the SIDECAR, never the folder name — the folder only says where a file lies, the sidecar is what
    /// <see cref="FederatedReplayManager.LoadGroup"/> validates (one exercise id, one file per node).</para>
    /// <para>⚠ A node recorded locally AND archived appears twice: the local copy wins (it is scanned first), so a group never
    /// carries a duplicate node, which <see cref="FederatedReplayManager.LoadGroup"/> refuses.</para>
    /// </remarks>
    public static class RecordingGroupInventory
    {
        /// <summary>Every openable exercise under <paramref name="stagingRoot"/>, newest first. A missing root is an empty list.</summary>
        public static IReadOnlyList<RecordingGroup> Scan(string stagingRoot)
        {
            var groups = new Dictionary<Guid, (DateTime Start, TimeSpan Duration, SortedDictionary<int, string> Nodes)>();
            foreach (var exercisesRoot in ExerciseRoots(stagingRoot))
            foreach (var dir in SafeDirectories(exercisesRoot))
            foreach (var fdp in SafeFiles(dir, "*.fdp"))
            {
                var meta = ReadSidecar(fdp);
                if (meta is null || meta.ExerciseId == Guid.Empty) continue;
                if (!groups.TryGetValue(meta.ExerciseId, out var g))
                    g = (meta.Timestamp, meta.Duration, new SortedDictionary<int, string>());
                if (g.Nodes.ContainsKey(meta.NodeId)) continue;   // the local copy wins over the archived one
                g.Nodes[meta.NodeId] = fdp;
                g = (meta.Timestamp < g.Start ? meta.Timestamp : g.Start, meta.Duration > g.Duration ? meta.Duration : g.Duration, g.Nodes);
                groups[meta.ExerciseId] = g;
            }
            return groups
                .Select(kv => new RecordingGroup(kv.Key, kv.Value.Start, kv.Value.Duration, kv.Value.Nodes.Values.ToList()))
                .OrderByDescending(g => g.StartTimeUtc)
                .ToList();
        }

        private static IEnumerable<string> ExerciseRoots(string stagingRoot)
        {
            foreach (var node in SafeDirectories(Path.Combine(stagingRoot, "nodes")))
                yield return Path.Combine(node, OrchestrationConstants.ExercisesDirectoryName);
            yield return Path.Combine(OrchestrationConstants.GetSharedRoot(stagingRoot), OrchestrationConstants.ExercisesDirectoryName);
        }

        private static RecordingMetadata? ReadSidecar(string fdpPath)
        {
            var metaPath = fdpPath + ".meta.json";
            if (!File.Exists(metaPath)) return null;
            try { return MetadataSerializer.Deserialize(File.ReadAllText(metaPath)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
        }

        private static IEnumerable<string> SafeDirectories(string root)
            => Directory.Exists(root) ? Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal) : Enumerable.Empty<string>();

        // Case-insensitive: *.fdp files may have drifted extension casing when authored on Windows.
        private static IEnumerable<string> SafeFiles(string dir, string pattern)
            => Directory.GetFiles(dir, pattern, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                        .OrderBy(f => f, StringComparer.Ordinal);
    }
}
