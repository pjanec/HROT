using System;
using System.IO;
using System.Threading;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// ⭐ <c>CE-1478 / CE-4407 P1</c> — one recorded frame, read and LZ4-decoded AHEAD of the apply: the raw bytes a <see cref="PlaybackSystem"/> reads,
    /// plus the frame's index entry. Pooled by <see cref="FramePrefetcher"/>: <see cref="Raw"/> is grown to the largest frame seen and never shrunk.
    /// </summary>
    public sealed class PreparedFrame
    {
        internal enum SlotState { Free, Filling, Ready, Taken }

        /// <summary>The decoded frame; only the first <see cref="RawSize"/> bytes are the frame.</summary>
        public byte[] Raw = Array.Empty<byte>();
        public int RawSize;
        /// <summary>Which recorded frame this holds.</summary>
        public int Index = -1;
        /// <summary>The frame's header (index entry).</summary>
        public FrameMetadata Header;

        internal SlotState State;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1478 / CE-4407 P1</c> (docs/DESIGN_Replay_Playback_Performance.md §2) — a background thread that reads and LZ4-decodes the NEXT recorded
    /// frames so the main loop only ever MEMCPYS them into the world: a frame of a 1 000-man recording is ~58 MB raw, and read + decode was 12 ms (56 max) of
    /// the main thread per applied frame. Mirrors the recorder's own precedent (<see cref="AsyncRecorder"/>: pooled buffers + an LZ4 worker).
    /// </summary>
    /// <remarks>
    /// ⛔ Never touches the world. It owns a SECOND read handle on the file (the controller's stream is the main thread's), so no stream position is shared.
    /// Depth = number of pooled frames: the window is <c>[first, first + depth)</c>; <see cref="Want"/> re-aims it (a seek / reverse step simply moves the window
    /// and what no longer falls inside is recycled). <see cref="TryTake"/> never waits: a frame not ready is the caller's inline read (cold start, seek).
    /// </remarks>
    public sealed class FramePrefetcher : IDisposable
    {
        private readonly string _filePath;
        private readonly FrameMetadata[] _frames;
        private readonly int _headerSize;
        private readonly PreparedFrame[] _slots;
        private readonly object _gate = new();
        private Thread? _thread;
        private bool _stop;
        private int _first;                       // the window start, under _gate
        private FileStream? _stream;              // the worker's own handle
        private byte[] _compressed = Array.Empty<byte>();   // the worker's scratch

        /// <summary>How many frames the window holds.</summary>
        public int Depth => _slots.Length;

        /// <summary>Frames the worker finished decoding (diagnostic).</summary>
        public int FramesPrepared => Volatile.Read(ref _prepared);
        private int _prepared;

        /// <param name="filePath">the recording</param>
        /// <param name="frames">the frame index (offsets), a COPY — immutable for the prefetcher's life</param>
        /// <param name="formatVersion">the file's format version (sizes the outer header)</param>
        /// <param name="depth">frames ahead, ≥ 1</param>
        public FramePrefetcher(string filePath, FrameMetadata[] frames, uint formatVersion, int depth)
        {
            if (depth < 1) throw new ArgumentOutOfRangeException(nameof(depth));
            _filePath = filePath;
            _frames = frames;
            _headerSize = FrameOuterHeader.SizeFor(formatVersion);
            _slots = new PreparedFrame[depth];
            for (int i = 0; i < depth; i++) _slots[i] = new PreparedFrame();
        }

        /// <summary>Aims the window at <c>[first, first + depth)</c>; starts the worker on the first call. Main thread; no allocation after the first.</summary>
        public void Want(int first)
        {
            lock (_gate)
            {
                if (_stop) return;
                _first = first;
                int end = first + _slots.Length;
                for (int i = 0; i < _slots.Length; i++)
                {
                    var s = _slots[i];
                    if (s.State == PreparedFrame.SlotState.Ready && (s.Index < first || s.Index >= end))
                    {
                        s.State = PreparedFrame.SlotState.Free;
                        s.Index = -1;
                    }
                }
                if (_thread == null)
                {
                    _thread = new Thread(Run) { IsBackground = true, Name = "FramePrefetcher" };
                    _thread.Start();
                }
                Monitor.PulseAll(_gate);
            }
        }

        /// <summary>The prepared frame <paramref name="index"/> if the worker has finished it, else false (never waits). The caller MUST <see cref="Release"/> it.</summary>
        public bool TryTake(int index, out PreparedFrame frame)
        {
            lock (_gate)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    var s = _slots[i];
                    if (s.State == PreparedFrame.SlotState.Ready && s.Index == index)
                    {
                        s.State = PreparedFrame.SlotState.Taken;
                        frame = s;
                        return true;
                    }
                }
            }
            frame = null!;
            return false;
        }

        /// <summary>Is frame <paramref name="index"/> decoded and waiting (a diagnostic / test hook).</summary>
        internal bool IsReady(int index)
        {
            lock (_gate)
            {
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i].State == PreparedFrame.SlotState.Ready && _slots[i].Index == index) return true;
                return false;
            }
        }

        /// <summary>Hands a taken frame's buffer back to the pool.</summary>
        public void Release(PreparedFrame frame)
        {
            lock (_gate)
            {
                frame.State = PreparedFrame.SlotState.Free;
                frame.Index = -1;
                Monitor.PulseAll(_gate);
            }
        }

        private void Run()
        {
            try
            {
                _stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch
            {
                return;   // the inline path serves every frame (and reports the real error)
            }

            while (true)
            {
                PreparedFrame? slot = null;
                lock (_gate)
                {
                    while (!_stop)
                    {
                        slot = PickNext();
                        if (slot != null) break;
                        Monitor.Wait(_gate);
                    }
                    if (_stop) return;
                }

                bool ok = Fill(slot!);

                lock (_gate)
                {
                    if (ok && !_stop && slot!.Index >= _first && slot.Index < _first + _slots.Length)
                    {
                        slot.State = PreparedFrame.SlotState.Ready;
                        Interlocked.Increment(ref _prepared);
                    }
                    else
                    {
                        slot!.State = PreparedFrame.SlotState.Free;
                        slot.Index = -1;
                        if (!ok) { Monitor.Wait(_gate, 50); }   // a failing frame is not retried in a hot loop
                    }
                }
            }
        }

        // under _gate: the lowest wanted frame no slot holds yet, claimed into a free slot (null = nothing to do)
        private PreparedFrame? PickNext()
        {
            int end = Math.Min(_first + _slots.Length, _frames.Length);
            for (int idx = Math.Max(_first, 0); idx < end; idx++)
            {
                bool held = false;
                PreparedFrame? free = null;
                for (int i = 0; i < _slots.Length; i++)
                {
                    var s = _slots[i];
                    if (s.State == PreparedFrame.SlotState.Free) free ??= s;
                    else if (s.Index == idx) { held = true; break; }
                }
                if (held) continue;
                if (free == null) return null;   // every slot is busy: wait for a Release / a re-aim
                free.State = PreparedFrame.SlotState.Filling;
                free.Index = idx;
                return free;
            }
            return null;
        }

        private bool Fill(PreparedFrame slot)
        {
            try
            {
                var meta = _frames[slot.Index];
                int compressedSize = meta.CompressedSize, rawSize = meta.UncompressedSize;
                var stream = _stream!;
                stream.Position = meta.FilePosition + _headerSize;
                if (_compressed.Length < compressedSize) _compressed = new byte[compressedSize];
                int got = 0;
                while (got < compressedSize)
                {
                    int n = stream.Read(_compressed, got, compressedSize - got);
                    if (n <= 0) return false;
                    got += n;
                }
                if (slot.Raw.Length < rawSize) slot.Raw = new byte[rawSize];
                K4os.Compression.LZ4.LZ4Codec.Decode(_compressed, 0, compressedSize, slot.Raw, 0, rawSize);
                slot.RawSize = rawSize;
                slot.Header = meta;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            Thread? t;
            lock (_gate)
            {
                _stop = true;
                t = _thread;
                Monitor.PulseAll(_gate);
            }
            t?.Join();
            _stream?.Dispose();
        }
    }
}
