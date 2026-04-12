namespace Hrot.Core.Network;

/// <summary>
/// Neutral interface for ExCon-originated time-control requests sent to the Orchestrator.
/// Replaces the direct <c>IDdsWriter&lt;ClusterOpRequest&gt;</c> field in ExConLogic.
/// </summary>
public interface ITimeControlGateway
{
    /// <summary>Requests the cluster to pause simulation time.</summary>
    void RequestPause();

    /// <summary>Requests the cluster to resume simulation time.</summary>
    void RequestResume();

    /// <summary>Requests the cluster to advance exactly one step.</summary>
    void RequestStep();

    /// <summary>Requests a time-scale change.</summary>
    void SetTimeScale(float scale);
}
