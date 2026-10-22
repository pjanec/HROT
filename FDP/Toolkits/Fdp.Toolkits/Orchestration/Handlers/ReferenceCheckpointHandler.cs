using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Core.Logging;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Replay;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// ⭐⭐ <c>CE-3299</c> B3 (R-3044) — the checkpoint handler: a checkpoint is a KEYFRAME the node's running recording writes on
    /// request and names. <see cref="NodeOpType.TakeSnapshot"/> asks the recording for one at the master's wall time (nothing
    /// pauses, the recorder already runs every frame); <see cref="NodeOpType.ExportCheckpoint"/> copies that keyframe out as a
    /// one-frame recording. Both answer at once: Success when done or queued, Failure (thrown, so the slave reports it) when this
    /// node is not recording or the label is wrong. ⛔ The <c>'FDPC'</c> capture (<c>SyncFrom</c> + a writer of its own) it
    /// replaced is gone. 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1g.
    /// <para>⭐⭐ <c>CE-3299</c> B4 (§1h) — and the RESTORE, in two phases: <see cref="NodeOpType.PrepareRestore"/> reads keyframe X
    /// from the live recording and stages it (nothing changes; an abort drops it); <see cref="NodeOpType.RestoreSnapshot"/> stops the
    /// recording, then on the main thread puts the world back to X (<see cref="WorldKeyframes.Apply"/>, <see cref="AfterRestore"/>, the
    /// ELM re-derive) and records on as the new exercise, its first frames a copy of the old one's up to X (D18).</para>
    /// </summary>
    public sealed class ReferenceCheckpointHandler : IClusterStateHandler
    {
        private readonly Func<RecordingModule?> _recording;
        private readonly IRecordReplayController? _controller;
        private readonly EntityRepository? _world;
        private readonly EventAccumulator? _accumulator;
        private readonly Action<bool>? _worldReplaced;
        private readonly string? _storageDirectory;

        // ⭐ B4 — phase 1's staged keyframe (one restore at a time), and what phase 2's commit applies.
        private Staged? _staged;
        private Staged? _committing;
        private sealed record Staged(Guid TransactionId, RestoreCheckpointPayload Payload, CheckpointMark Mark, WorldKeyframe Keyframe,
                                     RecordingBranch History);

        /// <param name="recording">The node's LIVE recording, or null when it is not recording (the record/replay controller's).</param>
        /// <param name="controller">⭐ B4 — the node's record/replay controller: a restore stops its recording and records the branch.
        /// Null: this host takes checkpoints but cannot restore one (refused, named).</param>
        /// <param name="world">⭐ B4 — the world a restore puts back.</param>
        /// <param name="accumulator">⭐ B4 — the step-event accumulator <see cref="AfterRestore"/> restarts (D4).</param>
        /// <param name="worldReplaced">⭐ B4 — the ELM's world-replacement hook (§2.1m), called with <c>true</c>: the restored world goes on
        /// live, so its protocols are re-derived.</param>
        /// <param name="storageDirectory">⭐ B4 — where the node's recordings live (the new exercise is recorded there).</param>
        public ReferenceCheckpointHandler(
            Func<RecordingModule?> recording,
            IRecordReplayController? controller = null,
            EntityRepository? world = null,
            EventAccumulator? accumulator = null,
            Action<bool>? worldReplaced = null,
            string? storageDirectory = null)
        {
            _recording        = recording ?? throw new ArgumentNullException(nameof(recording));
            _controller       = controller;
            _world            = world;
            _accumulator      = accumulator;
            _worldReplaced    = worldReplaced;
            _storageDirectory = storageDirectory;
        }

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) =>
            operation is NodeOpType.TakeSnapshot or NodeOpType.ExportCheckpoint or NodeOpType.PrepareRestore or NodeOpType.RestoreSnapshot;

        /// <inheritdoc />
        /// <remarks>⭐ B4 — an abort of the restore THIS handler staged (and only that one) drops it.</remarks>
        public bool CanHandle(ExecuteNodeOpIntent intent) =>
            intent.Operation == NodeOpType.AbortTransaction
                ? _staged != null && intent.DomainPayload is AbortTransactionPayload abort && abort.TargetTransactionId == _staged.TransactionId
                : CanHandle(intent.Operation);

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            try
            {
                switch (intent.Operation)
                {
                    case NodeOpType.AbortTransaction:
                        FdpLog<ReferenceCheckpointHandler>.Info("[Checkpoint] restore of '{0}' aborted — the staged keyframe is dropped.", _staged?.Payload.Label ?? "?");
                        _staged = null;
                        return Task.FromResult<object?>(null);
                    case NodeOpType.PrepareRestore:
                        return StageRestore(intent);
                    case NodeOpType.RestoreSnapshot:
                        return BeginRestore(intent);
                }

                var recording = _recording() ?? throw new InvalidOperationException("this node is not recording — no checkpoint can be written");
                if (intent.Operation == NodeOpType.TakeSnapshot)
                {
                    var take = intent.DomainPayload is CheckpointPayload p && !string.IsNullOrWhiteSpace(p.Label)
                        ? p : new CheckpointPayload($"cp-{DateTime.UtcNow:HHmmss}", 0);
                    recording.RequestCheckpoint(take.Label, take.AtWallTicks);
                    FdpLog<ReferenceCheckpointHandler>.Info("[Checkpoint] '{0}' asked of the recording at wall ticks {1}.", take.Label, take.AtWallTicks);
                    return Task.FromResult<object?>(take);
                }

                var label = intent.DomainPayload is ExportCheckpointPayload e ? e.Label : "";
                if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("an export names its checkpoint");
                string path = ExportPath(recording.FilePath!, label);
                recording.ExportCheckpoint(label, path);
                FdpLog<ReferenceCheckpointHandler>.Info("[Checkpoint] '{0}' exported to {1}.", label, path);
                return Task.FromResult<object?>(path);
            }
            catch (Exception ex)
            {
                FdpLog<ReferenceCheckpointHandler>.Error("[Checkpoint] {0} refused: {1}", intent.Operation, ex.Message);
                return Task.FromException<object?>(ex);
            }
        }

        // ⭐ B4 phase 1 — find mark X in the live recording and read its keyframe off the main thread. Nothing changes.
        private Task<object?> StageRestore(ExecuteNodeOpIntent intent)
        {
            if (_controller == null || _world == null || string.IsNullOrEmpty(_storageDirectory))
                throw new InvalidOperationException("this host cannot restore a checkpoint (no recording controller or world)");
            if (intent.DomainPayload is not RestoreCheckpointPayload payload || string.IsNullOrWhiteSpace(payload.Label))
                throw new ArgumentException("a restore names its checkpoint");
            var recording = _recording() ?? throw new InvalidOperationException("this node is not recording — it has no checkpoint to restore");
            var mark = recording.FindCheckpoint(payload.Label)
                       ?? throw new InvalidOperationException($"the recording has no checkpoint named '{payload.Label}'");
            string source = recording.FilePath!;
            Guid parent = recording.ExerciseId;
            _staged = null;
            return Task.Run<object?>(() =>
            {
                recording.WaitForWrites();
                var frame = RecordingSlice.ReadFrame(source, mark.Frame, out var type);
                if (type != FrameType.Keyframe)
                    throw new InvalidDataException($"checkpoint '{payload.Label}' (frame {mark.Frame}) is not a keyframe");
                _staged = new Staged(intent.TransactionId, payload, mark, WorldKeyframe.FromRecordedFrame(frame),
                    new RecordingBranch(source, mark.Frame + 1, parent, mark.WallTicks));
                FdpLog<ReferenceCheckpointHandler>.Info("[Checkpoint] restore of '{0}' staged ({1} bytes, frame {2}).",
                    payload.Label, frame.Length, mark.Frame);
                return (object?)null;   // ⚠ a bare null picks Task.Run's Func<Task> overload, and a null inner task CANCELS
            });
        }

        // ⭐ B4 phase 2, off the main thread — stop the recording (its frames up to X are what the branch copies). The world is put
        //   back in Commit, on the main thread.
        private Task<object?> BeginRestore(ExecuteNodeOpIntent intent)
        {
            var staged = _staged;
            if (staged == null || staged.TransactionId != intent.TransactionId)
                throw new InvalidOperationException("no checkpoint is staged for this restore (phase 1 did not run here)");
            _staged = null;
            return FinishPrepare();

            async Task<object?> FinishPrepare()
            {
                await _controller!.FinalizeRecordingAsync().ConfigureAwait(false);
                _committing = staged;
                var mark = staged.Mark;
                return new CheckpointRestoreResult(
                    new GlobalTime
                    {
                        TotalWallTicks = mark.WallTicks, TotalTime = mark.TotalTime,
                        UnscaledTotalTime = mark.UnscaledTotalTime, FrameNumber = mark.FrameNumber,
                    },
                    staged.Payload.NewExerciseId);
            }
        }

        /// <inheritdoc />
        /// <remarks>⭐ B4 — phase 2 on the main thread: keyframe X goes back in, then the recording of the new exercise starts (its install
        /// lands on a later tick, so no frame of the old world reaches it). The other ops did their work in the prepare.</remarks>
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation != NodeOpType.RestoreSnapshot || _committing is not { } restore) return;
            _committing = null;

            WorldKeyframes.Apply(_world!, restore.Keyframe);   // D2: no events re-injected · D8: versions only grow
            AfterRestore.Run(_world!, _accumulator);           // entity map · WorldEpoch.Restored · the step window (D4)
            _worldReplaced?.Invoke(true);                     // the ELM: clear, and re-derive the protocols the restored world implies

            var exercise = restore.Payload.NewExerciseId;
            _controller!.PrepareRecordingAsync(exercise, _storageDirectory!, restore.History).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    FdpLog<ReferenceCheckpointHandler>.Error("[Checkpoint] restored '{0}', but the recording of exercise {1} did not start: {2}",
                        restore.Payload.Label, exercise, t.Exception?.GetBaseException().Message ?? "unknown");
            }, TaskScheduler.Default);
            FdpLog<ReferenceCheckpointHandler>.Info("[Checkpoint] '{0}' restored; recording on as exercise {1} after {2} copied frame(s).",
                restore.Payload.Label, exercise, restore.History.FrameCount);
        }

        /// <inheritdoc />
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

        /// <summary>
        /// Where checkpoint <paramref name="label"/> of the recording <paramref name="recordingPath"/> is exported: a
        /// <c>checkpoints</c> folder beside it, named after the recording and the label (characters a file name cannot hold become '_').
        /// </summary>
        public static string ExportPath(string recordingPath, string label)
        {
            var safe = label;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recordingPath))!, "checkpoints",
                $"{Path.GetFileNameWithoutExtension(recordingPath)}.{safe}.fdp");
        }
    }
}
