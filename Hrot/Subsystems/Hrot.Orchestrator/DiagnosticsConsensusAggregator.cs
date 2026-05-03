using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Aggregates per-node <see cref="NodeOpType.CollectDiagnostics"/> response strings into a
/// single cluster-wide manifest.
///
/// <para>
/// Stores the <b>full</b> manifest (including <see cref="FileManifestEntry.SourceUnc"/>)
/// internally and returns a <b>stripped</b> manifest (SourceUnc cleared) from
/// <see cref="Aggregate"/>.  The stripped result is embedded in the DDS
/// <c>ClusterOpStatus</c> payload transmitted to ExCon.
/// </para>
///
/// <para>
/// <see cref="DiagnosticsDumpProcessManager"/> holds a reference to this aggregator
/// and calls <see cref="TakeFullManifest"/> after a successful cluster op to obtain
/// the paths needed for <c>PullToNasAsync</c>.
/// </para>
/// </summary>
public sealed class DiagnosticsConsensusAggregator : INodeResponseAggregator
{
    private static readonly JsonSerializerOptions _deserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private List<FileManifestEntry>? _lastFullManifest;

    /// <inheritdoc/>
    public NodeOpType TargetOp => NodeOpType.CollectDiagnostics;

    /// <inheritdoc/>
    public object? Aggregate(IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses)
    {
        var fullManifest = new List<FileManifestEntry>();

        foreach (var nodeDict in nodeResponses.Values)
        {
            if (!nodeDict.TryGetValue(NodeOpType.CollectDiagnostics, out var json)) continue;
            if (string.IsNullOrWhiteSpace(json)) continue;

            try
            {
                var entries = JsonSerializer.Deserialize<List<FileManifestEntry>>(json, _deserializeOptions);
                if (entries != null && entries.Count > 0)
                    fullManifest.AddRange(entries);
            }
            catch
            {
                // Malformed JSON from a node — skip without throwing.
            }
        }

        // Store full manifest for DiagnosticsDumpProcessManager.PullToNasAsync.
        _lastFullManifest = fullManifest.Count > 0 ? fullManifest : null;

        // Return stripped manifest (SourceUnc absent) for DDS ClusterOpStatus payload.
        if (_lastFullManifest == null) return null;

        return _lastFullManifest
            .Select(e => new FileManifestEntry { RelativeDest = e.RelativeDest })
            .ToList();
    }

    /// <summary>
    /// Retrieves and clears the internally-held full manifest (with SourceUnc populated).
    /// Returns <c>null</c> if <see cref="Aggregate"/> has not been called, produced no
    /// entries, or has already been drained by a previous call.
    /// </summary>
    public List<FileManifestEntry>? TakeFullManifest()
    {
        var manifest = _lastFullManifest;
        _lastFullManifest = null;
        return manifest;
    }
}
