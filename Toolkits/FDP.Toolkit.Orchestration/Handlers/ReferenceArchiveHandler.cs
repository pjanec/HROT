using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using FDP.Kernel.Logging;

namespace FDP.Toolkit.Orchestration.Handlers;

/// <summary>
/// Node-side archive handler (CGF1-S0505).
/// Responds to <see cref="SerializeLocalOperationId"/> commands whose
/// <c>PayloadJson</c> contains a <c>"ExerciseId"</c> key.
/// When committed, it reads the per-node <c>.fdp</c> file and publishes an
/// <see cref="OrchestrationStatus"/> whose <c>ResultJson</c> contains a serialised
/// manifest array so that <c>ClusterMaster.ConsumeNodeOpStatuses</c> can pull the
/// file to the central NAS.
/// </summary>
public sealed class ReferenceArchiveHandler : IClusterStateHandler
{
    /// <summary>Integer value of <c>NodeOpType.SerializeLocal</c>.</summary>
    public const int SerializeLocalOperationId = 15;

    private readonly IOrchestrationTransport? _transport;
    private readonly string _localTempRoot;
    private readonly int    _nodeId;

    public ReferenceArchiveHandler(
        IOrchestrationTransport? transport,
        string                   localTempRoot,
        int                      nodeId)
    {
        _transport     = transport;
        _localTempRoot = localTempRoot ?? throw new ArgumentNullException(nameof(localTempRoot));
        _nodeId        = nodeId;
    }

    /// <inheritdoc />
    public bool CanHandle(int operationId)
        => operationId == SerializeLocalOperationId;

    /// <inheritdoc />
    public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
    {
        string? exerciseId = ParseExerciseId(cmd.PayloadJson);
        if (exerciseId is null) return;  // payload is not an archive request; skip

        var file = Path.Combine(_localTempRoot, exerciseId, $"node_{_nodeId}.fdp");
        if (!File.Exists(file))
        {
            FdpLog<ReferenceArchiveHandler>.Warn($"[ReferenceArchiveHandler] No local .fdp at {file}; cannot report manifest.");
            return;
        }

        // Serialise as a JSON array matching the FileManifestEntry wire shape so that
        // ClusterMaster.ConsumeNodeOpStatuses can deserialise it back to FileManifestEntry[].
        var resultJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                SourceUnc    = file,
                RelativeDest = Path.Combine(exerciseId, $"node_{_nodeId}.fdp"),
            }
        });

        _transport?.PublishStatus(new OrchestrationStatus(
            TransactionId:   cmd.TransactionId,
            NodeId:          _nodeId,
            StatusCode:      OrchestrationStatusCode.Success,
            IsParticipating: true,
            ResultJson:      resultJson));
    }

    /// <inheritdoc />
    public void Abort(OrchestrationCommand cmd, EntityRepository? repo)
    {
        string? exerciseId = ParseExerciseId(cmd.PayloadJson);
        if (exerciseId is null) return;
        var file = Path.Combine(_localTempRoot, exerciseId, $"node_{_nodeId}.fdp");
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex)
        {
            FdpLog<ReferenceArchiveHandler>.Warn($"[ReferenceArchiveHandler] Abort cleanup failed for {file}: {ex.Message}");
        }
    }

    private static string? ParseExerciseId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ExerciseId", out var prop))
                return prop.GetString();
        }
        catch (JsonException) { /* not a JSON object; not our payload */ }
        return null;
    }
}
