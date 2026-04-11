using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Common.Events;
using Hrot.Map.Definitions.Tkb;
using FDP.Kernel.Logging;
using FDP.Toolkit.DER;
using Fdp.Kernel;
using Hrot.ExCon.Panels;

namespace Hrot.ExCon.Services;

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
/// local state reads and <see cref="FdpEventBus"/> for outgoing commands.
///
/// <para>Concurrency model: <see cref="CommitMissionAsync"/> stores a
/// <see cref="TaskCompletionSource{T}"/> keyed by <see cref="Guid"/> and
/// <see cref="Poll"/> resolves it when a <see cref="MissionControlAckEvent"/>
/// is consumed from the bus. Both methods may be called from different threads;
/// the internal dictionary is protected by a lock.</para>
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

    private readonly IDerRepo    _repo;
    private readonly FdpEventBus _bus;
    private readonly int         _commitTimeoutMs;
    private readonly long        _localNodeId;

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
    /// <param name="bus">Event bus for publishing <see cref="MissionControlIntent"/> and
    /// consuming <see cref="MissionControlAckEvent"/> messages.</param>
    /// <param name="commitTimeoutMs">Commit timeout; defaults to <see cref="DefaultCommitTimeoutMs"/>.</param>
    public MissionEditorService(
        IDerRepo    repo,
        FdpEventBus bus,
        int         commitTimeoutMs = DefaultCommitTimeoutMs,
        long        localNodeId     = 0)
    {
        _repo            = repo ?? throw new ArgumentNullException(nameof(repo));
        _bus             = bus  ?? throw new ArgumentNullException(nameof(bus));
        _commitTimeoutMs = commitTimeoutMs;
        _localNodeId     = localNodeId;
    }

    // ── IMissionEditorService ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
    {
        var entity = _repo.GetEntity((int)entityId);
        if (entity is null) return Array.Empty<string>();
        return DoctrineCatalog.GetValidDoctrines(entity.TkbType);
    }

    /// <inheritdoc/>
    public (MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
    {
        var entity = _repo.GetEntity((int)entityId);
        if (entity is null)
            return (null, 0);

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

        _bus.PublishManaged(new MissionControlIntent
        {
            RequestId      = requestId,
            TargetEntityId = entityId,
            BaseVersion    = baseVersion,
            Payload = new MissionCommandUnion
            {
                _d              = eMissionCommandType.CMD_REPLACE_MISSION,
                FullMissionData = newPlan
            }
        });

        FdpLog<MissionEditorService>.Info("[Node-{0}] CommitMissionAsync sent: entityId={1} requestId={2} baseVersion={3}",
            _localNodeId, entityId, requestId, baseVersion);

        using var cts = new CancellationTokenSource(_commitTimeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FdpLog<MissionEditorService>.Warn("[Node-{0}] Commit timed out: entityId={1} requestId={2}",
                _localNodeId, entityId, requestId);

            return new MissionCommitResult
            {
                Success      = false,
                ErrorMessage = "Timeout"
            };
        }
    }

    /// <inheritdoc/>
    public async Task<MissionCommitResult> SendControlCommandAsync(
        long entityId, eMissionCommandType type, Guid taskId)
    {
        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<MissionCommitResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pendingLock)
        {
            _pendingCommits[requestId] = tcs;
        }

        _bus.PublishManaged(new MissionControlIntent
        {
            RequestId      = requestId,
            TargetEntityId = entityId,
            BaseVersion    = 0,   // Control commands don't perform version checks.
            Payload = new MissionCommandUnion
            {
                _d           = type,
                TargetTaskId = taskId
            }
        });

        FdpLog<MissionEditorService>.Info(
            "[Node-{0}] SendControlCommandAsync sent: entityId={1} type={2} requestId={3}",
            _localNodeId, entityId, type, requestId);

        using var cts = new CancellationTokenSource(_commitTimeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FdpLog<MissionEditorService>.Warn(
                "[Node-{0}] Control command timed out: entityId={1} type={2} requestId={3}",
                _localNodeId, entityId, type, requestId);

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
        _bus.PublishManaged(new MissionControlIntent
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

    // ── Internal helpers ─────────────────────────────────────────────

    internal void OnAckReceived(MissionControlAckEvent ack)
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
            ErrorMessage = ack.ErrorCode == 0
                ? string.Empty
                : ack.ErrorCode == PanelConstants.VersionConflictErrorCode
                    ? PanelConstants.VersionConflictErrorMessage
                    : $"Error {ack.ErrorCode}",
            NewVersion   = ack.NewVersion,
            ErrorCode    = ack.ErrorCode
        });

        FdpLog<MissionEditorService>.Info("[Node-{0}] MissionControlAckEvent received: requestId={1} success={2} errorCode={3}",
            _localNodeId, ack.RequestId, ack.ErrorCode == 0, ack.ErrorCode);
    }

    // ── IIngressHandler ───────────────────────────────────────────────────────

    /// <summary>
    /// Drains all <see cref="MissionControlAckEvent"/> events from the bus read
    /// buffer and resolves any pending commits.
    ///
    /// <para>Register this service as an <see cref="IIngressHandler"/> in the
    /// <see cref="ExConLogic"/> constructor so that incoming ACKs are processed
    /// once per frame on the main thread.</para>
    ///
    /// <para>The caller must call <c>FdpEventBus.SwapBuffers()</c> before
    /// invoking this method so that newly arrived ACKs are visible in the
    /// read buffer.</para>
    /// </summary>
    public void Poll()
    {
        foreach (var ack in _bus.Consume<MissionControlAckEvent>())
            OnAckReceived(ack);
    }

    // ── IDisposable ────────────────────────────────────────────────

    /// <summary>
    /// Disposes the service, cancelling all pending commits with a graceful
    /// failure result so that awaiting callers are never left orphaned.
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
