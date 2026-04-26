using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>Skeleton distributed transaction (2PC execution arrives in later CGF phases).</summary>
public sealed class DistributedTransaction
{
    public Guid TransactionId { get; set; }
    public Guid OriginRequestId { get; set; }
    public ClusterState TargetDsmState { get; set; }
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }
    public float ElapsedSeconds { get; set; }
    public float TimeoutSeconds { get; set; } = 30f;
    public bool AllowPartialSuccess { get; set; }

    /// <summary><c>true</c> when the transaction was aborted (e.g. due to mandatory-node ejection).</summary>
    public bool IsAborted { get; set; }

    /// <summary>Cluster state the cluster was in immediately before this transaction started (CGF1-S0501).</summary>
    public ClusterState SourceDsmState { get; set; }

    /// <summary>The ClusterOpRequest JSON payload that initiated this transaction (CGF1-S0501).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Per-node ResultJson from each node's ACK for each operation, keyed by node ID then operation type (CGF1-S0501).</summary>
    public Dictionary<int, Dictionary<Fdp.Toolkit.Orchestration.NodeOpType, string>> NodeResponses { get; } = new();

    /// <summary>Per-node ACK latency in milliseconds, keyed by node ID. Populated on commit.</summary>
    public Dictionary<int, float> NodeAckLatencyMs { get; } = new();

    /// <summary>
    /// <c>true</c> when the operation completed with <c>OrchestrationStatusCode.Success</c>.
    /// Set by <c>ClusterUiCache.DrainSysOpStatus()</c> when tracking 2PC traffic over DDS
    /// (CGF1-S0506). Distinct from <see cref="IsAborted"/> which is set by ClusterMaster locally.
    /// </summary>
    public bool Completed { get; set; }
}
