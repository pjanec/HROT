using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NodeOpType   = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for <see cref="DistributedTransaction"/> default values and new fields
/// added in CGF1-S0501 (BATCH-26).
/// </summary>
public sealed class DistributedTransactionTests
{
    [Fact]
    public void NewTransaction_HasDefaultValues()
    {
        var tx = new DistributedTransaction();

        Assert.Equal(ClusterState.Idle, tx.SourceDsmState);
        Assert.Equal(string.Empty, tx.PayloadJson);
        Assert.Empty(tx.NodeResponses);
    }

    [Fact]
    public void NodeResponses_AcceptsEntries()
    {
        var tx = new DistributedTransaction();
        tx.NodeResponses[1] = new Dictionary<NodeOpType, string> { [NodeOpType.PrepareReplay] = "{\"Status\":\"ok\"}" };
        tx.NodeResponses[2] = new Dictionary<NodeOpType, string> { [NodeOpType.CommitState]  = "{\"Status\":\"error\"}" };

        Assert.Equal(2, tx.NodeResponses.Count);
        Assert.Equal("{\"Status\":\"ok\"}", tx.NodeResponses[1][NodeOpType.PrepareReplay]);
    }

    [Fact]
    public void PayloadJson_RoundTrips()
    {
        const string payload = "{\"TargetState\":30}";
        var tx = new DistributedTransaction { PayloadJson = payload };
        Assert.Equal(payload, tx.PayloadJson);
    }

    [Fact]
    public void SourceDsmState_CanBeSet()
    {
        var tx = new DistributedTransaction { SourceDsmState = ClusterState.OperatingLive };
        Assert.Equal(ClusterState.OperatingLive, tx.SourceDsmState);
    }
}
