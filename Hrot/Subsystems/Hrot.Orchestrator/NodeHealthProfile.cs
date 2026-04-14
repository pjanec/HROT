using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>Latest heartbeat view for a cluster node.</summary>
public sealed class NodeHealthProfile
{
    public int NodeId { get; set; }
    public string SubsystemName { get; set; } = string.Empty;
    public ClusterState LocalClusterState { get; set; }
    public double LastHeartbeatUtcSeconds { get; set; }

    /// <summary>CPU utilisation reported by the node in the last heartbeat (0–100 %).</summary>
    public float CpuUsagePercent { get; set; }

    /// <summary>Process RSS / working set reported by the node in the last heartbeat (bytes).</summary>
    public long RamUsedBytes { get; set; }
}
