using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐ <c>CE-3299</c> B5 (restore design §1k) — <b>a checkpoint as a file</b>: a ONE-FRAME recording the flight recorder writes (one
    /// keyframe, its mark, its schema manifest) — the format <see cref="RecordingModule.ExportCheckpoint"/> already exports (R-3044: no
    /// checkpoint format outside the recorder). For a world with no recording running (the editor's dry run, where R1 is asked).
    /// </summary>
    public static class CheckpointFile
    {
        /// <summary>
        /// Writes <paramref name="world"/> as it is NOW to <paramref name="path"/> (+ its <c>.meta.json</c>) as checkpoint
        /// <paramref name="label"/>, with the clock it stands at. ⛔ Never over an existing file. Returns the mark.
        /// </summary>
        public static CheckpointMark Write(EntityRepository world, string path, string label)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("a checkpoint is named", nameof(label));
            var time = world.GetSingletonUnmanaged<GlobalTime>();
            long wall = time.TotalWallTicks != 0 ? time.TotalWallTicks : DateTime.UtcNow.Ticks;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var recorder = new AsyncRecorder(path, new RecordingMetadata(), appendAfterFrames: 0);
            RecorderTickSystem.StampWorld(world, recorder.Metadata);
            recorder.CaptureKeyframe(world, wall, blocking: true, eventBus: world.Bus, readBuffer: true);   // as RecorderTickSystem writes one (CE-4118)
            return recorder.MarkCheckpoint(label, wall, time);
        }

        /// <summary>Reads the checkpoint at <paramref name="path"/>: its keyframe and its mark (the file's only one).</summary>
        public static (WorldKeyframe Keyframe, CheckpointMark Mark) Read(string path)
        {
            var meta = MetadataSerializer.Deserialize(File.ReadAllText(path + ".meta.json"));
            var mark = meta.Checkpoints.SingleOrDefault()
                       ?? throw new InvalidDataException($"{path} names {meta.Checkpoints.Count} checkpoints — a checkpoint file names one");
            var frame = RecordingSlice.ReadFrame(path, mark.Frame, out var type);
            if (type != FrameType.Keyframe) throw new InvalidDataException($"{path} frame {mark.Frame} is not a keyframe");
            return (WorldKeyframe.FromRecordedFrame(frame), mark);
        }
    }
}
