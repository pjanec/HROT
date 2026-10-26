using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-7, §9c) — ONE pass over a recording, on its OWN
    /// <see cref="PlaybackController"/>, repository and bus (the isolation invariant: the GUI's sandbox is never stepped), evaluating every
    /// given indexed lane on every frame. ⛔ It names no lane: each is a registered definition compiled by <see cref="TimelineLaneCompiler"/>
    /// (R-4079). Every <paramref name="chunkFrames"/> frames it publishes an immutable snapshot, so a panel fills in progressively (§2b).
    /// A lane whose definition cannot be compiled is left empty with its reason in <see cref="TimelineIndex.LaneErrors"/>.
    /// </summary>
    public static class TimelineIndexBuilder
    {
        public const int DefaultChunkFrames = 2000;

        /// <param name="compiler">The host's compilers (its edit service decides which event fields a path may name).</param>
        public static TimelineIndex Build(string fdpPath, IReadOnlyList<IndexedLaneDefinition> lanes, TimelineLaneCompiler compiler,
            Action<TimelineIndex>? onChunk = null, int chunkFrames = DefaultChunkFrames, CancellationToken ct = default)
        {
            if (fdpPath == null) throw new ArgumentNullException(nameof(fdpPath));
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            if (compiler == null) throw new ArgumentNullException(nameof(compiler));
            if (chunkFrames < 1) chunkFrames = 1;

            using var playback = new PlaybackController(fdpPath);
            using var repo = new EntityRepository();
            RecordingSearchService.RegisterAllComponents(repo, playback);
            playback.EventBus = repo.Bus;   // the repository's own bus, as the search's event scan (CE-3293 S3)

            var scans = new List<(string Lane, ILaneScan Scan)>();
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            var sink = new Dictionary<string, List<TimelineEvent>>(StringComparer.Ordinal);
            foreach (var lane in lanes)
            {
                sink[lane.Lane.Id] = new List<TimelineEvent>();
                try { scans.Add((lane.Lane.Id, compiler.Compile(lane))); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { errors[lane.Lane.Id] = ex.Message; }
            }

            int total = playback.TotalFrames;
            var ticks = new long[total];
            var results = new List<SearchResultDto>(64);
            int scanned = 0;
            while (playback.StepForward(repo))
            {
                if (ct.IsCancellationRequested) break;
                int frame = playback.CurrentFrame;
                long t = playback.GetFrameMetadata(frame).WallClockTicks;
                if (frame < total) ticks[frame] = t;
                foreach (var (laneId, scan) in scans)
                {
                    results.Clear();
                    scan.Scan(repo, frame, t, results);
                    var into = sink[laneId];
                    foreach (var r in results)
                    {
                        var p = r.Payload;
                        into.Add(p is { } payload
                            ? new TimelineEvent(frame, laneId, payload.Subject, payload.Other, payload.X, payload.Y, payload.Value)
                            : new TimelineEvent(frame, laneId, ResultIds.Of(repo, r.Entity), 0, 0, 0, 0));
                    }
                }
                repo.ClearDestructionLog();   // the structural scans read it; one frame's deaths belong to that frame
                scanned = frame + 1;
                if (onChunk != null && scanned % chunkFrames == 0 && scanned < total)
                    onChunk(Snapshot(sink, ticks[..scanned], scanned, total, errors));
            }
            var index = Snapshot(sink, scanned == total ? ticks : ticks[..scanned], scanned, total, errors);
            onChunk?.Invoke(index);
            return index;
        }

        private static TimelineIndex Snapshot(Dictionary<string, List<TimelineEvent>> sink, long[] ticks, int scanned, int total,
            Dictionary<string, string> errors)
        {
            var lanes = new Dictionary<string, TimelineEvent[]>(sink.Count, StringComparer.Ordinal);
            foreach (var (id, list) in sink) lanes[id] = list.ToArray();
            return new TimelineIndex(lanes, ticks, scanned, total, new Dictionary<string, string>(errors));
        }

        /// <summary>The union of two indexes over the same recording — lanes from <paramref name="fresh"/> win; frames from the one that scanned more.</summary>
        public static TimelineIndex Merge(TimelineIndex cached, TimelineIndex fresh)
        {
            var lanes = new Dictionary<string, TimelineEvent[]>(StringComparer.Ordinal);
            foreach (var id in cached.LaneIds) lanes[id] = cached.Raw(id);
            foreach (var id in fresh.LaneIds) lanes[id] = fresh.Raw(id);
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in cached.LaneErrors) errors[k] = v;
            foreach (var (k, v) in fresh.LaneErrors) errors[k] = v;
            var ticks = new long[fresh.FrameTicks.Count];
            for (int i = 0; i < ticks.Length; i++) ticks[i] = fresh.FrameTicks[i];
            return new TimelineIndex(lanes, ticks, fresh.ScannedFrames, fresh.TotalFrames, errors);
        }
    }
}
