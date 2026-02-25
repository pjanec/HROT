using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using FDP.Toolkit.DER;

namespace Bagira.IOS.Services;

/// <summary>
/// Implements <see cref="IMissionEditorService"/> using the DER repository for
/// local state reads and injected DDS writers for outgoing commands.
///
/// <para>Concurrency model: <see cref="CommitMissionAsync"/> stores a
/// <see cref="TaskCompletionSource{T}"/> keyed by <see cref="Guid"/> and
/// <see cref="OnAckReceived"/> resolves it. Both methods may be called from
/// different threads; the internal dictionary is protected by a lock.</para>
/// </summary>
public sealed class MissionEditorService : IMissionEditorService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Default commit timeout in milliseconds. A commit that does not receive
    /// an ACK within this window resolves with Success=false.
    /// </summary>
    public const int DefaultCommitTimeoutMs = 5000;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IDerRepo _repo;
    private readonly IDdsWriter<MissionControlRequest> _requestWriter;
    private readonly int _commitTimeoutMs;

    // ── Pending commits ───────────────────────────────────────────────────────

    private readonly Dictionary<Guid, TaskCompletionSource<MissionCommitResult>> _pendingCommits = new();
    private readonly object _pendingLock = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MissionEditorService"/> with a configurable timeout.
    /// Pass a custom <paramref name="commitTimeoutMs"/> in tests to avoid
    /// real-time waits.
    /// </summary>
    public MissionEditorService(
        IDerRepo repo,
        IDdsWriter<MissionControlRequest> requestWriter,
        int commitTimeoutMs = DefaultCommitTimeoutMs)
    {
        _repo             = repo             ?? throw new ArgumentNullException(nameof(repo));
        _requestWriter    = requestWriter    ?? throw new ArgumentNullException(nameof(requestWriter));
        _commitTimeoutMs  = commitTimeoutMs;
    }

    // ── IMissionEditorService ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
    {
        var entity = _repo.GetEntity((int)entityId);
        if (entity is null)
            return (null, 0);

        // GetDescriptor<T> returns default(T) when not set; for structs that
        // means an empty struct rather than null, so we check HasDescriptor.
        MissionPlan? plan = entity.HasDescriptor<EntityMission>()
            ? entity.GetDescriptor<EntityMission>().Plan
            : null;

        long version = entity.HasDescriptor<DescriptorOptimisticLock>()
            ? entity.GetDescriptor<DescriptorOptimisticLock>().CurrentVersion
            : 0;

        return (plan, version);
    }

    /// <inheritdoc/>
    public async Task<MissionCommitResult> CommitMissionAsync(
        long entityId, MissionPlan newPlan, long baseVersion)
    {
        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<MissionCommitResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingLock)
        {
            _pendingCommits[requestId] = tcs;
        }

        _requestWriter.Write(new MissionControlRequest
        {
            RequestId      = requestId,
            TargetEntityId = entityId,
            BaseVersion    = baseVersion,
            Payload = new MissionCommandUnion
            {
                _d             = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = newPlan
            }
        });

        using var cts = new CancellationTokenSource(_commitTimeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timeout: clean up and return a failure result without throwing.
            lock (_pendingLock)
            {
                _pendingCommits.Remove(requestId);
            }

            return new MissionCommitResult
            {
                Success      = false,
                ErrorMessage = "Timeout"
            };
        }
    }

    /// <inheritdoc/>
    public void SendControlCommand(long entityId, eMissionCommandType type, Guid taskId)
    {
        _requestWriter.Write(new MissionControlRequest
        {
            RequestId      = Guid.NewGuid(),
            TargetEntityId = entityId,
            BaseVersion    = 0,   // Control commands don't perform version checks.
            Payload = new MissionCommandUnion
            {
                _d           = type,
                TargetTaskId = taskId
            }
        });
    }

    /// <inheritdoc/>
    public void OnAckReceived(MissionControlAck ack)
    {
        TaskCompletionSource<MissionCommitResult>? tcs;

        lock (_pendingLock)
        {
            if (!_pendingCommits.Remove(ack.RequestId, out tcs))
                return; // Unknown or already resolved (e.g. timed out).
        }

        tcs.TrySetResult(new MissionCommitResult
        {
            Success      = ack.ErrorCode == 0,
            ErrorMessage = ack.ErrorMessage,
            NewVersion   = ack.NewVersion
        });
    }
}
