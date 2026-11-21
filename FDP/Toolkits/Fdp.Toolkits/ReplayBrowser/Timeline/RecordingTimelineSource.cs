using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9) — what a timeline panel reads: the newest index snapshot. A recording's
    /// source fills it from a sidecar or a background build (R1-a); the editor's live source fills it as the world runs (R1-e).
    /// </summary>
    public interface ITimelineSource
    {
        /// <summary>The newest immutable snapshot — safe to read from the frame thread while a build runs.</summary>
        TimelineIndex Index { get; }
        /// <summary>0…1 — the share of the recording scanned.</summary>
        float Progress { get; }
        bool IsComplete { get; }
        /// <summary>Why the index could not be built (e.g. the recording holds a component type this process does not know), else null.</summary>
        string? Error { get; }
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> (§9 TL-6 / TL-7, §9c ④) — opens a recording's index for the given lanes: every lane the sidecar holds with the SAME
    /// definition hash is reused; only the rest — a lane added or changed since — is scanned, on a background task that publishes a snapshot
    /// every chunk and rewrites the sidecar when it completes. Dispose cancels a build.
    /// </summary>
    public sealed class RecordingTimelineSource : ITimelineSource, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private volatile TimelineIndex _index = TimelineIndex.Empty;

        private RecordingTimelineSource(string fdpPath) { Path = fdpPath; }

        public string Path { get; }
        public TimelineIndex Index => _index;
        public float Progress { get { var i = _index; return i.TotalFrames == 0 ? (Completion.IsCompleted ? 1f : 0f) : (float)i.ScannedFrames / i.TotalFrames; } }
        public bool IsComplete => Completion.IsCompleted && _index.IsComplete;
        public string? Error { get; private set; }
        /// <summary>True when every lane came from the sidecar (no pass ran).</summary>
        public bool FromSidecar => ScannedLanes.Count == 0;
        /// <summary>The lanes this open had to scan (new or changed definitions); empty when the sidecar held them all.</summary>
        public IReadOnlyList<string> ScannedLanes { get; private set; } = Array.Empty<string>();
        /// <summary>Completes when the index is final (at once for a sidecar).</summary>
        public Task Completion { get; private set; } = Task.CompletedTask;

        /// <param name="background">false builds on the caller's thread (tests, the command line).</param>
        public static RecordingTimelineSource Open(string fdpPath, IReadOnlyList<IndexedLaneDefinition> lanes, TimelineLaneCompiler compiler,
            bool background = true, int chunkFrames = TimelineIndexBuilder.DefaultChunkFrames)
        {
            if (fdpPath == null) throw new ArgumentNullException(nameof(fdpPath));
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));
            var source = new RecordingTimelineSource(fdpPath);
            var hashes = lanes.ToDictionary(l => l.Lane.Id, TimelineSidecar.HashOf, StringComparer.Ordinal);

            var cached = TimelineSidecar.TryRead(fdpPath);
            var reused = new Dictionary<string, SharedList<TimelineEvent>>(StringComparer.Ordinal);
            var missing = new List<IndexedLaneDefinition>();
            foreach (var lane in lanes)
            {
                string id = lane.Lane.Id;
                if (cached is { } c && c.Hashes.TryGetValue(id, out var h) && h == hashes[id]) reused[id] = c.Index.Raw(id);
                else missing.Add(lane);
            }
            TimelineIndex? kept = cached is { } k
                ? new TimelineIndex(reused, new SharedList<long>(k.Index.FrameTicks.ToArray()), k.Index.ScannedFrames, k.Index.TotalFrames, frameSim: k.Index.FrameSim, positions: k.Index.Positions)   // ⭐ CE-4119 — the sim times too
                : null;
            source.ScannedLanes = missing.Select(l => l.Lane.Id).ToArray();
            if (missing.Count == 0 && kept != null)
            {
                source._index = kept;
                return source;
            }
            if (kept != null) source._index = kept;   // the reused lanes draw at once; the new ones fill in as the pass runs

            var ct = source._cts.Token;
            void Run()
            {
                try
                {
                    var built = TimelineIndexBuilder.Build(fdpPath, missing, compiler,
                        snap => source._index = kept == null ? snap : TimelineIndexBuilder.Merge(kept, snap), chunkFrames, ct);
                    var index = kept == null ? built : TimelineIndexBuilder.Merge(kept, built);
                    source._index = index;
                    if (!ct.IsCancellationRequested) TimelineSidecar.Write(fdpPath, index, hashes);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)   // a background task: the error is SHOWN, never lost in a faulted task
                {
                    // ⚠ measured on a real recording: a component type from an assembly this process never loaded stops the playback
                    //   (the Replay Browser's own sandbox registers the same way). The part scanned so far stays drawn; the panel says why.
                    source.Error = ex.Message;
                }
            }
            if (background) source.Completion = Task.Run(Run, ct);
            else Run();
            return source;
        }

        /// <summary>True once <see cref="Dispose"/> ran (a build still running has been told to stop).</summary>
        public bool IsDisposed => _cts.IsCancellationRequested;

        public void Dispose()
        {
            _cts.Cancel();   // ⚠ not disposed: a running build still polls the token
        }
    }
}
