using Hrot.Core.Mission;
using Hrot.Core.Network;
using Hrot.Map.Definitions.Tkb;
using Fdp.Kernel.Logging;
using Fdp.Toolkit.DER;

namespace Hrot.ExCon.Services;

/// <summary>
/// Named constants used by <see cref="MissionEditorService"/>.
/// </summary>
internal static class MissionEditorServiceConstants
{
    internal const string DisposedErrorMessage = "Service disposed";
    internal const string TimeoutErrorMessage  = "Timeout";
}

/// <summary>
/// Implements <see cref="IMissionEditorService"/> using the DER repository for
/// local state reads and <see cref="ICommandGateway"/> for outgoing commands.
///
/// <para>Concurrency model: <see cref="CommitMissionAsync"/> delegates directly
/// to <see cref="ICommandGateway.SendMissionControlRequestAsync"/> which handles
/// the request-response correlation internally. No bus or pending commit tracking
/// is required at this layer.</para>
/// </summary>
public sealed class MissionEditorService : IMissionEditorService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Default commit timeout in milliseconds.</summary>
    public const int DefaultCommitTimeoutMs = 5000;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IDerRepo        _repo;
    private readonly ICommandGateway _gateway;
    private readonly int             _commitTimeoutMs;
    private readonly long            _localNodeId;

    // ── Dispose guard ─────────────────────────────────────────────────────────

    private bool _disposed;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MissionEditorService"/>.
    /// </summary>
    /// <param name="repo">DER entity repository for snapshot reads.</param>
    /// <param name="gateway">ICommandGateway for sending mission control requests.</param>
    /// <param name="commitTimeoutMs">Commit timeout; defaults to <see cref="DefaultCommitTimeoutMs"/>.</param>
    /// <param name="localNodeId">Local node identifier for log messages.</param>
    public MissionEditorService(
        IDerRepo        repo,
        ICommandGateway gateway,
        int             commitTimeoutMs = DefaultCommitTimeoutMs,
        long            localNodeId     = 0)
    {
        _repo            = repo    ?? throw new ArgumentNullException(nameof(repo));
        _gateway         = gateway ?? throw new ArgumentNullException(nameof(gateway));
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

        if (!entity.HasDescriptor<EntityMissionDescriptor>())
            return (null, 0);

        var desc = entity.GetDescriptor<EntityMissionDescriptor>();
        return (desc.Plan, desc.Version);
    }

    /// <inheritdoc/>
    public async Task<MissionCommitResult> CommitMissionAsync(
        long entityId, MissionPlan newPlan, long baseVersion)
    {
        ThrowIfDisposed();

        FdpLog<MissionEditorService>.Info(
            "[Node-{0}] CommitMissionAsync: entityId={1} baseVersion={2}",
            _localNodeId, entityId, baseVersion);

        using var cts = new CancellationTokenSource(_commitTimeoutMs);
        try
        {
            return await _gateway.SendMissionControlRequestAsync(
                new MissionControlCommand
                {
                    EntityId    = (int)entityId,
                    CommandType = eMissionCommandType.CMD_REPLACE_MISSION,
                    Plan        = newPlan,
                    BaseVersion = baseVersion,
                },
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FdpLog<MissionEditorService>.Warn(
                "[Node-{0}] CommitMissionAsync timed out: entityId={1}", _localNodeId, entityId);
            return new MissionCommitResult { Success = false, ErrorMessage = MissionEditorServiceConstants.TimeoutErrorMessage };
        }
    }

    /// <inheritdoc/>
    public async Task<MissionCommitResult> SendControlCommandAsync(
        long entityId, eMissionCommandType type, Guid taskId)
    {
        ThrowIfDisposed();

        FdpLog<MissionEditorService>.Info(
            "[Node-{0}] SendControlCommandAsync: entityId={1} type={2}",
            _localNodeId, entityId, type);

        using var cts = new CancellationTokenSource(_commitTimeoutMs);
        try
        {
            return await _gateway.SendMissionControlRequestAsync(
                new MissionControlCommand
                {
                    EntityId    = (int)entityId,
                    CommandType = type,
                    TaskId      = taskId,
                    BaseVersion = 0,   // Control commands bypass version check.
                },
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FdpLog<MissionEditorService>.Warn(
                "[Node-{0}] SendControlCommandAsync timed out: entityId={1} type={2}",
                _localNodeId, entityId, type);
            return new MissionCommitResult { Success = false, ErrorMessage = MissionEditorServiceConstants.TimeoutErrorMessage };
        }
    }

    /// <inheritdoc/>
    public void SendControlCommand(long entityId, eMissionCommandType type, Guid taskId)
    {
        ThrowIfDisposed();
        // Fire-and-forget — errors are discarded intentionally.
        _ = _gateway.SendMissionControlRequestAsync(
            new MissionControlCommand
            {
                EntityId    = (int)entityId,
                CommandType = type,
                TaskId      = taskId,
                BaseVersion = 0,
            }).ConfigureAwait(false);
    }

    // ── IDisposable ────────────────────────────────────────────────

    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MissionEditorService));
    }
}
