using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.Map.Common.Dds;
using FDP.Kernel.Logging;
using FDP.Toolkit.DER;

namespace Bagira.IOS.Services;

/// <summary>
/// Named constants used by <see cref="MissionEditorService"/>.
/// Centralised here so any message-text change is a one-line edit
/// (CODE-STANDARDS §1).
/// </summary>
internal static class MissionEditorServiceConstants
{
    /// <summary>
    /// Error message placed in a <see cref="MissionCommitResult"/> when the
    /// service is disposed while commits are still pending.
    /// </summary>
    internal const string DisposedErrorMessage = "Service disposed";
}

/// <summary>
/// Implements <see cref="IMissionEditorService"/> using the DER repository for
/// local state reads and injected DDS writers for outgoing commands.
///
/// <para>Concurrency model: <see cref="CommitMissionAsync"/> stores a
/// <see cref="TaskCompletionSource{T}"/> keyed by <see cref="Guid"/> and
/// <see cref="OnAckReceived"/> resolves it. Both methods may be called from
/// different threads; the internal dictionary is protected by a lock.</para>
/// </summary>
public sealed class MissionEditorService : IMissionEditorService, IIngressHandler, IDisposable
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
    private readonly IEventQueue<MissionControlAck>?   _ackQueue;
    private readonly int _commitTimeoutMs;

    // ── Pending commits ───────────────────────────────────────────────────────

    private readonly Dictionary<Guid, TaskCompletionSource<MissionCommitResult>> _pendingCommits = new();
    private readonly object _pendingLock = new();

    // ── Dispose guard ─────────────────────────────────────────────────────────

    private bool _disposed;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MissionEditorService"/> with a configurable timeout.
    /// Pass a custom <paramref name="commitTimeoutMs"/> in tests to avoid
    /// real-time waits.
    /// </summary>
    /// <param name="repo">DER entity repository for snapshot reads.</param>
    /// <param name="requestWriter">DDS writer for outgoing <see cref="MissionControlRequest"/> messages.</param>
    /// <param name="commitTimeoutMs">Commit timeout; defaults to <see cref="DefaultCommitTimeoutMs"/>.</param>
    /// <param name="ackQueue">
    /// Optional ingress queue for <see cref="MissionControlAck"/> messages.
    /// When provided, call <see cref="Poll"/> each frame (via <see cref="IIngressHandler"/>)
    /// to drain incoming ACKs and resolve pending commits automatically.
    /// When <c>null</c> the caller must invoke <see cref="OnAckReceived"/> manually.
    /// </param>
    public MissionEditorService(
        IDerRepo repo,
        IDdsWriter<MissionControlRequest> requestWriter,
        int commitTimeoutMs = DefaultCommitTimeoutMs,
        IEventQueue<MissionControlAck>? ackQueue = null)
    {
        _repo             = repo             ?? throw new ArgumentNullException(nameof(repo));
        _requestWriter    = requestWriter    ?? throw new ArgumentNullException(nameof(requestWriter));
        _commitTimeoutMs  = commitTimeoutMs;
        _ackQueue         = ackQueue;
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

        FdpLog<MissionEditorService>.Info("[IOS] CommitMissionAsync sent: entityId={0} requestId={1} taskCount={2} baseVersion={3}",
            entityId, requestId, newPlan.Tasks?.Count ?? 0, baseVersion);

        using var cts = new CancellationTokenSource(_commitTimeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timeout: clean up and return a failure result without throwing.
            FdpLog<MissionEditorService>.Warn("[IOS] Commit timed out: entityId={0} requestId={1}",
                entityId, requestId);

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
            NewVersion   = ack.NewVersion,
            ErrorCode    = ack.ErrorCode
        });

        FdpLog<MissionEditorService>.Info("[IOS] MissionControlAck received: requestId={0} success={1} errorCode={2} newVersion={3}",
            ack.RequestId, ack.ErrorCode == 0, ack.ErrorCode, ack.NewVersion);
    }

    // ── IIngressHandler ───────────────────────────────────────────────────────

    /// <summary>
    /// Drains the injected <see cref="IEventQueue{MissionControlAck}"/> and
    /// calls <see cref="OnAckReceived"/> for each message.
    ///
    /// <para>Register this service as an <see cref="IIngressHandler"/> in the
    /// <see cref="IosLogic"/> constructor so that incoming ACKs are processed
    /// once per frame on the main thread, completing any pending commits.</para>
    ///
    /// <para>This method is a no-op when no queue was provided at construction.</para>
    /// </summary>
    public void Poll()
    {
        if (_ackQueue is null) return;

        while (_ackQueue.TryDequeue(out var ack))
            OnAckReceived(ack);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    /// Disposes the service, cancelling all pending commits with a graceful
    /// failure result so that awaiting callers are never left orphaned.
    ///
    /// <para>Each orphaned <see cref="TaskCompletionSource{T}"/> is resolved via
    /// <see cref="TaskCompletionSource{T}.TrySetResult"/> (not
    /// <c>TrySetCanceled</c>) so that callers receiving a <see cref="MissionCommitResult"/>
    /// with <c>Success=false</c> handle the teardown path without an
    /// <see cref="OperationCanceledException"/> propagating up the stack.</para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        List<TaskCompletionSource<MissionCommitResult>> orphans;

        lock (_pendingLock)
        {
            orphans = new List<TaskCompletionSource<MissionCommitResult>>(_pendingCommits.Values);
            _pendingCommits.Clear();
        }

        foreach (var tcs in orphans)
        {
            tcs.TrySetResult(new MissionCommitResult
            {
                Success      = false,
                ErrorMessage = MissionEditorServiceConstants.DisposedErrorMessage
            });
        }
    }
}
