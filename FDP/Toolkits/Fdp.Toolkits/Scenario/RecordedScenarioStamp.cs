using System;
using System.IO;
using System.Text.Json.Nodes;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ <c>CE-4091</c> (AQ108 D7, M3) — <b>THE one source a reader trusts for "which version of its scenario did this recording run"</b>:
    /// a small stamp the MASTER writes into the recording's scenario copy at GO-LIVE, from the SAVED <c>scenario.json</c> that was copied
    /// (<see cref="ScenarioDescription.HashOf"/>, the one hash rule), with the scenario's name and when it was recorded.
    /// <para>⚠ <b>Why not the load-time stamp.</b> <c>ScenarioDescriptionResident.Name/Hash</c> is stamped ONCE, by the node's load step, from the
    /// file AS LOADED; an edit → save → go-live that does not reload (the continued world, D3) keeps the old hash. That stamp stays — its
    /// <c>Text</c> is what a Save writes back, and its name/hash are the recorder's fallback for a recording that has no copy (a run before CE-4091,
    /// or one with recording off) — but <b>for the version that ran, read this stamp</b>, never the recording meta's
    /// <c>ScenarioHash</c>.</para>
    /// <para>⭐ <b>CE-3340 ② — and the readers go through ONE helper, <see cref="VersionOf"/></b>: the stamp of the recording's copy
    /// (<see cref="FindCopy"/>), falling back to the recording meta's load-time hash ONLY when no copy / stamp is reachable (a recording from
    /// before CE-4091, or a host that sees neither the master's ledger nor the archive). Nothing else reads <c>RecordingMetadata.ScenarioHash</c>.</para>
    /// <para>A plain file beside the copy's slices (<see cref="FileName"/>, not <c>.json</c>, so no per-node loader mistakes it for a slice);
    /// ⛔ never staged into a scenario that is opened for editing from the copy — that scenario is new, not a recording.</para>
    /// </summary>
    public sealed record RecordedScenarioStamp(string ScenarioName, string ScenarioHash, DateTime RecordedAt)
    {
        /// <summary>The stamp's file name inside a recording's scenario copy.</summary>
        public const string FileName = "recorded.stamp";

        /// <summary>The scenario file the hash is taken from.</summary>
        public const string ScenarioFileName = "scenario.json";

        /// <summary>
        /// Writes the stamp into <paramref name="copyDirectory"/>: the name, the hash of the copy's <c>scenario.json</c> as it is now (empty when the
        /// copy has none), and <paramref name="recordedAtUtc"/>. Returns what it wrote.
        /// </summary>
        public static RecordedScenarioStamp Write(string copyDirectory, string scenarioName, DateTime recordedAtUtc)
        {
            var file = Path.Combine(copyDirectory, ScenarioFileName);
            var hash = File.Exists(file) ? ScenarioDescription.HashOf(File.ReadAllText(file)) : string.Empty;
            var stamp = new RecordedScenarioStamp(scenarioName, hash, recordedAtUtc);
            var json = new JsonObject
            {
                ["scenarioName"] = stamp.ScenarioName,
                ["scenarioHash"] = stamp.ScenarioHash,
                ["recordedAt"]   = stamp.RecordedAt.ToString("O"),
            };
            File.WriteAllText(Path.Combine(copyDirectory, FileName), json.ToJsonString());
            return stamp;
        }

        /// <summary>
        /// ⭐ CE-3340 ② — the scenario copy of <paramref name="exerciseId"/> as a host that shares <paramref name="stagingRoot"/> can see it: the
        /// master's ledger snapshot (<c>{staging}/nodes/node-N/recording_ledger/&lt;id&gt;.scenario</c>, while the run is unarchived), else the archive's
        /// (<c>{shared}/exercises/&lt;id&gt;/scenario</c>); null when there is none. The same two places, in the same order, as the master's own lookup.
        /// </summary>
        public static string? FindCopy(string stagingRoot, Guid exerciseId)
        {
            if (exerciseId == Guid.Empty || string.IsNullOrEmpty(stagingRoot)) return null;
            try
            {
                var nodes = Path.Combine(stagingRoot, OrchestrationConstants.NodesDirectoryName);
                if (Directory.Exists(nodes))
                    foreach (var node in Directory.EnumerateDirectories(nodes))
                    {
                        var snapshot = Path.Combine(node, OrchestrationConstants.RecordingLedgerDirectoryName, OrchestrationConstants.GetScenarioSnapshotName(exerciseId));
                        if (Directory.Exists(snapshot)) return snapshot;
                    }
                var archived = OrchestrationConstants.GetArchivedScenarioDirectory(OrchestrationConstants.GetSharedRoot(stagingRoot), exerciseId);
                return Directory.Exists(archived) ? archived : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// ⭐⭐ CE-3340 ② — <b>THE reader of "which version of its scenario did this recording run"</b>: the hash in the stamp of the recording's copy
        /// (<see cref="FindCopy"/>), else — no copy reachable, no stamp (a recording from before CE-4091), or an empty hash — the recording meta's
        /// load-time <c>ScenarioHash</c>. Empty when neither knows. ⛔ Nobody else reads <c>RecordingMetadata.ScenarioHash</c>.
        /// </summary>
        public static string VersionOf(string stagingRoot, RecordingMetadata meta)
        {
            if (meta is null) throw new ArgumentNullException(nameof(meta));
            if (FindCopy(stagingRoot, meta.ExerciseId) is { } copy && Read(copy) is { ScenarioHash.Length: > 0 } stamp) return stamp.ScenarioHash;
            return meta.ScenarioHash ?? string.Empty;
        }

        /// <summary>The stamp in <paramref name="copyDirectory"/>, or null when there is none (a copy from before CE-4091) or it is unreadable.</summary>
        public static RecordedScenarioStamp? Read(string copyDirectory)
        {
            try
            {
                var path = Path.Combine(copyDirectory, FileName);
                if (!File.Exists(path)) return null;
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) return null;
                return new RecordedScenarioStamp(
                    (string?)o["scenarioName"] ?? string.Empty,
                    (string?)o["scenarioHash"] ?? string.Empty,
                    DateTime.TryParse((string?)o["recordedAt"], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : default);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or FormatException or InvalidCastException)
            {
                return null;
            }
        }
    }
}
