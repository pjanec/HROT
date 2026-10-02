using System.Collections.Generic;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>Latest heartbeat view for a cluster node.</summary>
public sealed class NodeHealthProfile
{
    public int NodeId { get; set; }
    public string SubsystemName { get; set; } = string.Empty;
    public ClusterState LocalClusterState { get; set; }
    public double LastHeartbeatUtcSeconds { get; set; }

    /// <summary>CE-286 (C-roles): the node's declared <see cref="NodeRole"/> mask ([Flags], possibly
    /// multi-role), <b>DERIVED</b> from the <c>fdp.role.*</c> subset of <see cref="Capabilities"/> via
    /// <see cref="NodeRoleTokens"/> at ingest (AQ-70 §Q70-C — supersedes CE-282's heartbeat mask).
    /// <see cref="NodeRole.None"/> for a node that advertises no role token (e.g. the orchestrator).
    /// Queried by role via <see cref="NodeRoster.NodesWithRole"/> (P3).</summary>
    public NodeRole Roles { get; set; }

    /// <summary>CE-285 (C-cap): the node's static capability token set (OpenGL-extension-style namespaced),
    /// gathered from the durable <c>NodeCapabilities</c> descriptor. Empty until the node's capabilities
    /// advertisement is seen. Queried via <see cref="NodeRoster.Supports"/>.</summary>
    public IReadOnlySet<string> Capabilities { get; set; } = new HashSet<string>();

    /// <summary>CPU utilisation reported by the node in the last heartbeat (0–100 %).</summary>
    public float CpuUsagePercent { get; set; }

    /// <summary>Process RSS / working set reported by the node in the last heartbeat (bytes).</summary>
    public long RamUsedBytes { get; set; }
}
