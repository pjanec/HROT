using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;

namespace Fdp.Toolkit.Orchestration.Handlers;

/// <summary>
/// Payload for <see cref="ReferenceArchiveHandler"/> commands.
/// </summary>
public record struct ArchiveHandlerPayload(string? ExerciseId);

/// <summary>
/// Result published by <see cref="ReferenceArchiveHandler"/> after a successful archive.
/// </summary>
public record struct FileManifestResult(string SourceUnc, string RelativeDest);

/// <summary>
/// Node-side archive handler (CGF1-S0505).
/// Responds to <see cref="NodeOpType.SerializeLocal"/> intents whose
/// <c>DomainPayload</c> is an <see cref="ArchiveHandlerPayload"/> with a non-null
/// <c>ExerciseId</c>.
/// When committed, it reads the per-node <c>.fdp</c> file and publishes a
/// <see cref="NodeOpCompletedEvent"/> whose <c>ResultPayload</c> contains a
/// <see cref="FileManifestResult"/> array so that <c>ClusterMaster.ConsumeNodeOpStatuses</c>
/// can pull the file to the central NAS.
/// </summary>
public sealed class ReferenceArchiveHandler : IClusterStateHandler
{
    private readonly string _localTempRoot;
    private readonly int    _nodeId;

    public ReferenceArchiveHandler(
        string                   localTempRoot,
        int                      nodeId)
    {
        _localTempRoot = localTempRoot ?? throw new ArgumentNullException(nameof(localTempRoot));
        _nodeId        = nodeId;
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation)
        => operation == NodeOpType.SerializeLocal;

    /// <inheritdoc />
    /// <remarks>
    /// Locates the local .fdp file and returns a <see cref="FileManifestResult"/> array as the
    /// task result so that <c>ClusterSlave.DispatchIntent</c> can include it in the
    /// <see cref="NodeOpCompletedEvent.ResultPayload"/> published to the event bus.
    /// </remarks>
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        var exerciseId = intent.DomainPayload is ArchiveHandlerPayload p ? p.ExerciseId : null;
        if (exerciseId is null) return Task.FromResult<object?>(null);

        var file = Path.Combine(_localTempRoot, exerciseId, $"node_{_nodeId}.fdp");
        if (!File.Exists(file))
        {
            FdpLog<ReferenceArchiveHandler>.Warn($"[ReferenceArchiveHandler] No local .fdp at {file}; cannot report manifest.");
            return Task.FromResult<object?>(null);
        }

        var manifest = new[]
        {
            new FileManifestResult(
                SourceUnc:    file,
                RelativeDest: Path.Combine(exerciseId, $"node_{_nodeId}.fdp")),
        };

        return Task.FromResult<object?>(manifest);
    }

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <inheritdoc />
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        var exerciseId = intent.DomainPayload is ArchiveHandlerPayload p ? p.ExerciseId : null;
        if (exerciseId is null) return;
        var file = Path.Combine(_localTempRoot, exerciseId, $"node_{_nodeId}.fdp");
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex)
        {
            FdpLog<ReferenceArchiveHandler>.Warn($"[ReferenceArchiveHandler] Abort cleanup failed for {file}: {ex.Message}");
        }
    }
}
