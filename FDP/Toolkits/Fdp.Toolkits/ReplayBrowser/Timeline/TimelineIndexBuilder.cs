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

            // ⭐ CE-3330 (addendum 6) — the per-frame step is TimelineFrameScanner's, the one the live source runs too
            var scanner = new TimelineFrameScanner(lanes, compiler);
            int total = playback.TotalFrames;
            var ticks = new long[total];
            int scanned = 0;
            while (playback.StepForward(repo))
            {
                if (ct.IsCancellationRequested) break;
                int frame = playback.CurrentFrame;
                var meta = playback.GetFrameMetadata(frame);
                long t = meta.WallClockTicks;
                if (frame < total) ticks[frame] = t;
                // ⭐ CE-3330 E1 — the frame's recorded sim time; a v6 recording has none (FrameFlags.Unknown, NaN)
                double? sim = (meta.Flags & FrameFlags.Unknown) != 0 || double.IsNaN(meta.SimTime) ? null : meta.SimTime;
                scanner.Step(repo, frame, t, clearDestructionLog: true, simSeconds: sim);   // the structural scans read it; one frame's deaths belong to that frame
                scanned = frame + 1;
                if (onChunk != null && scanned % chunkFrames == 0 && scanned < total)
                    onChunk(scanner.Snapshot(scanned, total, ticks[..scanned]));
            }
            var index = scanner.Snapshot(scanned, total, scanned == total ? ticks : ticks[..scanned]);
            onChunk?.Invoke(index);
            return index;
        }

        /// <summary>The union of two indexes over the same recording — lanes from <paramref name="fresh"/> win; frames from the one that scanned more.</summary>
        public static TimelineIndex Merge(TimelineIndex cached, TimelineIndex fresh)
        {
            var lanes = new Dictionary<string, SharedList<TimelineEvent>>(StringComparer.Ordinal);
            foreach (var id in cached.LaneIds) lanes[id] = cached.Raw(id);
            foreach (var id in fresh.LaneIds) lanes[id] = fresh.Raw(id);
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in cached.LaneErrors) errors[k] = v;
            foreach (var (k, v) in fresh.LaneErrors) errors[k] = v;
            var ticks = new long[fresh.FrameTicks.Count];
            for (int i = 0; i < ticks.Length; i++) ticks[i] = fresh.FrameTicks[i];
            return new TimelineIndex(lanes, new SharedList<long>(ticks), fresh.ScannedFrames, fresh.TotalFrames, errors, fresh.FrameSim,
                cached.Positions.UnitCount > 0 ? cached.Positions : fresh.Positions);   // ⭐ CE-4059 — a sidecar's track is complete; a chunk's is partial
        }
    }
}
