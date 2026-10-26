using System;
using System.Collections.Generic;
using System.Threading;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9 TL-7) — ONE pass over a recording, on its OWN
    /// <see cref="PlaybackController"/>, repository and bus (the isolation invariant: the GUI's sandbox is never stepped). Every
    /// <paramref name="chunkFrames"/> frames it publishes an immutable snapshot, so a panel fills in progressively (§2b).
    /// </summary>
    public static class TimelineIndexBuilder
    {
        public const int DefaultChunkFrames = 2000;

        /// <summary>The R1 extractors: Life, Combat, Behaviour (TL-5).</summary>
        public static ITimelineExtractor[] DefaultExtractors() => new ITimelineExtractor[] { new LifeExtractor(), new CombatExtractor(), new BehaviourExtractor() };

        public static TimelineIndex Build(string fdpPath, Action<TimelineIndex>? onChunk = null, int chunkFrames = DefaultChunkFrames,
            CancellationToken ct = default, IReadOnlyList<ITimelineExtractor>? extractors = null)
        {
            if (fdpPath == null) throw new ArgumentNullException(nameof(fdpPath));
            extractors ??= DefaultExtractors();
            if (chunkFrames < 1) chunkFrames = 1;

            using var playback = new PlaybackController(fdpPath);
            using var repo = new EntityRepository();
            RecordingSearchService.RegisterAllComponents(repo, playback);
            playback.EventBus = repo.Bus;   // the repository's own bus, as the search's event scan (CE-3293 S3)

            int total = playback.TotalFrames;
            var ticks = new long[total];
            var sink = new TimelineEventSink();
            int scanned = 0;
            while (playback.StepForward(repo))
            {
                if (ct.IsCancellationRequested) break;
                int frame = playback.CurrentFrame;
                if (frame < total) ticks[frame] = playback.GetFrameMetadata(frame).WallClockTicks;
                for (int i = 0; i < extractors.Count; i++) extractors[i].Frame(repo, repo.Bus, frame, sink);
                scanned = frame + 1;
                if (onChunk != null && scanned % chunkFrames == 0 && scanned < total)
                    onChunk(sink.Snapshot(ticks[..scanned], scanned, total));
            }
            var index = sink.Snapshot(scanned == total ? ticks : ticks[..scanned], scanned, total);
            onChunk?.Invoke(index);
            return index;
        }
    }
}
