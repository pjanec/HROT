using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
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
    /// </summary>
    public sealed class ReferenceCheckpointHandler : IClusterStateHandler
    {
        private readonly Func<RecordingModule?> _recording;

        /// <param name="recording">The node's LIVE recording, or null when it is not recording (the record/replay controller's).</param>
        public ReferenceCheckpointHandler(Func<RecordingModule?> recording)
            => _recording = recording ?? throw new ArgumentNullException(nameof(recording));

        /// <inheritdoc />
        public bool CanHandle(NodeOpType operation) => operation is NodeOpType.TakeSnapshot or NodeOpType.ExportCheckpoint;

        /// <inheritdoc />
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            try
            {
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

        /// <inheritdoc />
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }   // the work is the prepare's (it answers at once)

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
