using System;
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
    }

    /// <summary>
    /// ⭐ <c>CE-1302</c> (§9 TL-6 / TL-7) — opens a recording's index: the sidecar when it matches, else a <see cref="TimelineIndexBuilder"/>
    /// pass on a background task that publishes a snapshot every chunk and writes the sidecar when it completes. Dispose cancels a build.
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
        /// <summary>True when the index came from the sidecar (no pass ran).</summary>
        public bool FromSidecar { get; private set; }
        /// <summary>Completes when the index is final (at once for a sidecar).</summary>
        public Task Completion { get; private set; } = Task.CompletedTask;

        /// <param name="background">false builds on the caller's thread (tests, the command line).</param>
        public static RecordingTimelineSource Open(string fdpPath, bool background = true, int chunkFrames = TimelineIndexBuilder.DefaultChunkFrames)
        {
            if (fdpPath == null) throw new ArgumentNullException(nameof(fdpPath));
            var source = new RecordingTimelineSource(fdpPath);
            var cached = TimelineSidecar.TryRead(fdpPath);
            if (cached != null)
            {
                source._index = cached;
                source.FromSidecar = true;
                return source;
            }
            var ct = source._cts.Token;
            void Run()
            {
                var index = TimelineIndexBuilder.Build(fdpPath, snap => source._index = snap, chunkFrames, ct);
                source._index = index;
                if (!ct.IsCancellationRequested) TimelineSidecar.Write(fdpPath, index);
            }
            if (background) source.Completion = Task.Run(Run, ct);
            else Run();
            return source;
        }

        public void Dispose()
        {
            _cts.Cancel();   // ⚠ not disposed: a running build still polls the token
        }
    }
}
