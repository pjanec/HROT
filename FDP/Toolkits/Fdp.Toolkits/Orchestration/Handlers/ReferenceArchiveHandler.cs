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
public record struct ArchiveHandlerPayload(Guid ExerciseId);

/// <summary>
/// Result published by a <see cref="NodeOpType.SerializeLocal"/> handler after writing its local file(s).
/// <para>⭐ <paramref name="DocType"/> (CE-277(c1)) carries the slice's <c>$meta.docType</c> so the
/// orchestrator can classify it after the pull — a compatible scenario slice is merged, a foreign one
/// (e.g. ExCon's) is routed verbatim. It is <c>null</c> for the <c>.fdp</c> archive path, which is not merged.</para>
/// </summary>
public record struct FileManifestResult(string SourceUnc, string RelativeDest, string? DocType = null);

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
        var exerciseId = intent.DomainPayload is ArchiveHandlerPayload p ? p.ExerciseId : Guid.Empty;
        if (exerciseId == Guid.Empty) return Task.FromResult<object?>(null);
        var exerciseIdText = exerciseId.ToString();

        var fileName = OrchestrationConstants.GetNodeRecordingFileName(_nodeId);
        var file = Path.Combine(_localTempRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseIdText, fileName);
        if (!File.Exists(file))
        {
            FdpLog<ReferenceArchiveHandler>.Warn($"[ReferenceArchiveHandler] No local .fdp at {file}; cannot report manifest.");
            return Task.FromResult<object?>(null);
        }

        var manifest = new System.Collections.Generic.List<FileManifestResult>
        {
            new FileManifestResult(
                SourceUnc:    file,
                RelativeDest: Path.Combine(OrchestrationConstants.ExercisesDirectoryName, exerciseIdText, fileName)),
        };

        var metaFile = file + ".meta.json";
        if (File.Exists(metaFile))
        {
            manifest.Add(new FileManifestResult(
                SourceUnc:    metaFile,
                RelativeDest: Path.Combine(OrchestrationConstants.ExercisesDirectoryName, exerciseIdText, fileName + ".meta.json")));
        }

        return Task.FromResult<object?>(manifest.ToArray());
    }

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <inheritdoc />
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        var exerciseId = intent.DomainPayload is ArchiveHandlerPayload p ? p.ExerciseId : Guid.Empty;
        if (exerciseId == Guid.Empty) return;
        var exerciseIdText = exerciseId.ToString();
        var fileName = OrchestrationConstants.GetNodeRecordingFileName(_nodeId);
        var file = Path.Combine(_localTempRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseIdText, fileName);
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex)
        {
            FdpLog<ReferenceArchiveHandler>.Warn($"[ReferenceArchiveHandler] Abort cleanup failed for {file}: {ex.Message}");
        }
    }
}
