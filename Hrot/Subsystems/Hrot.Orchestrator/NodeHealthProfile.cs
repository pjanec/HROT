using System.Collections.Generic;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>Latest heartbeat view for a cluster node.</summary>
public sealed class NodeHealthProfile
{
    public int NodeId { get; set; }
    public string SubsystemName { get; set; } = string.Empty;

    /// <summary>⭐ <c>CE-3354</c> ② — the node's unique human-given name from its heartbeat (never empty on the roster: a sender with none gets <c>&lt;subsystem&gt;-&lt;nodeId&gt;</c>).</summary>
    public string NodeName { get; set; } = string.Empty;
    public ClusterState LocalClusterState { get; set; }
    public double LastHeartbeatUtcSeconds { get; set; }
    /// <summary>⭐ C2 (R-4006 I5) — the cluster session the node reports following; empty until it has seen one.</summary>
    public Guid SessionId { get; set; }

    /// <summary>⭐ <c>CE-3345</c> (AQ110 A) — what the node last reported about ITSELF: its phase (Ready · Booting · Loading · CatchingUp · PlayReady · Failed). The master combines the participants' phases into cluster play-ready.</summary>
    public Fdp.Toolkit.Orchestration.NodePhase Phase { get; set; }

    /// <summary>⭐ <c>CE-3345</c> — the load step the node is on while Loading / CatchingUp (empty otherwise).</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>⭐ <c>CE-3345</c> — why the node's last prepare failed while Failed (empty otherwise).</summary>
    public string Reason { get; set; } = string.Empty;

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
