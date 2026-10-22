using System;
using System.Buffers.Binary;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐ A whole world in RAM — exactly what the flight recorder writes as a keyframe (R-3040: ONE format, the recorder's).
    /// </summary>
    public sealed class WorldKeyframe
    {
        internal WorldKeyframe(byte[] bytes, uint tick) { Bytes = bytes; Tick = tick; }

        /// <summary>The keyframe as <see cref="RecorderSystem.RecordKeyframe"/> wrote it.</summary>
        public byte[] Bytes { get; }

        /// <summary>The world's <see cref="EntityRepository.GlobalVersion"/> when it was captured.</summary>
        public uint Tick { get; }

        public int SizeBytes => Bytes.Length;
    }

    /// <summary>
    /// ⭐ <c>D2</c>/<c>D12</c> — what a keyframe apply puts back besides the components.
    /// </summary>
    /// <param name="RestoreEvents">Re-inject the keyframe's recorded events. ⛔ Off for a dry-run exit (D2): they were
    /// processed before the capture, and processing them again would act twice.</param>
    public sealed record RestoreOptions(bool RestoreEvents = false)
    {
        /// <summary>The dry-run exit (D2: no events).</summary>
        public static readonly RestoreOptions DryRunExit = new();
    }

    /// <summary>
    /// ⭐⭐ <c>B1</c> (restore design §3) — <b>capture a world as a keyframe in RAM, and put it back in place.</b> A thin facade
    /// over the flight recorder's own writer and reader (<see cref="RecorderSystem.RecordKeyframe"/>,
    /// <see cref="PlaybackSystem.ApplyFrame"/>): ⛔ no second format, no second copy mechanism (R-3040, R-3044).
    /// <para>📐 Measured (§6a M2/M3, Debug, uc-urban-battle): 4–8 MiB and 19–31 ms to capture per host, 36 ms to apply.</para>
    /// <para>⚠ It carries what the recorder records — the components and singletons whose <c>DataPolicy</c> lacks
    /// <c>NoReplay</c> (D1, D7). Singletons are never cleared by an apply, so a content singleton that is not recorded stays
    /// loaded (R-3043).</para>
    /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §3, D2, D8.
    /// </summary>
    public static class WorldKeyframes
    {
        /// <summary>Captures <paramref name="repo"/> as a keyframe (no events — a world, not a moment's traffic).</summary>
        public static WorldKeyframe Capture(EntityRepository repo, long wallClockTicks = 0)
        {
            if (repo is null) throw new ArgumentNullException(nameof(repo));
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                new RecorderSystem().RecordKeyframe(repo, writer, wallClockTicks, eventBus: null);
                writer.Flush();
            }
            return new WorldKeyframe(ms.ToArray(), repo.GlobalVersion);
        }

        /// <summary>
        /// Replaces <paramref name="repo"/>'s entities and components with <paramref name="keyframe"/>'s, in place.
        /// <para>⭐ <c>D8</c> — <b>versions only grow.</b> The keyframe is applied under <c>max(live, recorded) + 1</c>, not the
        /// recorded tick: a version that went BACK would make every change-since check (the running recorder's deltas,
        /// egress dirty checks) skip the restored state. ⚠ The apply reads the frame's tick from its first 8 bytes, so the
        /// apply works on a copy with that header rewritten — the keyframe itself is never changed.</para>
        /// </summary>
        public static void Apply(EntityRepository repo, WorldKeyframe keyframe, RestoreOptions? options = null)
        {
            if (repo is null) throw new ArgumentNullException(nameof(repo));
            if (keyframe is null) throw new ArgumentNullException(nameof(keyframe));
            options ??= RestoreOptions.DryRunExit;

            uint version = Math.Max(repo.GlobalVersion, keyframe.Tick) + 1;
            var bytes = (byte[])keyframe.Bytes.Clone();
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, version);

            using var reader = new BinaryReader(new MemoryStream(bytes, writable: false));
            new PlaybackSystem().ApplyFrame(repo, reader,
                eventBus: options.RestoreEvents ? repo.Bus : null,
                processEvents: options.RestoreEvents);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>D4</c> — <b>the one step after ANY world restore</b> (the dry-run exit, a checkpoint restore): the things a
    /// keyframe cannot carry are re-derived, never restored (R-3039 class A/B).
    /// </summary>
    public static class AfterRestore
    {
        /// <summary>
        /// ① the entity map is rebuilt from the restored world (it indexes entities the apply just replaced) ·
        /// ② <see cref="WorldEpoch.Restored"/> — every per-id cache keyed on <see cref="WorldEpoch.Moved"/> clears
        /// (class A/B), while <see cref="WorldEpoch.Ended"/> does NOT move, so nothing is deleted on the wire ·
        /// ③ the step window restarts (<see cref="EventAccumulator.Restart"/>) — a window must not span two worlds.
        /// </summary>
        public static void Run(EntityRepository repo, EventAccumulator? accumulator = null)
        {
            if (repo is null) throw new ArgumentNullException(nameof(repo));

            if (repo.HasSingletonManaged<NetworkEntityMap>())
                repo.GetSingletonManaged<NetworkEntityMap>()!.RebuildFromWorld(repo);

            WorldEpoch.Restored(repo);
            accumulator?.Restart();
        }
    }
}
