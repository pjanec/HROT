using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ CE-3293 S4 (R-3036 rule 9; docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §1a) — one event stream's history,
    /// held by the <see cref="EventAccumulator"/>: a buffer per slot of its ring (created once, grown only to a new high-water
    /// mark, never freed), plus one reused WINDOW buffer that concatenates the slots a reader is owed. Captured and replayed by
    /// a virtual call — no iterator, no <c>object</c>, no box, no reflection.
    /// </summary>
    public abstract class EventStreamHistory
    {
        internal readonly bool[] InSlot;

        // the window cache, per LANE: (from, to] as of the accumulator's capture stamp — several readers of one window share it.
        // ⭐ N-11 (CE-4473) — lane 0 serves serial code; during a per-entity phase every batch thread reads through its own lane
        //   (EventAccumulator.CurrentLane), so one thread refilling a window never rewrites the span another is reading
        internal uint[] WindowFrom = new uint[1];
        internal uint[] WindowTo = new uint[1];
        internal long[] WindowStamp = { -1 };

        private protected EventStreamHistory(int slotCapacity) => InSlot = new bool[slotCapacity];

        /// <summary>Makes room for <paramref name="lane"/> (under the accumulator's lock; grown, never shrunk).</summary>
        internal void EnsureLane(int lane)
        {
            if (lane < WindowStamp.Length) return;
            int n = Math.Max(lane + 1, WindowStamp.Length * 2);
            Array.Resize(ref WindowFrom, n);
            Array.Resize(ref WindowTo, n);
            int old = WindowStamp.Length;
            Array.Resize(ref WindowStamp, n);
            for (int i = old; i < n; i++) WindowStamp[i] = -1;
            GrowLanes(n);
        }

        private protected abstract void GrowLanes(int lanes);

        /// <summary>The bus type id of the stream.</summary>
        internal abstract int TypeId { get; }

        /// <summary>Appends the stream's READ buffer to <paramref name="slot"/>; true when a buffer had to grow.</summary>
        internal abstract bool Capture(int slot);

        /// <summary>How many events the stream's READ buffer holds right now.</summary>
        internal abstract int CurrentCount { get; }

        /// <summary>Empties <paramref name="slot"/>, keeping its buffer.</summary>
        internal abstract void ClearSlot(int slot);

        /// <summary>Appends <paramref name="slot"/>'s events to <paramref name="replica"/>'s READ buffer of the same type.</summary>
        internal abstract void InjectSlot(int slot, FdpEventBus replica);

        /// <summary>Starts a new window on <paramref name="lane"/> (empties its window buffer).</summary>
        internal abstract void BeginWindow(int lane);

        /// <summary>Appends <paramref name="slot"/> to <paramref name="lane"/>'s window; true when the window buffer had to grow.</summary>
        internal abstract bool AppendToWindow(int lane, int slot);
    }

    /// <summary>A native (unmanaged) stream's history, as bytes — it serves typed and untyped (replayed) streams alike.</summary>
    internal sealed class NativeStreamHistory : EventStreamHistory
    {
        private readonly INativeEventStream _stream;
        private readonly byte[]?[] _slots;
        private readonly int[] _lengths;
        private byte[]?[] _window = new byte[1][];
        private int[] _windowLength = new int[1];

        public NativeStreamHistory(INativeEventStream stream, int slotCapacity) : base(slotCapacity)
        {
            _stream = stream;
            _slots = new byte[slotCapacity][];
            _lengths = new int[slotCapacity];
        }

        internal override int TypeId => _stream.EventTypeId;
        internal int ElementSize => _stream.ElementSize;
        internal override int CurrentCount => _stream.ElementSize == 0 ? 0 : _stream.GetRawBytes().Length / _stream.ElementSize;

        internal ReadOnlySpan<byte> Window(int lane) => new(_window[lane], 0, _windowLength[lane]);

        private protected override void GrowLanes(int lanes) { Array.Resize(ref _window, lanes); Array.Resize(ref _windowLength, lanes); }

        internal override bool Capture(int slot)
        {
            var src = _stream.GetRawBytes();
            bool grew = Ensure(ref _slots[slot], _lengths[slot] + src.Length, _lengths[slot]);
            src.CopyTo(_slots[slot].AsSpan(_lengths[slot]));
            _lengths[slot] += src.Length;
            return grew;
        }

        internal override void ClearSlot(int slot) => _lengths[slot] = 0;

        internal override void InjectSlot(int slot, FdpEventBus replica)
        {
            if (_lengths[slot] == 0) return;
            replica.InjectIntoCurrentBySize(_stream.EventTypeId, _stream.ElementSize, new ReadOnlySpan<byte>(_slots[slot], 0, _lengths[slot]));
        }

        internal override void BeginWindow(int lane) => _windowLength[lane] = 0;

        internal override bool AppendToWindow(int lane, int slot)
        {
            int len = _lengths[slot];
            if (len == 0) return false;
            byte[]? window = _window[lane];
            bool grew = Ensure(ref window, _windowLength[lane] + len, _windowLength[lane]);
            _window[lane] = window!;
            new ReadOnlySpan<byte>(_slots[slot], 0, len).CopyTo(window!.AsSpan(_windowLength[lane]));
            _windowLength[lane] += len;
            return grew;
        }

        private static bool Ensure(ref byte[]? buffer, int needed, int keep)
        {
            if (buffer != null && buffer.Length >= needed) return false;
            var grown = new byte[Math.Max(needed, Math.Max(64, (buffer?.Length ?? 0) * 2))];
            if (buffer != null && keep > 0) Buffer.BlockCopy(buffer, 0, grown, 0, keep);
            buffer = grown;
            return true;
        }
    }

    /// <summary>A managed stream's history — public only because <see cref="IManagedEventStream.CreateHistory"/> returns it.</summary>
    public abstract class ManagedStreamHistory : EventStreamHistory
    {
        private protected ManagedStreamHistory(int slotCapacity) : base(slotCapacity) { }
    }

    /// <summary>A managed stream's history, typed: a <see cref="List{T}"/> per slot, so a managed struct is never boxed.</summary>
    internal sealed class ManagedStreamHistory<T> : ManagedStreamHistory
    {
        private readonly ManagedEventStream<T> _stream;
        private readonly List<T>?[] _slots;
        private List<T>?[] _window = { new() };

        public ManagedStreamHistory(ManagedEventStream<T> stream, int slotCapacity) : base(slotCapacity)
        {
            _stream = stream;
            _slots = new List<T>[slotCapacity];
        }

        internal override int TypeId => FdpEventBus.ManagedTypeId<T>.Id;
        internal override int CurrentCount => _stream.ReadCount;

        internal IReadOnlyList<T> Window(int lane) => _window[lane]!;

        private protected override void GrowLanes(int lanes)
        {
            int old = _window.Length;
            Array.Resize(ref _window, lanes);
            for (int i = old; i < lanes; i++) _window[i] = new List<T>();   // once per new lane (a new batch thread), never per frame
        }

        internal override bool Capture(int slot)
        {
            var src = (List<T>)_stream.Read();
            var list = _slots[slot];
            bool grew = list == null;
            list ??= _slots[slot] = new List<T>(Math.Max(4, src.Count));
            grew |= list.Capacity < list.Count + src.Count;
            list.AddRange(src);
            return grew;
        }

        internal override void ClearSlot(int slot) => _slots[slot]?.Clear();

        internal override void InjectSlot(int slot, FdpEventBus replica)
        {
            var list = _slots[slot];
            if (list != null && list.Count > 0) replica.InjectManagedIntoCurrent(list);
        }

        internal override void BeginWindow(int lane) => _window[lane]!.Clear();

        internal override bool AppendToWindow(int lane, int slot)
        {
            var list = _slots[slot];
            if (list == null || list.Count == 0) return false;
            var window = _window[lane]!;
            bool grew = window.Capacity < window.Count + list.Count;
            window.AddRange(list);
            return grew;
        }
    }
}
