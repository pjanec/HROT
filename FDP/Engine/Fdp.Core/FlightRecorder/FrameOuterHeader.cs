using System;
using System.Runtime.InteropServices;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// Binary contract for the outer frame header written to .fdp recording files.
    /// Pack = 1 prevents hidden padding — bytes on disk match field order exactly.
    /// Layout (format v7): [CompressedSize:4][UncompressedSize:4][Tick:8][FrameType:1][WallClockTicks:8][SimTime:8][Flags:1] = 34 bytes.
    /// Format v6 ended after WallClockTicks (25 bytes) — read with <see cref="SizeFor"/> / <see cref="Read"/>.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <c>CE-3329</c> P0 (R1, F17) — the frame's SIM time and its <see cref="FrameFlags"/> live in the index, so a replay's sim axis
    /// (paused stretches, sim seeks) is read without decoding a frame. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §5 R1, §8 rows 1–4.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FrameOuterHeader
    {
        public int CompressedSize;
        public int UncompressedSize;
        public ulong Tick;
        public byte FrameType;
        public long WallClockTicks;

        /// <summary>⭐ v7 — the frame's recorded <c>GlobalTime.TotalTime</c> (seconds of the run's own clock). NaN when unknown (a v6 frame).</summary>
        public double SimTime;

        /// <summary>⭐ v7 — <see cref="FrameFlags"/>.</summary>
        public byte Flags;

        /// <summary>
        /// Compile-time size of the CURRENT (v7) header in bytes. Replaces all magic numbers (25, 17, etc.).
        /// ⚠ A reader of a file uses <see cref="SizeFor"/> with the file's version.
        /// </summary>
        public static unsafe int Size => sizeof(FrameOuterHeader);

        /// <summary>The v6 header size: everything up to and including <see cref="WallClockTicks"/>.</summary>
        public const int SizeV6 = 25;

        /// <summary>The header size in a file of format <paramref name="formatVersion"/>.</summary>
        public static int SizeFor(uint formatVersion) => formatVersion >= 7 ? Size : SizeV6;

        /// <summary>
        /// Reads a header of a file of format <paramref name="formatVersion"/> from <paramref name="bytes"/> (at least
        /// <see cref="SizeFor"/> long). A v6 header reads as a v7 one with <see cref="SimTime"/> = NaN and <see cref="FrameFlags.Unknown"/>.
        /// </summary>
        public static FrameOuterHeader Read(ReadOnlySpan<byte> bytes, uint formatVersion)
        {
            if (formatVersion >= 7) return MemoryMarshal.Read<FrameOuterHeader>(bytes);
            Span<byte> full = stackalloc byte[Size];
            full.Clear();
            bytes.Slice(0, SizeV6).CopyTo(full);
            var head = MemoryMarshal.Read<FrameOuterHeader>(full);
            head.SimTime = double.NaN;
            head.Flags = (byte)FrameFlags.Unknown;
            return head;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> P0 — what a frame says about the clock and the world, written by the recorder into the v7 frame index.
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §4, §5 R1, §12f-P0.
    /// </summary>
    [Flags]
    public enum FrameFlags : byte
    {
        None = 0,

        /// <summary>The clock did not advance on this frame (<c>GlobalTime.IsHalted</c>, R-126 — the clock's own word, never a wall gap).
        /// ⚠ An operator pause and a gap between steps both read Halted on a node; the time map tells them apart (D-H).</summary>
        Halted = 1,

        /// <summary>The frame carries a change besides the clock: an entity chunk, a destruction or an event (singletons excluded).
        /// A keyframe always reads Changed.</summary>
        Changed = 2,

        /// <summary>The frame was written in format v6: nothing is known about it (decode the frame to learn its sim time).</summary>
        Unknown = 0x80,
    }
}
