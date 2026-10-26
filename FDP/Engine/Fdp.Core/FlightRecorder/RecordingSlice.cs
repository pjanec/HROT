using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// ⭐ <c>CE-3299</c> B3 (R-3044 "collect") — copies whole frames of a recording into a NEW recording, byte for byte: the
    /// global header, then the chosen frames exactly as written (outer header + compressed payload), so the copy is read by
    /// the same replay code with no decode and no second writer. The source may still be recording (it is opened for shared
    /// read; only frames already written are walked). 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1g.
    /// </summary>
    public static class RecordingSlice
    {
        /// <summary>The global header's size: magic (6) + format version (4) + timestamp (8).</summary>
        public const int GlobalHeaderSize = 6 + 4 + 8;

        /// <summary>
        /// Writes <paramref name="count"/> frames of <paramref name="sourcePath"/>, starting at frame <paramref name="firstFrame"/>
        /// (0-based, in file order), as a recording at <paramref name="outputPath"/>. ⛔ Never overwrites a file. Throws when the
        /// source has fewer frames, or when the first one copied is not a keyframe (a recording must open with one).
        /// </summary>
        public static void CopyFrames(string sourcePath, string outputPath, int firstFrame, int count)
        {
            if (firstFrame < 0) throw new ArgumentOutOfRangeException(nameof(firstFrame));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[GlobalHeaderSize];
            source.ReadExactly(header);
            if (System.Text.Encoding.ASCII.GetString(header, 0, 6) != "FDPREC")
                throw new InvalidDataException($"'{sourcePath}' is not a recording");

            uint version = VersionOf(header, sourcePath);
            Span<byte> outerBuffer = stackalloc byte[FrameOuterHeader.Size];
            var outer = outerBuffer.Slice(0, FrameOuterHeader.SizeFor(version));
            for (int frame = 0; frame < firstFrame; frame++)
                source.Seek(SkipFrame(source, outer, version, frame, sourcePath), SeekOrigin.Current);

            // ⭐ CE-3329 P0 (row 5) — the copy is written in the CURRENT format: the recorder appends v7 frames after it, and ⛔ one
            //   file never holds two header sizes. A v6 frame's header becomes a v7 one with no sim time (FrameFlags.Unknown).
            BitConverter.TryWriteBytes(header.AsSpan(6, 4), FdpConfig.FORMAT_VERSION);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(header);
            var payload = Array.Empty<byte>();
            for (int i = 0; i < count; i++)
            {
                int size = ReadOuter(source, outer, version, firstFrame + i, sourcePath, out var head);
                if (i == 0 && head.FrameType != (byte)FrameType.Keyframe)
                    throw new InvalidDataException($"frame {firstFrame} of '{sourcePath}' is not a keyframe");
                if (payload.Length < size) payload = new byte[size];
                source.ReadExactly(payload, 0, size);
                output.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<FrameOuterHeader>(ref head)));
                output.Write(payload, 0, size);
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 — frame <paramref name="frame"/> of <paramref name="sourcePath"/>, decompressed: exactly the bytes the
        /// recorder captured (a keyframe is what <c>RecorderSystem.RecordKeyframe</c> wrote). The source may still be recording.
        /// </summary>
        public static byte[] ReadFrame(string sourcePath, int frame, out FrameType frameType)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[GlobalHeaderSize];
            source.ReadExactly(header);
            if (System.Text.Encoding.ASCII.GetString(header, 0, 6) != "FDPREC")
                throw new InvalidDataException($"'{sourcePath}' is not a recording");

            uint version = VersionOf(header, sourcePath);
            Span<byte> outerBuffer = stackalloc byte[FrameOuterHeader.Size];
            var outer = outerBuffer.Slice(0, FrameOuterHeader.SizeFor(version));
            for (int i = 0; i < frame; i++)
                source.Seek(SkipFrame(source, outer, version, i, sourcePath), SeekOrigin.Current);
            int size = ReadOuter(source, outer, version, frame, sourcePath, out var head);
            var compressed = new byte[size];
            source.ReadExactly(compressed);
            var raw = new byte[head.UncompressedSize];
            K4os.Compression.LZ4.LZ4Codec.Decode(compressed, 0, size, raw, 0, raw.Length);
            frameType = (FrameType)head.FrameType;
            return raw;
        }

        // Reads frame `frame`'s outer header; returns its payload size.
        private static int ReadOuter(FileStream source, Span<byte> outer, uint version, int frame, string path, out FrameOuterHeader head)
        {
            if (source.Read(outer) != outer.Length)
                throw new InvalidDataException($"'{path}' ends before frame {frame}");
            head = FrameOuterHeader.Read(outer, version);
            if (head.CompressedSize < 0 || source.Position + head.CompressedSize > source.Length)
                throw new InvalidDataException($"'{path}' ends inside frame {frame}");
            return head.CompressedSize;
        }

        private static long SkipFrame(FileStream source, Span<byte> outer, uint version, int frame, string path)
            => ReadOuter(source, outer, version, frame, path, out _);

        // ⭐ CE-3329 P0 — the file's format (the uint after the magic): it decides the outer header's size.
        private static uint VersionOf(byte[] header, string path)
        {
            uint version = BitConverter.ToUInt32(header, 6);
            if (version < FdpConfig.MIN_READABLE_FORMAT_VERSION || version > FdpConfig.FORMAT_VERSION)
                throw new InvalidDataException($"'{path}' is format {version}; readable: {FdpConfig.MIN_READABLE_FORMAT_VERSION}–{FdpConfig.FORMAT_VERSION}");
            return version;
        }
    }
}
