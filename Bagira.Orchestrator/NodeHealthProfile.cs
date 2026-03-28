using Bagira.BDC.SSTD.Orchestration;

namespace Bagira.Orchestrator;

/// <summary>Latest heartbeat view for a cluster node.</summary>
public sealed class NodeHealthProfile
{
    public int NodeId { get; set; }
    public string SubsystemName { get; set; } = string.Empty;
    public DSMState LocalDsmState { get; set; }
    public double LastHeartbeatUtcSeconds { get; set; }
}
