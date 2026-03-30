using Bagira.BDC.SSTD.Orchestration;

namespace Bagira.Orchestrator;

/// <summary>Skeleton distributed transaction (2PC execution arrives in later CGF phases).</summary>
public sealed class DistributedTransaction
{
    public Guid TransactionId { get; set; }
    public Guid OriginRequestId { get; set; }
    public DSMState TargetDsmState { get; set; }
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }
    public float ElapsedSeconds { get; set; }
    public float TimeoutSeconds { get; set; } = 30f;
    public bool AllowPartialSuccess { get; set; }

    /// <summary><c>true</c> when the transaction was aborted (e.g. due to mandatory-node ejection).</summary>
    public bool IsAborted { get; set; }

    /// <summary>DSM state the cluster was in immediately before this transaction started (CGF1-S0501).</summary>
    public DSMState SourceDsmState { get; set; }

    /// <summary>The SysOpRequest JSON payload that initiated this transaction (CGF1-S0501).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Per-node ResultJson from each node's final NodeOpStatus ACK, keyed by node ID (CGF1-S0501).</summary>
    public Dictionary<int, string> NodeResponses { get; } = new();

    /// <summary>Per-node ACK latency in milliseconds, keyed by node ID. Populated on commit.</summary>
    public Dictionary<int, float> NodeAckLatencyMs { get; } = new();
}
