using System;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// Data-plane module that strictly owns one <see cref="AsyncRecorder"/> and the
    /// <see cref="RecorderTickSystem"/> that drives it at the scheduler's frame rate.
    /// <para>
    /// Lifecycle: <see cref="RegisterSystems"/> opens the <c>.fdp</c> file stream;
    /// <see cref="Dispose"/> blocks until all LZ4 buffers are flushed and the
    /// <c>.meta.json</c> manifest is written.
    /// </para>
    /// <para>
    /// Zero-cost idle path: when the module is not installed, no
    /// <see cref="RecorderTickSystem"/> exists in the scheduler — no
    /// <c>if (isRecording)</c> guards on the hot path.
    /// </para>
    /// </summary>
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class RecordingModule : IEcsModule, IDisposable
    {
        private readonly RecordingConfiguration _config;
        private AsyncRecorder? _recorder;
        private RecorderTickSystem? _tick;

        /// <inheritdoc/>
        public string Name => $"Recording_{_config.ExerciseId:N}";

        /// <inheritdoc/>
        /// <remarks>
        /// Synchronous policy so <see cref="RecorderTickSystem"/> runs on the main thread
        /// and receives the live <see cref="EntityRepository"/> as its
        /// <see cref="ISimulationView"/>.
        /// </remarks>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <summary>
        /// Creates a recording module with the given configuration.
        /// The output file is not opened until <see cref="RegisterSystems"/> is called.
        /// </summary>
        public RecordingModule(RecordingConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            var metadata = new RecordingMetadata
            {
                ExerciseId = _config.ExerciseId, NodeId = _config.NodeId, CheckpointingSeconds = Math.Max(0, _config.CheckpointingSeconds),
            };
            // ⭐ D18 (R-3047) — a branch copies its parent's frames up to the branch point and appends after them; ⛔ never over a file.
            _recorder = _config.Branch is { } branch
                ? new AsyncRecorder(_config.FilePath, StartBranch(branch, _config.FilePath, metadata), branch.FrameCount)
                : new AsyncRecorder(_config.FilePath, metadata, appendAfterFrames: 0);

            if (_config.EntityFilter != null)
                _recorder.EntityFilter = _config.EntityFilter;

            // D17 — the mode is the metadata's: a branch's StartBranch put its parent's there
            registry.RegisterSystem(_tick = new RecorderTickSystem(_recorder, _config.Blocking, _recorder.Metadata.CheckpointingSeconds,
                                                                    _config.SlotRunInFlight));
        }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> — writes <paramref name="branch"/>'s copied history into <paramref name="filePath"/> (a byte
        /// copy, <see cref="RecordingSlice.CopyFrames"/>) and fills <paramref name="metadata"/> with what it inherits: the parent's
        /// checkpoints at or before the branch point, its terrain and geo origin, and the parent's id and branch time.
        /// </summary>
        internal static RecordingMetadata StartBranch(RecordingBranch branch, string filePath, RecordingMetadata metadata)
        {
            if (branch.FrameCount < 1) throw new ArgumentOutOfRangeException(nameof(branch), "a branch copies at least the branch point's frame");
            RecordingSlice.CopyFrames(branch.SourcePath, filePath, 0, branch.FrameCount);
            var parentMeta = branch.SourcePath + ".meta.json";
            if (System.IO.File.Exists(parentMeta))
            {
                var parent = MetadataSerializer.Deserialize(System.IO.File.ReadAllText(parentMeta));
                metadata.Checkpoints = parent.Checkpoints.FindAll(m => m.Frame < branch.FrameCount);
                metadata.TerrainName = parent.TerrainName;
                metadata.GeoOrigin   = parent.GeoOrigin;
                metadata.MaxNetworkId = parent.MaxNetworkId;
                metadata.CheckpointingSeconds = parent.CheckpointingSeconds;   // D17 — one session, one mode
            }
            metadata.ParentExerciseId = branch.ParentExerciseId;
            metadata.BranchWallTicks  = branch.BranchWallTicks;
            return metadata;
        }

        /// <summary>⭐ B4 — the checkpoint <paramref name="label"/> of this recording, or null.</summary>
        public CheckpointMark? FindCheckpoint(string label) => _recorder?.Metadata.Checkpoints.Find(m => m.Label == label);

        /// <summary>⭐ CE-3329 (§9f) — every checkpoint written so far (in memory — a review's marks, before any <c>.meta.json</c>).</summary>
        public CheckpointMark[] Checkpoints => _recorder?.Metadata.Checkpoints.ToArray() ?? System.Array.Empty<CheckpointMark>();

        /// <summary>⭐ B4 — the exercise this module records.</summary>
        public Guid ExerciseId => _config.ExerciseId;

        /// <summary>⭐ B4 — waits until every frame captured so far is on disk (<see cref="AsyncRecorder.WaitForWrites"/>).</summary>
        public void WaitForWrites() => _recorder?.WaitForWrites();

        /// <summary>⭐ CE-3329 P3 (R8) — suspend the recording for a review (nothing written, the file stays open) and flush.</summary>
        public void SuspendForReview()
        {
            _tick?.Suspend();
            _recorder?.WaitForWrites();
        }

        /// <summary>⭐ CE-3329 P3 — resume the SAME recording after a review (a keyframe first, the review's destructions dropped).</summary>
        public void ResumeAfterReview(double? atSimSeconds = null) => _tick?.Resume(atSimSeconds);

        /// <summary>⭐ CE-3329 — labelled checkpoints this recording still owes (a forced <c>review-&lt;n&gt;</c> waits for its frame).</summary>
        public int PendingCheckpoints => _tick?.PendingCheckpoints ?? 0;

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime) { /* driven by RecorderTickSystem */ }

        /// <summary>
        /// Sets the maximum network entity ID for the recording session; written to
        /// <c>.meta.json</c> when <see cref="Dispose"/> is called.
        /// Callers supply the value from the network entity map just before requesting uninstall.
        /// </summary>
        public void SetMaxNetworkId(long maxNetworkId)
        {
            if (_recorder != null)
                _recorder.MaxNetworkId = maxNetworkId;
        }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B3 (R-3044) — asks THIS recording for a checkpoint: a keyframe named <paramref name="label"/> at the
        /// first frame at or past <paramref name="atWallTicks"/> (<see cref="RecorderTickSystem.RequestCheckpoint"/>). Throws when
        /// the module is not recording yet or the label is taken.
        /// </summary>
        public void RequestCheckpoint(string label, long atWallTicks)
            => (_tick ?? throw new InvalidOperationException("the recording has not started")).RequestCheckpoint(label, atWallTicks);

        /// <summary>
        /// ⭐ <c>CE-3299</c> B3 (R-3044 "collect") — exports the checkpoint <paramref name="label"/> as a ONE-FRAME recording at
        /// <paramref name="outputPath"/> (+ its <c>.meta.json</c>): a byte copy of this recording's header and that keyframe, readable
        /// by the replay code. Throws when the checkpoint has not been written (yet).
        /// </summary>
        public void ExportCheckpoint(string label, string outputPath)
        {
            var recorder = _recorder ?? throw new InvalidOperationException("the recording has not started");
            var mark = recorder.Metadata.Checkpoints.Find(m => m.Label == label)
                       ?? throw new InvalidOperationException($"the recording has no checkpoint named '{label}' (yet)");
            recorder.WaitForWrites();
            RecordingSlice.CopyFrames(recorder.FilePath, outputPath, mark.Frame, 1);
            var meta = MetadataSerializer.Deserialize(MetadataSerializer.Serialize(recorder.Metadata));
            meta.TotalFrames = 1;
            meta.Checkpoints = new System.Collections.Generic.List<CheckpointMark>
                { new CheckpointMark { Label = mark.Label, Frame = 0, WallTicks = mark.WallTicks, TotalTime = mark.TotalTime,
                                       UnscaledTotalTime = mark.UnscaledTotalTime, FrameNumber = mark.FrameNumber } };
            System.IO.File.WriteAllText(outputPath + ".meta.json", MetadataSerializer.Serialize(meta));
        }

        /// <summary>The recording file (null before it starts).</summary>
        public string? FilePath => _recorder?.FilePath;

        /// <summary>
        /// Blocking dispose: drains the LZ4 front-buffer, writes
        /// <c>MaxNetworkId</c> and <c>.meta.json</c> manifest before returning.
        /// <c>NodeOpStatus(Success)</c> must not be sent before this completes.
        /// </summary>
        public void Dispose()
        {
            _recorder?.Dispose();
            _recorder = null;
        }
    }
}
