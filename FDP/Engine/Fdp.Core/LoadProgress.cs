using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-2417</c> — <b>how far a node's load has got</b>: one monotonic count per node that every loader advances from inside its work
    /// (records parsed, bytes read, stages done) plus the world's <see cref="EntityRepository.StructuralChanges"/> (entity creation counts on its
    /// own). Read by both heartbeat sources — the frame's (<c>ClusterSlave</c>) and the pump's (<c>NodeOpSlaveTranslator</c>) — so while a node
    /// loads, a heartbeat means <i>"the load is advancing"</i>, and a loader that hangs stops the beats and gets the node ejected by the master's
    /// existing 5 s rule.
    ///
    /// <para>🔒 User, <c>2026-11-05</c>: <i>"it must be watching the entity load progress - if it gets stuck, no more hearbeat shall be sent"</i> ·
    /// <i>"for sure all loaders need to report progress, how else we would find out it is hung? Nothing should be interruptible for 5 seconds"</i>.</para>
    ///
    /// <para>⭐ One per node <b>by construction</b>: <see cref="For"/> keys it on the node's event bus, which the slave and its translator share.
    /// ⭐ Loaders reach it through <see cref="Report"/> — the ambient <see cref="Current"/> the slave sets before it runs a handler, which flows
    /// into the handler's awaits and its <c>Task.Run</c> workers — so no loader signature changes.</para>
    ///
    /// <para>📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §12.</para>
    /// </summary>
    public sealed class LoadProgress
    {
        private static readonly ConditionalWeakTable<object, LoadProgress> ByOwner = new();
        private static readonly AsyncLocal<LoadProgress?> Ambient = new();

        /// <summary>⭐ The node's progress — the one instance for this <paramref name="owner"/> (the node's event bus).</summary>
        public static LoadProgress For(object owner) => ByOwner.GetValue(owner ?? throw new ArgumentNullException(nameof(owner)), static _ => new LoadProgress());

        /// <summary>⭐ The progress of the node whose handler is running here (set by the slave; flows into awaits and workers), or <see langword="null"/>.</summary>
        public static LoadProgress? Current
        {
            get => Ambient.Value;
            set => Ambient.Value = value;
        }

        /// <summary>⭐ What a loader calls from inside its work: the running node's load advanced by <paramref name="amount"/>. Free when no load runs.</summary>
        public static void Report(long amount = 1,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)   // ⭐ the call site, for the longest-silence diagnostic (compile-time constants: no allocation)
            => Ambient.Value?.AdvanceAt(amount, file, line);

        /// <summary>
        /// ⭐ <c>CE-11038</c> — a loader's long stretch says HOW FAR it has got (<i>"navmesh 12000/59834"</i>): the running node's step text carries it
        /// (<see cref="Detail"/>), so the one progress report every watcher reads (the cluster's load step; the director's stall clock) CHANGES while a
        /// stage that has no step of its own works for long. Throttled to once a second (the last call, <c>done == total</c>, always lands); the text is
        /// built only when it is stored — a call in between is free (0 B). Free when no load runs.
        /// <para>⭐⭐ <c>CE-5060</c> — and it ADVANCES the count, as <see cref="Report"/> does: a loader saying how far it has got HAS advanced, and both heartbeat sources
        /// beat only while the count moves (<see cref="MovedWithin"/>). 📐 Measured: a first-use terrain import inside a node's load reported only its detail
        /// (the road levelling, 12 s on Stechovice) and the master ejected the node for a heartbeat timeout 8 s in. A hung loader still reports nothing.</para>
        /// <para>⭐ Only when the report CHANGED (<paramref name="what"/> or <paramref name="done"/> differs from the last one): a stuck loop re-reporting the same value
        /// is a hung loader and must not keep its node alive (coordinator review, <c>2026-11-26</c>). Reports from parallel workers race benignly (a change may advance
        /// twice or once; an unchanged value never does).</para>
        /// </summary>
        public static void ReportDetail(string what, long done, long total,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (Ambient.Value is not { } progress) return;
            progress.SetDetail(what, done, total);
            if (progress.DetailChanged(what, done)) progress.AdvanceAt(1, file, line);
        }

        private string? _lastWhat;
        private long _lastDone = long.MinValue;

        /// <summary>True when this report differs from the previous one (and remembers it). Allocation-free: the text is compared, never built.</summary>
        private bool DetailChanged(string what, long done)
        {
            if (done == Volatile.Read(ref _lastDone) && string.Equals(what, Volatile.Read(ref _lastWhat), StringComparison.Ordinal)) return false;
            Volatile.Write(ref _lastWhat, what);
            Volatile.Write(ref _lastDone, done);
            return true;
        }

        /// <summary>⭐ The detail the last <see cref="ReportDetail"/> stored for the step in flight (<c>null</c> when none; cleared when a step is entered).</summary>
        public string? Detail => _detail;

        private volatile string? _detail;
        private long _detailAt;   // Stopwatch ticks of the last stored detail

        private void SetDetail(string what, long done, long total)
        {
            long now = Stopwatch.GetTimestamp();
            long last = Volatile.Read(ref _detailAt);
            if (done < total && last != 0 && now - last < Stopwatch.Frequency) return;
            Volatile.Write(ref _detailAt, now);
            _detail = $"{what} {done}/{total}";
        }

        private long _count;
        private int _active;
        private EntityRepository? _world;

        private readonly object _gate = new();
        private long _lastMovedAt;          // Stopwatch ticks
        private long _longestSilence;       // Stopwatch ticks, while active
        private string? _longestSilenceStep;
        private volatile string? _step;

        /// <summary>The load advanced by <paramref name="amount"/> (thread-safe; any thread). ⭐ Stamped with its time, so a stall is seen from the
        /// last advance — not from the poll that happened to notice it (up to a second late).</summary>
        public void Advance(long amount = 1)
        {
            Interlocked.Add(ref _count, amount);
            Volatile.Write(ref _lastAdvanceAt, Stopwatch.GetTimestamp());
        }

        private long _lastAdvanceAt;   // Stopwatch ticks

        private void AdvanceAt(long amount, string file, int line)
        {
            _siteFile = file; _siteLine = line;   // a diagnostic pair: a torn read names a neighbouring site, harmlessly
            Advance(amount);
        }

        private volatile string _siteFile = "";
        private volatile int _siteLine;
        private string _movedFile = "", _longestFrom = "", _longestTo = "";
        private int _movedLine;
        private static string Site(string file, int line) => file.Length == 0 ? "(the step's start)" : $"{System.IO.Path.GetFileName(file)}:{line}";
        private long _lastCount = long.MinValue, _lastWorld = long.MinValue;

        /// <summary>⭐ The node's world: its <see cref="EntityRepository.StructuralChanges"/> counts as progress (entity creation reports itself).</summary>
        public void Watch(EntityRepository? world) => _world = world;

        /// <summary>The count now — the loaders' advances plus the watched world's structural changes.</summary>
        public long Value => Interlocked.Read(ref _count) + (_world?.StructuralChanges ?? 0);

        /// <summary>⭐ A load step is working right now (a prepare or commit runs — not a node held or waiting on the cluster).</summary>
        public bool IsActive => Volatile.Read(ref _active) > 0;

        /// <summary>The step the last <see cref="Active"/> scope named (diagnostics).</summary>
        public string? Step => _step;

        /// <summary>⭐ Enter a working load step (<c>using</c>): <see cref="IsActive"/> while any is open. Entering counts as progress.</summary>
        public ActiveScope Active(string? step = null)
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _active) == 0)
                {
                    long now = Stopwatch.GetTimestamp();
                    Poll(now);            // what moved (or waited) BEFORE the work is not the work's: polled while still inactive, no gap is recorded
                    _lastMovedAt = now;   // a silence is measured from the start of the work, never from before it
                    _movedFile = ""; _movedLine = 0;   // ⇒ "(the step's start)"
                }
                if (step != null) _step = step;
                _detail = null;   // ⭐ CE-11038 — a detail belongs to the step that reported it
                Interlocked.Increment(ref _active);
            }
            Advance();
            return new ActiveScope(this);
        }

        private void Leave()
        {
            Poll(Stopwatch.GetTimestamp());   // the stretch up to the end of the work counts
            Interlocked.Decrement(ref _active);
        }

        /// <summary>⭐⭐ Did the load advance within the last <paramref name="seconds"/>? (polled by both heartbeat sources; thread-safe)</summary>
        public bool MovedWithin(double seconds)
        {
            long now = Stopwatch.GetTimestamp();
            lock (_gate)
            {
                Poll(now);
                return Stopwatch.GetElapsedTime(_lastMovedAt, now).TotalSeconds < seconds;
            }
        }

        /// <summary>⭐ The longest stretch an active load went without advancing (as seen by the polls), and the step it was in — the measure that a loader reports often enough.</summary>
        public (TimeSpan Longest, string? Step) LongestSilence
        {
            get { lock (_gate) return (TimeSpan.FromSeconds(_longestSilence / (double)Stopwatch.Frequency), _longestSilenceStep); }
        }

        /// <summary>⭐ Where the longest silence sat: the report site before it and the one that ended it (<c>File.cs:line</c>) — names the loader to split.</summary>
        public (string From, string To) LongestSilenceSites
        {
            get { lock (_gate) return (_longestFrom, _longestTo); }
        }

        /// <summary>Forget the longest silence (a new load starts).</summary>
        public void ResetLongestSilence()
        {
            lock (_gate) { _longestSilence = 0; _longestSilenceStep = null; _longestFrom = _longestTo = ""; }
        }

        private void Poll(long now)
        {
            lock (_gate)
            {
                long count = Interlocked.Read(ref _count);
                long world = _world?.StructuralChanges ?? 0;
                if (count == _lastCount && world == _lastWorld) return;
                // ⭐ When it moved: an advance carries its own time; a world change is only known to have happened by now.
                long movedAt = world != _lastWorld ? now : Math.Max(Volatile.Read(ref _lastAdvanceAt), _lastMovedAt);
                // (the sites are formatted only when a NEW longest silence is recorded — rare)
                if (IsActive)
                {
                    long gap = movedAt - _lastMovedAt;
                    if (gap > _longestSilence) { _longestSilence = gap; _longestSilenceStep = _step; _longestFrom = Site(_movedFile, _movedLine); _longestTo = world != _lastWorld ? "(entity creation)" : Site(_siteFile, _siteLine); }
                }
                if (world == _lastWorld && count != _lastCount) { _movedFile = _siteFile; _movedLine = _siteLine; }   // no formatting here: a poll stays allocation-free
                _lastCount   = count;
                _lastWorld   = world;
                _lastMovedAt = movedAt;
            }
        }

        /// <summary>The <c>using</c> handle of <see cref="Active"/>.</summary>
        public readonly struct ActiveScope : IDisposable
        {
            private readonly LoadProgress? _owner;
            internal ActiveScope(LoadProgress owner) => _owner = owner;
            /// <inheritdoc/>
            public void Dispose() => _owner?.Leave();
        }
    }
}
