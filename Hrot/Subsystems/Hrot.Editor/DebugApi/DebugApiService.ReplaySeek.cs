using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ RB-2 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — <b>the seek forms that are LOOKUPS over the recording</b>, resolved
    /// here for EVERY host and then sent as the ordinary seek (<see cref="SeekReplayTo"/>): <c>{frame}</c>, <c>{frames: ±n}</c>,
    /// <c>{cause: {frame, entity}}</c>, <c>{next: {entity, component?}, direction}</c>. ⛔ No host seeks its own way.
    /// </summary>
    /// <remarks>
    /// <para>⭐ A frame becomes a moment through THIS node's recording index (its wall ticks) and the replay's zero
    /// (<see cref="Hrot.Presentation.DebugApi.ReplayPosition"/>): <c>atSeconds = (ticks(frame) − zero) / 1e7</c> — exact on the Replay
    /// Browser and the editor, and on a cluster node it lands on the frame the master's seek round restores there.</para>
    /// <para>⚠ <c>next</c> scans the whole file: <see cref="ResolveReplaySeek"/> returns it as a job the host runs OFF the main thread
    /// (in a reader of its own, never the shown world), then <see cref="SeekReplayToFrame"/> seeks.</para>
    /// </remarks>
    public sealed partial class DebugApiService
    {
        /// <summary>A <c>next</c> lookup to run off the main thread.</summary>
        public sealed record ChangeScan(string Path, int StartFrame, long NetworkId, string? Component, int Direction,
                                        Fdp.Toolkit.Scenario.ScenarioSerializer Serializer);

        /// <summary>The replay's zero and now on THIS node: the editor's own recorder, else the active provider.</summary>
        private Hrot.Presentation.DebugApi.ReplayPosition? ReplayPositionOrNull
            => _rrController is { IsReplayActive: true } rr
                ? new Hrot.Presentation.DebugApi.ReplayPosition(
                      rr.ReplayAnchorWallTicks > 0 ? rr.ReplayAnchorWallTicks : rr.ActiveRecordingStartWallTicks,
                      rr.GetCurrentReplayTime().TotalWallTicks)
                : _dispatcher?.ReplayPosition;

        private Fdp.Toolkit.Scenario.ScenarioSerializer? _lookupSerializer;

        /// <summary>The serializer whole-file lookups dump an entity with — built once, from this host's behaviours (CE-171: never the
        /// reflection fallback, which collapses a fixed array to its first element and would miss a change past it).</summary>
        private Fdp.Toolkit.Scenario.ScenarioSerializer LookupSerializer
            => _lookupSerializer ??= Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(
                   _behaviorRegistry ?? new Fdp.Toolkit.Behavior.BehaviorRegistry(), _blueprintRegistry);

        /// <summary>
        /// <c>POST /replay/seek</c>, main thread: a plain <c>{atSeconds}</c> seeks at once; a frame form resolves to its moment and seeks;
        /// <c>{next}</c> comes back as a <see cref="ChangeScan"/> for the host to run off the main thread.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory, ChangeScan? Scan) ResolveReplaySeek(JsonNode? body)
        {
            if (body?["frame"] is null && body?["frames"] is null && body?["cause"] is null && body?["next"] is null)
            {
                var plain = SeekReplayTo(body);
                return (plain.Result, plain.Error, plain.HintCategory, null);
            }
            if (CurrentClusterStateOrNull() is { } s && s != ClusterState.OperatingReplay)
                return (null, $"The cluster is in {s}, not a replay: POST /replay first.", DebugApiHints.Scenario, null);
            var ctx = ScanContext(out var path);
            if (ctx?.Playback is not { } playback || path is null) return (null, NoReplayRecording, DebugApiHints.Scenario, null);
            if (ReplayPositionOrNull is not { } position)
                return (null, "This host does not know where its replay stands (no replay position).", DebugApiHints.Scenario, null);
            int now = FrameAt(playback, position.NowWallTicks);

            try
            {
                if (body!["frame"] is JsonNode f) return Done(SeekReplayToFrame(Int(f), "frame"));
                if (body["frames"] is JsonNode n) return Done(SeekReplayToFrame(now + Int(n), "frames"));
                if (body["cause"] is JsonObject cause)
                {
                    // ⭐ the causality jump: the frame AFTER the event, where its effect shows (replay-browser-2 §4.7)
                    if (cause["frame"] is null) return (null, "cause.frame is required: {\"cause\": {\"frame\": 4119, \"entity\": 1042}}.", DebugApiHints.Scenario, null);
                    var seek = SeekReplayToFrame(Int(cause["frame"]!) + 1, "cause");
                    if (seek.Result is JsonObject o && cause["entity"] is JsonNode target) o["entity"] = Long(target);
                    return Done(seek);
                }
                if (body["next"] is JsonObject next)
                {
                    if (next["entity"] is null)
                        return (null, "next.entity is required: {\"next\": {\"entity\": 1042, \"component\": \"Health\"}, \"direction\": 1}.", DebugApiHints.Scenario, null);
                    int direction = body["direction"] is JsonNode d && Int(d) < 0 ? -1 : 1;
                    return (null, null, null, new ChangeScan(path, now, Long(next["entity"]!),
                        next["component"]?.ToString(), direction, LookupSerializer));
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                return (null, $"Invalid seek: {ex.Message}", DebugApiHints.Scenario, null);
            }
            return (null, "cause must be an object: {\"cause\": {\"frame\": 4119, \"entity\": 1042}}; next likewise.", DebugApiHints.Scenario, null);

            static (JsonNode?, string?, string?, ChangeScan?) Done((JsonNode? R, string? E, string? H) r) => (r.R, r.E, r.H, null);
        }

        /// <summary>
        /// A <see cref="ChangeScan"/>, OFF the main thread: the next (previous) frame where the unit changed — in the named component, or
        /// any. <see langword="null"/> when nothing changes in that direction.
        /// </summary>
        public static int? RunChangeScan(ChangeScan scan)
        {
            var serializer = scan.Serializer;
            return Fdp.Toolkit.ReplayBrowser.Diff.EntityChangeFinder.Find(
                scan.Path, scan.StartFrame,
                repo => NetworkIdResolver.FindEntityByNetworkId(repo, scan.NetworkId),
                scan.Direction,
                (repo, e) => serializer.SerializeEntity(repo, e, new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver(), repo.GetSnapshotableMask()),
                name => scan.Component is null || string.Equals(name, scan.Component, StringComparison.OrdinalIgnoreCase),
                epsilon: 0.0);
        }

        /// <summary>Main thread: seeks the replay to frame <paramref name="frame"/> of THIS node's recording (clamped), as the ordinary seek.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) SeekReplayToFrame(int frame, string resolvedFrom)
        {
            var ctx = ScanContext(out _);
            if (ctx?.Playback is not { } playback) return (null, NoReplayRecording, DebugApiHints.Scenario);
            if (ReplayPositionOrNull is not { } position)
                return (null, "This host does not know where its replay stands (no replay position).", DebugApiHints.Scenario);
            int target = Math.Clamp(frame, 0, Math.Max(0, playback.TotalFrames - 1));
            long ticks = playback.GetFrameMetadata(target).WallClockTicks;
            double at = Math.Max(0, (ticks - position.ZeroWallTicks) / (double)TimeSpan.TicksPerSecond);
            var seek = SeekReplayTo(new JsonObject { ["atSeconds"] = at });
            if (seek.Result is JsonObject o)
            {
                o["frame"] = target;
                o["resolvedFrom"] = resolvedFrom;
            }
            return seek;
        }

        // ⚠ Lenient: a number may arrive parsed from HTTP or built in memory (an int where a long is read) — read its text.
        private static long Long(JsonNode n) => long.Parse(n.ToJsonString().Trim('"'), System.Globalization.CultureInfo.InvariantCulture);
        private static int Int(JsonNode n) => int.Parse(n.ToJsonString().Trim('"'), System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The last frame of the recording at or before <paramref name="wallTicks"/> (0 before its first).</summary>
        internal static int FrameAt(PlaybackController playback, long wallTicks)
        {
            int lo = 0, hi = playback.TotalFrames - 1, found = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (playback.GetFrameMetadata(mid).WallClockTicks <= wallTicks) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }
    }
}
