using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Fdp.Core.Threading
{
    /// <summary>
    /// ⭐⭐ <b>N-3 (CE-4465) — THE TWO STAGING BUFFERS + THE PUBLISH QUEUE</b> (R-4216, R-4217; 📄 docs/DESIGN_Threaded_Work_Inventory.md §8.2a).
    /// The ONLY crossing from the simulation world S to the presentation world A — no lock between the two threads.
    /// <list type="bullet">
    /// <item>⭐ the SIM thread, after each tick (<see cref="Publish"/>): copies the masked state of S (the entity index, the table-mask types, the
    /// singletons, the ROWS of the entities the UI shows) into the buffer the UI is NOT pulling, then marks it "latest complete". ⭐ It NEVER waits:
    /// if that buffer is busy (the UI is still pulling it), the tick is NOT published — the next publish carries everything accumulated.</item>
    /// <item>⭐ the UI thread, at its frame start (<see cref="Pull"/>): copies the latest complete buffer into A — only when it holds a tick newer
    /// than A has — then hands A the events, logs and job replies that travelled with it, in order, exactly once (skipped ticks included).</item>
    /// <item>the UI → S direction: the events the UI published on A's bus ride UP (<see cref="TakeUplinkInto"/>, the sim thread, between ticks);
    /// every other UI effect is a job on the <see cref="SimJobQueue"/>.</item>
    /// </list>
    /// ⭐ Each buffer is held by ONE side at a time, claimed by a compare-and-swap on its state (free / writing / reading) — a failed claim is a skip
    /// (the sim) or a retry with the newer buffer (the UI), never a wait. ⛔ Allocation: none per steady tick / frame once the worlds are warm.
    /// </summary>
    public sealed class StagingWorlds : IPresentationOutbox, IDisposable
    {
        private const int Free = 0, Writing = 1, Reading = 2;

        private sealed class Buffer
        {
            public readonly EntityRepository World = new();
            public Entity[] Rows = new Entity[64];
            public int RowCount;
            public long Tick = -1;
            public int State;                                          // Free / Writing / Reading (Interlocked)
        }

        // ⭐ what travels WITH the publishes: one packet per published tick (its events + the replies / logs posted since the last publish), in a
        //   FIFO apart from the buffers — the UI drains every packet up to the tick it pulls, so a buffer it never pulled loses nothing
        private sealed class Packet
        {
            public long Tick;
            public readonly FdpEventBus Events = new();
            public List<Action<EntityRepository>> Replies = new();
        }
        private readonly Queue<Packet> _carried = new();
        private readonly Stack<Packet> _packetPool = new();
        private readonly object _carriedLock = new();
        private readonly List<Packet> _drainScratch = new();
        private FdpEventBus? _streamSource;

        private readonly Buffer[] _buf = { new(), new() };
        private volatile int _latest = -1;            // index of the latest COMPLETE buffer
        private long _pulledTick = -1;                // the UI side's last pulled tick (UI thread only)

        // ── the sim side's accumulation (sim thread only; a skipped publish leaves it to the next) ──
        private readonly FdpEventBus _pendingEvents = new();
        private List<Action<EntityRepository>> _pendingReplies = new();
        private readonly object _replyLock = new();   // a reply may be posted from a job (sim thread) or a log line (any thread)

        // ── the UI → S events ──
        private readonly FdpEventBus _uplink = new();
        private readonly object _uplinkLock = new();

        // ── the rows the UI shows (selected ∪ inspected ∪ pinned): set by the UI, read by the sim at publish ──
        private Entity[] _uiRows = new Entity[64];
        private int _uiRowCount;
        private readonly object _rowsLock = new();
        private readonly List<Entity> _rowScratch = new();

        /// <summary>The table types copied whole (the presentation's mask).</summary>
        public BitMask512 TableMask;
        /// <summary>Singletons A owns itself (never overwritten by a pull).</summary>
        public BitMask512 SingletonExcept;

        /// <summary>Extra rows the SIM side knows (e.g. S's selection while selection systems still tick on S) — called on the sim thread at publish.</summary>
        public Action<EntityRepository, List<Entity>>? SimRows { get; set; }

        /// <summary>Counters: ticks published / skipped (a buffer was busy) / pulled by the UI; the last publish's and pull's cost (ms).</summary>
        public long Published, Skipped, Pulled;
        public double LastPublishMs, LastPullMs, MaxPublishMs;

        /// <summary>The tick of the latest complete buffer (-1: none).</summary>
        public long LatestTick { get { int l = _latest; return l < 0 ? -1 : Volatile.Read(ref _buf[l].Tick); } }

        /// <summary>The tick A holds now.</summary>
        public long PulledTick => Volatile.Read(ref _pulledTick);

        /// <summary>A test seam: runs on the UI thread while it HOLDS a buffer (after the copy, before release) — the rails hold it to force skips.</summary>
        public Action? WhileHoldingForTest { get; set; }

        /// <summary>The event types both buses must know (copied from S's bus once, so a type first published later is carried too).</summary>
        public void RegisterStreamsFrom(FdpEventBus simBus)
        {
            _streamSource = simBus;
            _pendingEvents.CopyRegistrationsFrom(simBus);
            _uplink.CopyRegistrationsFrom(simBus);
        }

        /// <summary>The UI's rows (any thread; the latest call wins). Selected ∪ inspected ∪ pinned — every component of these is copied.</summary>
        public void SetRows(ReadOnlySpan<Entity> rows)
        {
            lock (_rowsLock)
            {
                if (_uiRows.Length < rows.Length) _uiRows = new Entity[Math.Max(rows.Length, _uiRows.Length * 2)];
                rows.CopyTo(_uiRows);
                _uiRowCount = rows.Length;
            }
        }

        /// <inheritdoc/> — a job reply / a log line bound for A, carried by the next successful publish.
        public void Post(Action<EntityRepository> onPresentation)
        {
            lock (_replyLock) _pendingReplies.Add(onPresentation);
        }

        // ═══════════════ the SIM thread ═══════════════

        /// <summary>
        /// After tick <paramref name="tick"/> of <paramref name="sim"/>: accumulates the tick's readable events, then publishes into the buffer the
        /// UI is not pulling. Returns false when that buffer is busy — the tick is skipped, nothing waits, and the next publish carries it.
        /// </summary>
        public bool Publish(EntityRepository sim, long tick)
        {
            long t0 = Stopwatch.GetTimestamp();
            _pendingEvents.PublishCurrentOf(sim.Bus, addMissingStreams: true);   // this tick's readable events join the accumulation

            int l = _latest;
            int w = l == 0 ? 1 : 0;
            var b = _buf[w];
            if (Interlocked.CompareExchange(ref b.State, Writing, Free) != Free)
            {   // the UI holds the buffer we would overwrite: skip — never wait (R-4217)
                Interlocked.Increment(ref Skipped);
                return false;
            }
            try
            {
                var world = b.World;
                world.SyncPresentationFrom(sim, TableMask, default);
                // the rows: the UI's set ∪ the sim side's own
                _rowScratch.Clear();
                lock (_rowsLock) for (int i = 0; i < _uiRowCount; i++) _rowScratch.Add(_uiRows[i]);
                SimRows?.Invoke(sim, _rowScratch);
                if (b.Rows.Length < _rowScratch.Count) b.Rows = new Entity[Math.Max(_rowScratch.Count, b.Rows.Length * 2)];
                _rowScratch.CopyTo(b.Rows);
                b.RowCount = _rowScratch.Count;
                world.SyncRowsFrom(sim, new ReadOnlySpan<Entity>(b.Rows, 0, b.RowCount), TableMask);
                // the accumulated events + replies → a packet for this tick (FIFO), and the accumulation emptied
                Packet pk;
                lock (_carriedLock) pk = _packetPool.Count > 0 ? _packetPool.Pop() : NewPacket();
                pk.Tick = tick;
                _pendingEvents.SwapBuffers();
                pk.Events.ClearCurrentBuffers();
                pk.Events.PublishCurrentOf(_pendingEvents, addMissingStreams: true);   // → the packet's PENDING side
                _pendingEvents.ClearCurrentBuffers();
                lock (_replyLock) { var r = pk.Replies; pk.Replies = _pendingReplies; _pendingReplies = r; }   // the pooled list is empty
                lock (_carriedLock) _carried.Enqueue(pk);
                Volatile.Write(ref b.Tick, tick);
            }
            finally { Volatile.Write(ref b.State, Free); }
            _latest = w;
            Interlocked.Increment(ref Published);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            LastPublishMs = ms; if (ms > MaxPublishMs) MaxPublishMs = ms;
            return true;
        }

        /// <summary>The sim thread, between ticks: the events the UI published on A since the last call are published into <paramref name="simBus"/>
        /// (pending — readable after the next swap, like any intent published this frame).</summary>
        public void TakeUplinkInto(FdpEventBus simBus)
        {
            lock (_uplinkLock)
            {
                _uplink.SwapBuffers();
                simBus.PublishCurrentOf(_uplink, addMissingStreams: true);
                _uplink.ClearCurrentBuffers();
            }
        }

        // ═══════════════ the UI thread ═══════════════

        /// <summary>
        /// The UI frame's start: if a tick newer than A's has been published, copies it into <paramref name="presentation"/> (index, masked tables,
        /// singletons except <see cref="SingletonExcept"/>, the rows), sends the events the UI published on A's bus UP, makes the carried S events
        /// readable on A's bus, and runs the carried replies on A in order. Returns true when A changed.
        /// </summary>
        public bool Pull(EntityRepository presentation)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int r = _latest;
                if (r < 0) return false;
                var b = _buf[r];
                if (Volatile.Read(ref b.Tick) <= _pulledTick) return false;
                if (Interlocked.CompareExchange(ref b.State, Reading, Free) != Free) continue;   // the sim just started on it: a newer one is coming — retry
                try
                {
                    if (b.Tick <= _pulledTick) return false;   // re-check under the claim
                    long t0 = Stopwatch.GetTimestamp();
                    presentation.SyncPresentationFrom(b.World, TableMask, SingletonExcept);
                    presentation.SyncRowsFrom(b.World, new ReadOnlySpan<Entity>(b.Rows, 0, b.RowCount), TableMask);
                    // events: A's own publishes go UP; then the carried S events become A's readable events
                    var ab = presentation.Bus;
                    ab.SwapBuffers();
                    lock (_uplinkLock) _uplink.PublishCurrentOf(ab, addMissingStreams: true);
                    ab.ClearCurrentBuffers();
                    // every packet up to this tick, in order (a skipped / never-pulled publish's included), exactly once
                    _drainScratch.Clear();
                    lock (_carriedLock) while (_carried.Count > 0 && _carried.Peek().Tick <= b.Tick) _drainScratch.Add(_carried.Dequeue());
                    for (int k = 0; k < _drainScratch.Count; k++)
                    {
                        var pk = _drainScratch[k];
                        pk.Events.SwapBuffers();
                        ab.PublishCurrentOf(pk.Events, addMissingStreams: true);
                        pk.Events.ClearCurrentBuffers();
                    }
                    ab.SwapBuffers();
                    for (int k = 0; k < _drainScratch.Count; k++)
                    {
                        var replies = _drainScratch[k].Replies;
                        for (int i = 0; i < replies.Count; i++)
                        {
                            try { replies[i](presentation); }
                            catch (Exception ex) { Console.WriteLine("[Staging] a reply threw on the presentation world: " + ex); }
                        }
                        replies.Clear();
                    }
                    lock (_carriedLock) for (int k = 0; k < _drainScratch.Count; k++) _packetPool.Push(_drainScratch[k]);
                    _drainScratch.Clear();
                    _pulledTick = b.Tick;
                    LastPullMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    WhileHoldingForTest?.Invoke();
                    Interlocked.Increment(ref Pulled);
                    return true;
                }
                finally { Volatile.Write(ref b.State, Free); }
            }
            return false;
        }

        private Packet NewPacket()
        {
            var pk = new Packet();
            if (_streamSource != null) pk.Events.CopyRegistrationsFrom(_streamSource);
            return pk;
        }

        public void Dispose()
        {
            _buf[0].World.Dispose(); _buf[1].World.Dispose();
            lock (_carriedLock) { foreach (var p in _carried) p.Events.Dispose(); foreach (var p in _packetPool) p.Events.Dispose(); }
            _pendingEvents.Dispose(); _uplink.Dispose();
        }
    }
}
