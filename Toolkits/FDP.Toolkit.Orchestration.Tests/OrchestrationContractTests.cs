using System;
using System.Text.Json;
using FDP.Toolkit.Orchestration;

namespace FDP.Toolkit.Orchestration.Tests;

/// <summary>
/// Unit tests verifying the G0401 core contract types and their behaviour.
/// </summary>
public sealed class OrchestrationContractTests
{
    // ── OrchestrationCommand JSON round-trip ──────────────────────────────

    /// <summary>
    /// <c>OrchestrationCommand</c> must survive a JSON serialise → deserialise cycle
    /// with all fields intact.
    /// </summary>
    [Fact]
    public void OrchestrationCommand_RoundTripsJson()
    {
        var txId    = Guid.NewGuid();
        var original = new OrchestrationCommand(
            TransactionId: txId,
            TargetNodeId:  42,
            OperationId:   9,
            PayloadJson:   "{\"State\":30}");

        var json         = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OrchestrationCommand>(json);

        Assert.Equal(original.TransactionId, deserialized.TransactionId);
        Assert.Equal(original.TargetNodeId,  deserialized.TargetNodeId);
        Assert.Equal(original.OperationId,   deserialized.OperationId);
        Assert.Equal(original.PayloadJson,   deserialized.PayloadJson);
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
        Assert.False(OrchestrationStatusCode.IsError(OrchestrationStatusCode.Success),
            "Success (0) must not be an error.");
        Assert.False(OrchestrationStatusCode.IsError(OrchestrationStatusCode.InProgress),
            "InProgress (1) must not be an error.");
        Assert.True(OrchestrationStatusCode.IsError(OrchestrationStatusCode.Rejected),
            "Rejected (10) must be an error.");
        Assert.True(OrchestrationStatusCode.IsError(1001),
            "AssetNotFound (1001) must be an error.");
    }
}
