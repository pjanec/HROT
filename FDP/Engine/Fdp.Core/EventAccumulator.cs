using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core.Logging;

namespace Fdp.Core
{
    /// <summary>
    /// The live bus's event history, kept for every reader that does not read every frame: a background module's snapshot
    /// (flushed at its dispatch) and — CE-3293 — a step-only system's STEP WINDOW (every event since that system's last run).
    ///
    /// <para>⭐⭐ CE-3293 S4 (R-3036 rules 5, 6, 7, 9; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §1, §1a):</para>
    /// <list type="bullet">
    /// <item>a fixed RING of slots, created once; each event stream keeps one buffer per slot (<see cref="EventStreamHistory"/>),
    /// grown only to a new high-water mark — 0 bytes per frame in steady state, growth counted (<see cref="Growth"/>);</item>
    /// <item>an EMPTY frame takes no slot, and consecutive frames share one while no reader has read since it began (a pause:
    /// every step-only reader is held at the last step) — so a long pause costs a few slots, not one per frame;</item>
    /// <item>a slot is kept until its LAST READER has had it (every registered reader); with no reader registered, until the cap;</item>
    /// <item>what the cap drops from a reader that still needed it is counted (<see cref="Overflow"/>) and that reader EXPIRES —
    /// counted and logged once — until it reads again (a stopped module no longer pins the history);</item>
    /// <item><see cref="Restart"/> (the one time discontinuity, <c>SnapAndPause</c>) forgets everything before it.</item>
    /// </list>
    ///
    /// <para>Keys are the caller's frame numbers (the kernel passes the live world's <c>GlobalVersion</c>); they must increase.
    /// A key that does not is a new timeline: the history restarts and every reader starts over at 0.</para>
    /// </summary>
    public sealed class EventAccumulator
    {
        /// <summary>The default slot capacity: non-empty frames (or merged runs of them) a slow reader may fall behind by.</summary>
        public const int DefaultCapacity = 256;

        /// <summary>The default bound on events held in all slots together — what a reader that stopped reading can cost.</summary>
        public const long DefaultMaxRetainedEvents = 1 << 16;

        private const byte ReaderFree = 0, ReaderActive = 1, ReaderExpired = 2;

        private readonly object _lock = new();
        private readonly int _capacity;
        private readonly long _maxRetained;
        private readonly long _mergeLimit;   // a merged run stops absorbing frames at this many events
        private long _retained;

        private FdpEventBus? _bus;
        private NativeStreamHistory[] _native = new NativeStreamHistory[16];
        private int _nativeKnown;
        private ManagedStreamHistory[] _managed = new ManagedStreamHistory[16];
        private int _managedKnown;
        private readonly Dictionary<int, NativeStreamHistory> _nativeById = new();
        private readonly Dictionary<int, ManagedStreamHistory> _managedById = new();

        // the ring: LOGICAL order of slots (_ring[(_head + k) % _capacity]) over PHYSICAL slot ids reused from a free stack —
        // most recently freed first, so the slots in use stay the few whose buffers are already grown (a strict ring would walk
        // all _capacity positions and grow each one once)
        private readonly int[] _ring;
        private readonly int[] _free;
        private int _freeCount;
        private readonly uint[] _first;
        private readonly uint[] _last;
        private readonly long[] _events;
        private readonly EventStreamHistory[][] _touched;
        private readonly int[] _touchedCount;
        private int _head;
        private int _count;

        private uint _currentKey;
        private uint _previousKey;
        private uint _timelineStart;   // the key the current timeline began after (a Restart's current key; 0 for a new one)
        private bool _any;
        private long _stamp;

        // readers
        private uint[] _seen = new uint[32];
        private byte[] _state = new byte[32];
        private int _readerSlots;

        /// <param name="maxHistoryFrames">
        /// The slot capacity — ⚠ CE-3293 S4: counted in NON-EMPTY slots (an empty frame takes none; a pause merges), no longer
        /// in frames.
        /// </param>
        /// <param name="maxRetainedEvents">The bound on events held in all slots together; past it the oldest slots go (counted).</param>
        public EventAccumulator(int maxHistoryFrames = DefaultCapacity, long maxRetainedEvents = DefaultMaxRetainedEvents)
        {
            if (maxHistoryFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maxHistoryFrames));
            if (maxRetainedEvents <= 0) throw new ArgumentOutOfRangeException(nameof(maxRetainedEvents));
            _capacity     = maxHistoryFrames;
            _maxRetained  = maxRetainedEvents;
            _mergeLimit   = Math.Max(1, maxRetainedEvents / 8);
            _first        = new uint[_capacity];
            _last         = new uint[_capacity];
            _events       = new long[_capacity];
            _touched      = new EventStreamHistory[_capacity][];
            _touchedCount = new int[_capacity];
            for (int i = 0; i < _capacity; i++) _touched[i] = new EventStreamHistory[8];
            _ring = new int[_capacity];
            _free = new int[_capacity];
            for (int i = 0; i < _capacity; i++) _free[i] = _capacity - 1 - i;
            _freeCount = _capacity;
        }

        /// <summary>Events the cap dropped before a reader that still needed them had read them.</summary>
        public long Overflow { get; private set; }

        /// <summary>How many times a reader expired (fell a whole ring behind).</summary>
        public long ExpiredReaders { get; private set; }

        /// <summary>How many times a history buffer grew (a new high-water mark) — the only allocations of the hot path.</summary>
        public long Growth { get; private set; }

        /// <summary>Slots in use.</summary>
        public int SlotsInUse { get { lock (_lock) return _count; } }

        /// <summary>The key of the latest capture — the frame the bus's READ buffer holds.</summary>
        public uint CurrentKey { get { lock (_lock) return _currentKey; } }

        // ───────────────────────────── capture ─────────────────────────────

        /// <summary>
        /// Captures the bus's READ buffer (call right after <see cref="FdpEventBus.SwapBuffers"/>) as frame
        /// <paramref name="frameIndex"/>. Zero allocation in steady state.
        /// </summary>
        public void CaptureFrame(FdpEventBus liveBus, uint frameIndex)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_bus, liveBus)) Bind(liveBus);
                if (_any && frameIndex <= _currentKey) RestartTimeline();
                Discover();

                int slot = -1;
                for (int i = 0; i < _nativeKnown; i++) slot = CaptureStream(_native[i], slot, frameIndex);
                for (int i = 0; i < _managedKnown; i++) slot = CaptureStream(_managed[i], slot, frameIndex);

                _previousKey = _any ? _currentKey : frameIndex - 1;
                _currentKey  = frameIndex;
                _any         = true;
                _stamp++;
                ReleaseRead();
                while (_retained > _maxRetained && _count > 1) DropOldest();
            }
        }

        private int CaptureStream(EventStreamHistory history, int slot, uint key)
        {
            int count = history.CurrentCount;
            if (count == 0) return slot;
            if (slot < 0) slot = OpenSlot(key);
            if (!history.InSlot[slot])
            {
                history.InSlot[slot] = true;
                ref var touched = ref _touched[slot];
                if (_touchedCount[slot] == touched.Length) { Array.Resize(ref touched, touched.Length * 2); Growth++; }
                touched[_touchedCount[slot]++] = history;
            }
            if (history.Capture(slot)) Growth++;
            _events[slot] += count;
            _retained     += count;
            return slot;
        }

        private int OpenSlot(uint key)
        {
            if (_count > 0)
            {
                int tail = _ring[(_head + _count - 1) % _capacity];
                if (_events[tail] < _mergeLimit && CanMerge(_first[tail])) { _last[tail] = key; return tail; }
            }
            if (_count == _capacity) DropOldest();
            int idx = _free[--_freeCount];
            _ring[(_head + _count) % _capacity] = idx;
            _count++;
            _first[idx] = key;
            _last[idx]  = key;
            _events[idx] = 0;
            return idx;
        }

        // A run of frames may share a slot only while no reader's position falls inside it — every active reader has read
        // nothing since it began (a pause). With no reader registered nothing merges: a stateless flush may ask any key.
        private bool CanMerge(uint tailFirst)
        {
            bool anyActive = false;
            for (int r = 0; r < _readerSlots; r++)
            {
                if (_state[r] != ReaderActive) continue;
                anyActive = true;
                if (_seen[r] >= tailFirst) return false;
            }
            return anyActive;
        }

        private void DropOldest()
        {
            int idx = _ring[_head];
            bool needed = false;
            for (int r = 0; r < _readerSlots; r++)
            {
                if (_state[r] != ReaderActive || _seen[r] >= _last[idx]) continue;
                needed = true;
                _state[r] = ReaderExpired;
                ExpiredReaders++;
                FdpLog<EventAccumulator>.Warn(
                    "[EventAccumulator] reader {0} fell behind the history's bound ({1} slots / {2} events) and EXPIRED — events "
                  + "up to frame {3} were dropped before it read them; it resumes from what is left when it next reads.",
                    r, _capacity, _maxRetained, _last[idx]);
            }
            if (needed) Overflow += _events[idx];
            FreeOldest();
        }

        private void FreeOldest()
        {
            int idx = _ring[_head];
            var touched = _touched[idx];
            for (int i = 0; i < _touchedCount[idx]; i++)
            {
                touched[i].ClearSlot(idx);
                touched[i].InSlot[idx] = false;
                touched[i] = null!;
            }
            _touchedCount[idx] = 0;
            _retained -= _events[idx];
            _events[idx] = 0;
            _free[_freeCount++] = idx;
            _head = (_head + 1) % _capacity;
            _count--;
        }

        // Frees every slot all active readers have read. With none registered the history runs to the cap.
        private void ReleaseRead()
        {
            bool anyActive = false;
            uint min = uint.MaxValue;
            for (int r = 0; r < _readerSlots; r++)
            {
                if (_state[r] != ReaderActive) continue;
                anyActive = true;
                if (_seen[r] < min) min = _seen[r];
            }
            if (!anyActive) return;
            while (_count > 0 && _last[_ring[_head]] <= min) FreeOldest();
        }

        private void Bind(FdpEventBus bus)
        {
            while (_count > 0) FreeOldest();
            _bus = bus;
            _nativeKnown = _managedKnown = 0;
            _nativeById.Clear();
            _managedById.Clear();
        }

        private void Discover()
        {
            var bus = _bus!;
            int natives = bus.NativeStreamCount;
            while (_nativeKnown < natives)
            {
                var stream = bus.NativeStreamAt(_nativeKnown);
                var history = new NativeStreamHistory(stream, _capacity);
                if (_nativeKnown == _native.Length) Array.Resize(ref _native, _native.Length * 2);
                _native[_nativeKnown++] = history;
                _nativeById[stream.EventTypeId] = history;
                Growth++;
            }
            int manageds = bus.ManagedStreamCount;
            while (_managedKnown < manageds)
            {
                var stream = bus.ManagedStreamAt(_managedKnown);
                var history = stream.CreateHistory(_capacity);
                if (_managedKnown == _managed.Length) Array.Resize(ref _managed, _managed.Length * 2);
                _managed[_managedKnown++] = history;
                _managedById[stream.TypeId] = history;
                Growth++;
            }
        }

        // ───────────────────────────── restart ─────────────────────────────

        /// <summary>
        /// ⭐ Rule 7 — the time discontinuity (<c>SnapAndPause</c>: a load, a preview Stop, a seek). Everything captured so far
        /// is forgotten and every reader's window restarts at the latest capture: nothing from before it is delivered after it.
        /// </summary>
        public void Restart()
        {
            lock (_lock)
            {
                while (_count > 0) FreeOldest();
                for (int r = 0; r < _readerSlots; r++)
                {
                    if (_state[r] == ReaderFree) continue;
                    _seen[r]  = _currentKey;
                    _state[r] = ReaderActive;
                }
                _previousKey   = _currentKey;
                _timelineStart = _currentKey;
                _stamp++;
            }
        }

        // keys went back: a new timeline numbered from scratch — every reader starts over at 0
        private void RestartTimeline()
        {
            while (_count > 0) FreeOldest();
            for (int r = 0; r < _readerSlots; r++)
            {
                if (_state[r] == ReaderFree) continue;
                _seen[r]  = 0;
                _state[r] = ReaderActive;
            }
            _any = false;
            _currentKey = _previousKey = _timelineStart = 0;
        }

        // ───────────────────────────── readers ─────────────────────────────

        /// <summary>
        /// Registers a reader that has seen everything up to <paramref name="lastSeen"/> (0 = everything the history still
        /// holds). The history keeps every event until each registered reader has had it.
        /// </summary>
        public int RegisterReader(uint lastSeen = 0)
        {
            lock (_lock)
            {
                int r = 0;
                while (r < _readerSlots && _state[r] != ReaderFree) r++;
                if (r == _readerSlots)
                {
                    if (_readerSlots == _seen.Length)
                    {
                        Array.Resize(ref _seen, _seen.Length * 2);
                        Array.Resize(ref _state, _state.Length * 2);
                    }
                    _readerSlots++;
                }
                _seen[r]  = lastSeen;
                _state[r] = ReaderActive;
                return r;
            }
        }

        /// <summary>
        /// Registers a reader whose first read is the CURRENT frame only (a step-only system seen for the first time: it has
        /// nothing to catch up on).
        /// </summary>
        public int RegisterReaderAtCurrentFrame()
        {
            lock (_lock) return RegisterReader(_any ? _previousKey : 0);
        }

        /// <summary>
        /// ⭐ CE-3293 — registers a reader that is owed every event of the CURRENT TIMELINE (since the last
        /// <see cref="Restart"/>, or all of it): a step-only system. Registered the same way on every run of a scenario, so
        /// what its first step reads does not depend on whether it ran before (a reload) or not (the first run after boot).
        /// </summary>
        public int RegisterReaderAtTimelineStart()
        {
            lock (_lock) return RegisterReader(_timelineStart);
        }

        /// <summary>Unregisters a reader (a removed module); it no longer holds the history.</summary>
        public void UnregisterReader(int reader)
        {
            lock (_lock)
            {
                if (reader < 0 || reader >= _readerSlots) return;
                _state[reader] = ReaderFree;
                ReleaseRead();
            }
        }

        /// <summary>The last frame <paramref name="reader"/> has had.</summary>
        public uint LastSeen(int reader) { lock (_lock) return _seen[reader]; }

        /// <summary>Records that <paramref name="reader"/> has had everything up to the current frame.</summary>
        public void MarkRead(int reader)
        {
            lock (_lock)
            {
                _seen[reader]  = _currentKey;
                _state[reader] = ReaderActive;
            }
        }

        /// <summary>
        /// True when <paramref name="reader"/>'s window is only the current frame — it read on the previous capture (or
        /// nothing was captured): its read IS the bus's read buffer, zero copy.
        /// </summary>
        public bool WindowIsCurrentFrame(int reader)
        {
            lock (_lock) return !_any || _seen[reader] >= _previousKey;
        }

        // ───────────────────────────── the step window ─────────────────────────────

        /// <summary>
        /// Every native event of <typeparamref name="T"/> since <paramref name="reader"/> last read, oldest first. Reused buffer:
        /// valid until the next capture. Call only when <see cref="WindowIsCurrentFrame"/> is false.
        /// </summary>
        public ReadOnlySpan<T> ReadWindow<T>(int reader) where T : unmanaged
        {
            var bytes = ReadRawWindow(reader, EventType<T>.Id, out _);
            return MemoryMarshal.Cast<byte, T>(bytes);
        }

        /// <summary>The raw-bytes form of <see cref="ReadWindow{T}"/>, by type id (the blueprint dispatch).</summary>
        public ReadOnlySpan<byte> ReadRawWindow(int reader, int typeId, out int elementSize)
        {
            lock (_lock)
            {
                if (!_nativeById.TryGetValue(typeId, out var history)) { elementSize = 0; return ReadOnlySpan<byte>.Empty; }
                elementSize = history.ElementSize;
                FillWindow(history, _seen[reader]);
                return history.Window;
            }
        }

        /// <summary>The managed twin of <see cref="ReadWindow{T}"/>.</summary>
        public IReadOnlyList<T> ReadManagedWindow<T>(int reader)
        {
            lock (_lock)
            {
                if (!_managedById.TryGetValue(FdpEventBus.ManagedTypeId<T>.Id, out var history)
                    || history is not ManagedStreamHistory<T> typed)
                    return Array.Empty<T>();
                FillWindow(typed, _seen[reader]);
                return typed.Window;
            }
        }

        /// <summary>Is there any event of the type id in <paramref name="reader"/>'s window?</summary>
        public bool HasInWindow(int reader, int typeId, bool native)
        {
            lock (_lock)
            {
                EventStreamHistory? history = native
                    ? (_nativeById.TryGetValue(typeId, out var n) ? n : null)
                    : (_managedById.TryGetValue(typeId, out var m) ? m : null);
                if (history == null) return false;
                uint from = _seen[reader];
                for (int k = 0; k < _count; k++)
                {
                    int idx = _ring[(_head + k) % _capacity];
                    if (_last[idx] > from && history.InSlot[idx]) return true;
                }
                return false;
            }
        }

        private void FillWindow(EventStreamHistory history, uint from)
        {
            if (history.WindowStamp == _stamp && history.WindowFrom == from && history.WindowTo == _currentKey) return;
            history.BeginWindow();
            for (int k = 0; k < _count; k++)
            {
                int idx = _ring[(_head + k) % _capacity];
                if (_last[idx] <= from || !history.InSlot[idx]) continue;
                if (history.AppendToWindow(idx)) Growth++;
            }
            history.WindowFrom  = from;
            history.WindowTo    = _currentKey;
            history.WindowStamp = _stamp;
        }

        // ───────────────────────────── flush to a background module ─────────────────────────────

        /// <summary>
        /// Appends every event <paramref name="reader"/> has not had to <paramref name="replicaBus"/>'s READ buffers, then
        /// marks it read. The reader form: it holds the history until it has flushed. (Not an overload of
        /// <see cref="FlushToReplica(FdpEventBus, uint)"/> on purpose: an integer literal would bind to the wrong one.)
        /// </summary>
        public void FlushUnreadTo(FdpEventBus replicaBus, int reader)
        {
            lock (_lock)
            {
                Inject(replicaBus, _seen[reader]);
                _seen[reader]  = _currentKey;
                _state[reader] = ReaderActive;
            }
        }

        /// <summary>
        /// Appends every event captured AFTER <paramref name="lastSeenTick"/> to <paramref name="replicaBus"/>'s READ buffers.
        /// Stateless: nothing holds the history for this caller — use a registered reader for that.
        /// </summary>
        public void FlushToReplica(FdpEventBus replicaBus, uint lastSeenTick)
        {
            lock (_lock) Inject(replicaBus, lastSeenTick);
        }

        private void Inject(FdpEventBus replicaBus, uint from)
        {
            for (int k = 0; k < _count; k++)
            {
                int idx = _ring[(_head + k) % _capacity];
                if (_last[idx] <= from) continue;
                var touched = _touched[idx];
                for (int i = 0; i < _touchedCount[idx]; i++) touched[i].InjectSlot(idx, replicaBus);
            }
        }
    }
}
