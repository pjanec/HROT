using System.Collections.Generic;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Aggregates per-node <see cref="NodeOpType.SerializeLocal"/> response strings into a
/// single flattened <see cref="List{FileManifestEntry}"/> that collects all per-node
/// manifest entries into one cluster-wide manifest.
///
/// <para>
/// Registered with <see cref="ClusterMaster"/> so the orchestrator can reduce
/// distributed snapshot manifests into a single list before passing it to
/// <see cref="StorageGatewayModule.PullToNasAsync"/>.
/// </para>
/// </summary>
public sealed class StorageConsensusAggregator : INodeResponseAggregator
{
    private static readonly JsonSerializerOptions _deserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.SerializeLocal;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        var flatManifest = new List<FileManifestEntry>();

        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.SerializeLocal, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                var entries = JsonSerializer.Deserialize<List<FileManifestEntry>>(json, _deserializeOptions);
                if (entries != null && entries.Count > 0)
                {
                    flatManifest.AddRange(entries);
                }
            }
            catch
            {
                // Malformed JSON — skip this node's payload without throwing.
            }
        }

        return flatManifest.Count > 0 ? flatManifest : null;
    }
}
