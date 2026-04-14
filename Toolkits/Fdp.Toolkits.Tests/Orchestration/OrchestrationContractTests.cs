using System;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace FDP.Toolkit.Orchestration.Tests;

/// <summary>
/// Unit tests verifying the G0401 core contract types and their behaviour.
/// </summary>
public sealed class OrchestrationContractTests
{
    // ── ExecuteNodeOpIntent construction ──────────────────────────────────

    /// <summary>
    /// <c>ExecuteNodeOpIntent</c> must be constructable with all fields set.
    /// Verifies the new CQRS intent type replaces OrchestrationCommand.
    /// </summary>
    [Fact]
    public void ExecuteNodeOpIntent_CanBeConstructed()
    {
        var txId   = Guid.NewGuid();
        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 42,
            Operation     = NodeOpType.PrepareLive,
            DomainPayload = "test-scenario",
        };

        Assert.Equal(txId,                    intent.TransactionId);
        Assert.Equal(42,                      intent.TargetNodeId);
        Assert.Equal(NodeOpType.PrepareLive,  intent.Operation);
        Assert.Equal("test-scenario",         intent.DomainPayload);
    }

    // ── TransitionGraphBuilder ────────────────────────────────────────────

    /// <summary>
    /// A graph built with <c>AddTransition(0,1).AddTransition(1,2)</c> must
    /// report correct neighbour lists and state set.
    /// </summary>
    [Fact]
    public void TransitionGraphBuilder_BuildsValidGraph()
    {
        var graph = new TransitionGraphBuilder()
            .AddTransition(0, 1)
            .AddTransition(1, 2)
            .Build();

        Assert.Equal(new[] { 1 }, graph.GetNeighbors(0));
        Assert.Equal(new[] { 2 }, graph.GetNeighbors(1));
        Assert.Empty(graph.GetNeighbors(2));   // terminal — no outgoing edges
        Assert.Contains(0, graph.AllStates);
        Assert.Contains(1, graph.AllStates);
        Assert.Contains(2, graph.AllStates);
    }

    // ── OrchestrationStatusCode ───────────────────────────────────────────

    /// <summary>
    /// <c>IsError(0)</c> must return <c>false</c> (Success is not an error).
    /// <c>IsError(Rejected)</c> and <c>IsError(1001)</c> must return <c>true</c>.
    /// </summary>
    [Fact]
    public void OrchestrationStatusCode_IsError_CorrectlyCategorises()
    {
        Assert.False(OrchestrationStatusCode.Success.IsError(),
            "Success (0) must not be an error.");
        Assert.False(OrchestrationStatusCode.InProgress.IsError(),
            "InProgress (1) must not be an error.");
        Assert.True(OrchestrationStatusCode.Rejected.IsError(),
            "Rejected (10) must be an error.");
        Assert.True(((OrchestrationStatusCode)1001).IsError(),
            "AssetNotFound (1001) must be an error.");
    }
}
