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
}
