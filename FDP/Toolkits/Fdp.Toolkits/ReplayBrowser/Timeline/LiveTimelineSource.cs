using System;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Timeline
{
    /// <summary>
    /// ⭐ <c>CE-3330</c> — TE-17 / DAR row 28 (docs/DESIGN_Replay_Time_Axis_And_Review.md §9e; docs/DESIGN_Director_Timeline_Editor.md §2c rule 7,
    /// §2d) — the timeline's LIVE source: the host's catalogue of indexed lanes evaluated on the live world each sim frame by the SAME per-frame
    /// step the recording's builder runs (<see cref="TimelineFrameScanner"/>), appended. A frame is the world's own (<see cref="GlobalTime.FrameNumber"/>
    /// advancing — a paused world adds nothing); its ticks are the wall clock, as a recording's (TL-4).
    /// <list type="bullet">
    /// <item>a NEW run (another world, or its frame counter going back — a rewind to edit) starts the index afresh;</item>
    /// <item>a lane registered or changed in the catalogue starts it afresh too (its definition is compiled once);</item>
    /// <item>⭐ the snapshot the panel reads is published at most every <see cref="PublishTicks"/> — copying the lanes each frame would cost
    /// what the index exists to avoid.</item>
    /// </list>
    /// ⛔ It never clears the world's destruction log (the recorder's) and never writes a file; the end-of-run sidecar is backend's (row 33).
    /// </summary>
    public sealed class LiveTimelineSource : ITimelineSource
    {
        /// <summary>The longest a snapshot lags the live world (a quarter of a second).</summary>
        public const long PublishTicks = TimeSpan.TicksPerSecond / 4;

        private readonly TimelineLaneCatalog _catalog;
        private readonly TimelineLaneContext _context;
        private readonly TimelineLaneCompiler _compiler;
        private TimelineFrameScanner? _scanner;
        private EntityRepository? _world;
        private int _catalogVersion = -1;
        private long _lastFrame = long.MinValue;
        private long _lastPublish = long.MinValue;
        private volatile TimelineIndex _index = TimelineIndex.Empty;

        public LiveTimelineSource(TimelineLaneCatalog catalog, TimelineLaneContext context, TimelineLaneCompiler compiler)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _context = context;
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        }

        public TimelineIndex Index => _index;
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;

        /// <summary>Frames indexed in the current run.</summary>
        public int Frames => _scanner?.Frames ?? 0;

        /// <summary>
        /// One host frame: when <paramref name="world"/>'s sim frame advanced, its lanes are evaluated as the next index frame at
        /// <paramref name="wallTicks"/>. Call it on the frame thread, after the world ticked.
        /// </summary>
        /// <param name="wallTicks">⭐ CE-3330 E1 — the FALLBACK wall clock: a world with a <see cref="GlobalTime"/> whose
        /// <see cref="GlobalTime.TotalWallTicks"/> is set is stamped with THAT (the frame-locked wall the flight recorder writes, so the live index
        /// shares a recording's and a session's clock exactly); a world without one (a rail, a bare host) uses this.</param>
        /// <param name="force">Publish the snapshot now (a rail; the host's last frame).</param>
        public void Step(EntityRepository world, long wallTicks, bool force = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            long frame = _lastFrame + 1;
            double sim = double.NaN;
            if (world.HasSingleton<GlobalTime>())
            {
                var time = world.GetSingleton<GlobalTime>();
                frame = time.FrameNumber;
                sim = time.TotalTime;
                if (time.TotalWallTicks != 0) wallTicks = time.TotalWallTicks;
            }
            if (!ReferenceEquals(world, _world) || frame < _lastFrame || _catalog.Version != _catalogVersion) Reset(world);
            if (frame == _lastFrame) { if (force) Publish(wallTicks); return; }
            _lastFrame = frame;
            _scanner!.Step(world, _scanner.Frames, wallTicks, clearDestructionLog: false, simSeconds: sim);
            // ⚠ the first publish is unconditional — `wallTicks - long.MinValue` would overflow negative (measured: a live editor's index
            //   stayed empty for a whole run until this)
            if (force || _lastPublish == long.MinValue || wallTicks - _lastPublish >= PublishTicks) Publish(wallTicks);
        }

        /// <summary>Starts a new run's index for <paramref name="world"/> (also on a new world, a rewind or a changed catalogue).</summary>
        public void Reset(EntityRepository? world)
        {
            _world = world;
            _catalogVersion = _catalog.Version;
            _scanner = new TimelineFrameScanner(_catalog.Indexed(_context), _compiler);
            _lastFrame = long.MinValue;
            _lastPublish = long.MinValue;
            _index = TimelineIndex.Empty;
        }

        private void Publish(long wallTicks)
        {
            if (_scanner == null) return;
            int n = _scanner.Frames;
            _index = _scanner.Snapshot(n, n);
            _lastPublish = wallTicks;
        }
    }
}
