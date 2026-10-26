using System;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-3330</c> E6 (docs/DESIGN_Director_Timeline_Editor.md §2e) — the ONE "keep a recording's index open" holder: re-opens a
    /// <see cref="RecordingTimelineSource"/> when the recording's path OR the lane catalogue (its <see cref="TimelineLaneCatalog.Version"/>)
    /// changed, otherwise keeps the source it has. The Replay Browser (the primary node's recording) and every ECS node in a cluster replay
    /// (its own recording, <c>HrotNodeContext.ShownTimeline</c>) hold one — no second copy of the reopen rule.
    /// </summary>
    public sealed class RecordingTimelineSlot : IDisposable
    {
        private readonly TimelineLaneCatalog _catalog;
        private readonly TimelineLaneContext _context;
        private readonly Func<TimelineLaneCompiler> _compiler;
        private readonly bool _background;
        private RecordingTimelineSource? _source;
        private string? _path;
        private int _version = -1;
        private bool _disposed;

        /// <param name="compiler">Called once, on the first open — building the compiler is not free and most nodes never replay.</param>
        /// <param name="background">false builds on the caller's thread (tests).</param>
        public RecordingTimelineSlot(TimelineLaneCatalog catalog, TimelineLaneContext context, Func<TimelineLaneCompiler> compiler, bool background = true)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _context = context;
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _background = background;
        }

        /// <summary>The open source over the last synced path; null when that was null.</summary>
        public RecordingTimelineSource? Source => _source;

        /// <summary>The path <see cref="Source"/> was opened for.</summary>
        public string? Path => _path;

        /// <summary>Re-opens when <paramref name="path"/> or the catalogue changed (disposing the old source); a no-op otherwise. Cheap per frame.</summary>
        public void Sync(string? path)
        {
            if (_disposed) return;
            if (path == _path && _catalog.Version == _version) return;
            _source?.Dispose();
            _source = path == null ? null : RecordingTimelineSource.Open(path, _catalog.Indexed(_context), _compiler(), _background);
            _path = path;
            _version = _catalog.Version;
        }

        public void Dispose()
        {
            _disposed = true;
            _source?.Dispose();
            _source = null;
        }
    }
}
