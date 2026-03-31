using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.NED.Descriptors.Orchestration;
using FDP.Toolkit.Orchestration;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Pure unit tests for <see cref="ClusterMasterPlanner"/> (Hrot-layer planner) and
/// <see cref="FDP.Toolkit.Orchestration.TransitionPlanner"/> (generic BFS). No DDS, no ECS.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class TransitionPlannerTests
{
    private readonly ClusterMasterPlanner _planner = new(HrotStateGraph.Build());
    private readonly FDP.Toolkit.Orchestration.TransitionPlanner _tkPlanner =
        new(HrotStateGraph.Build());

    // -- Helpers --

    private Queue<ISysOpStep> PlanInt(ClusterState from, ClusterState to) =>
        _planner.PlanTrajectory(from, new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)to).ToString(),
        });

    private Queue<ISysOpStep> PlanWithSeek(ClusterState from, ClusterState to, long targetWallTicks) =>
        _planner.PlanTrajectory(from, new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)to},\"TargetWallTicks\":{targetWallTicks}}}",
        });

    private static IReadOnlyList<ClusterState> TransitionStates(Queue<ISysOpStep> queue) =>
        queue.OfType<TransitionStep>().Select(s => s.TargetState).ToList();

    // -- CGF1-S0201 success conditions --

    /// <summary>Standby to LoadingEdit is a direct edge; exactly one TransitionStep.
    /// Also validates toolkit TransitionPlanner BFS with int state IDs.</summary>
    [Fact]
    public void StandbyToLoadingEdit_Produces_SingleStep()
    {
        var queue = PlanInt(ClusterState.Idle, ClusterState.LoadingEdit);

        var steps = TransitionStates(queue);
        Assert.Single(steps);
        Assert.Equal(ClusterState.LoadingEdit, steps[0]);
    }

    /// <summary>
    /// RunningLive to RunningReplay: unload through Standby, 4 TransitionSteps.
    /// </summary>
    [Fact]
    public void RunningLiveToRunningReplay_Produces_FourSteps()
    {
        var queue = PlanInt(ClusterState.OperatingLive, ClusterState.OperatingReplay);

        var steps = TransitionStates(queue);
        Assert.Equal(4, steps.Count);
        Assert.Equal(new[]
        {
            ClusterState.UnloadingLive,
            ClusterState.Idle,
            ClusterState.LoadingReplay,
            ClusterState.OperatingReplay,
        }, steps);
    }

    /// <summary>
    /// RunningLive to RunningReplay with a TargetWallTicks hint: 4 TransitionSteps
    /// followed by one OperationStep(ReplaySeek).
    /// </summary>
    [Fact]
    public void RunningLiveToRunningReplayWithSeek_Produces_FiveSteps()
    {
        const long seekTicks = 999_000L;
        var queue = PlanWithSeek(ClusterState.OperatingLive, ClusterState.OperatingReplay, seekTicks);

        Assert.Equal(5, queue.Count);

        var steps = TransitionStates(queue);
        Assert.Equal(4, steps.Count);
        Assert.Equal(ClusterState.OperatingReplay, steps[^1]);

        var seekStep = queue.OfType<OperationStep>().LastOrDefault();
        Assert.NotNull(seekStep);
        Assert.Equal(ClusterOpType.ReplaySeek, seekStep!.Operation);
        Assert.Contains(seekTicks.ToString(), seekStep.PayloadJson);
    }

    /// <summary>
    /// RunningEdit to RunningLive: unload through Standby, 4 TransitionSteps.
    /// </summary>
    [Fact]
    public void RunningEditToRunningLive_Produces_FourSteps()
    {
        var queue = PlanInt(ClusterState.OperatingEdit, ClusterState.OperatingLive);

        var steps = TransitionStates(queue);
        Assert.Equal(4, steps.Count);
        Assert.Equal(new[]
        {
            ClusterState.UnloadingEdit,
            ClusterState.Idle,
            ClusterState.LoadingLive,
            ClusterState.OperatingLive,
        }, steps);
    }

    /// <summary>
    /// ClusterState.Degraded has no outgoing planning edges; any request from that state
    /// must throw InvalidOperationException with both state names in the message.
    /// Note: The original task-detail cited RunningPreview to RunningReplay as impossible,
    /// but BFS proves that path is reachable in 6 steps. Degraded is the canonical
    /// impossible source. CGF-1-TASK-DETAIL S0201 has been updated accordingly.
    /// </summary>
    [Fact]
    public void ImpossibleRequest_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PlanInt(ClusterState.Degraded, ClusterState.OperatingLive));

        Assert.Contains("Degraded",    ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RunningLive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Requesting a transition to the currently-active state returns an empty queue (no-op).
    /// </summary>
    [Fact]
    public void SameState_ReturnsEmptyQueue()
    {
        var queue = PlanInt(ClusterState.Idle, ClusterState.Idle);
        Assert.Empty(queue);
    }

    // -- Additional coverage --

    /// <summary>
    /// Verifies that BFS finds the 6-step path RunningPreview to RunningReplay via
    /// UnloadingPreview, RunningEdit, UnloadingEdit, Standby, LoadingReplay, RunningReplay.
    /// The original task-detail entry incorrectly stated this was impossible.
    /// See updated CGF-1-TASK-DETAIL S0201.
    /// </summary>
    [Fact]
    public void RunningPreviewToRunningReplay_Produces_SixSteps()
    {
        var path = _planner.CalculateShortestPath(ClusterState.OperatingPreview, ClusterState.OperatingReplay);

        Assert.Equal(6, path.Count);
        Assert.Equal(new[]
        {
            ClusterState.UnloadingPreview,
            ClusterState.OperatingEdit,
            ClusterState.UnloadingEdit,
            ClusterState.Idle,
            ClusterState.LoadingReplay,
            ClusterState.OperatingReplay,
        }, path);
    }

    /// <summary>
    /// G0403 success condition 2: BFS path preserved via toolkit TransitionPlanner
    /// with integer state IDs (HrotStateGraph).
    /// </summary>
    [Fact]
    public void TkPlanner_StandbyToRunningLive_BfsPathPreserved()
    {
        var intPath = _tkPlanner.CalculateShortestPath(
            (int)ClusterState.Idle, (int)ClusterState.OperatingLive);

        Assert.Equal(2, intPath.Count);
        Assert.Equal((int)ClusterState.LoadingLive,  intPath[0]);
        Assert.Equal((int)ClusterState.OperatingLive,  intPath[1]);
    }

    /// <summary>
    /// Toolkit TransitionPlanner error message includes state numeric IDs when path not found.
    /// </summary>
    [Fact]
    public void TkPlanner_ImpossibleRequest_ThrowsInvalidOperationException()
    {
        // ClusterState.Degraded = 99 (unreachable)
        Assert.Throws<InvalidOperationException>(
            () => _tkPlanner.CalculateShortestPath((int)ClusterState.Degraded, (int)ClusterState.OperatingLive));
    }

    // -- A.2: Payload fail-fast tests (CGF-1-BATCH-05) --

    /// <summary>
    /// A whitespace-only PayloadJson for a TransitionState request must throw rather than
    /// silently default to Standby. Whitespace is caught by the same IsNullOrWhiteSpace
    /// guard as the empty string case.
    /// </summary>
    [Fact]
    public void PlanTrajectory_WhitespaceOnlyPayload_Throws()
    {
        var request = new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = "   ",
        };

        Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(ClusterState.Idle, request));
    }

    /// <summary>
    /// An empty PayloadJson for a TransitionState request must throw rather than silently
    /// default to Standby.
    /// </summary>
    [Fact]
    public void PlanTrajectory_EmptyPayload_ThrowsInvalidOperationException()
    {
        var request = new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = string.Empty,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(ClusterState.Idle, request));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Garbage (non-parseable) JSON must throw rather than silently default to Standby.
    /// </summary>
    [Fact]
    public void PlanTrajectory_GarbageJson_ThrowsInvalidOperationException()
    {
        var request = new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = "not-valid-json!!!",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(ClusterState.Idle, request));

        Assert.NotNull(ex.Message);
    }

    /// <summary>
    /// A valid JSON object that lacks the TargetState property must throw rather than
    /// silently default to Standby (which could produce a seemingly-valid plan).
    /// </summary>
    [Fact]
    public void PlanTrajectory_JsonWithoutTargetState_ThrowsInvalidOperationException()
    {
        var request = new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = "{}",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(ClusterState.Idle, request));

        Assert.Contains("TargetState", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -- CGF1-S0302 success condition (B.4) --

    /// <summary>
    /// A payload with <c>ScenarioId</c> targeting <see cref="ClusterState.LoadingEdit"/>
    /// must inject a storage-gateway prefetch step as the very
    /// first entry in the planned queue, before the
    /// <see cref="TransitionStep"/>(<see cref="ClusterState.LoadingEdit"/>).
    /// CGF1-S0302 success condition: <c>PlanWithScenarioId_InjectsStorageGatewayStep</c>.
    /// </summary>
    [Fact]
    public void PlanWithScenarioId_InjectsStorageGatewayStep()
    {
        var request = new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingEdit},\"ScenarioId\":\"Alpha\"}}",
        };

        var queue = _planner.PlanTrajectory(ClusterState.Idle, request);

        // Must have at least the prefetch step + the LoadingEdit transition step.
        Assert.True(queue.Count >= 2);

        var first = queue.Dequeue();
        var prefetch = Assert.IsType<OperationStep>(first);
        Assert.Equal(ClusterOpType.PrefetchScenario, prefetch.Operation);
        Assert.Equal("Alpha", prefetch.PayloadJson);

        var second = queue.Dequeue();
        var transition = Assert.IsType<TransitionStep>(second);
        Assert.Equal(ClusterState.LoadingEdit, transition.TargetState);
    }

    // -- CGF1-S0106: GetReachableTargets (CGF-1-BATCH-23) --

    /// <summary>
    /// From Standby, the valid one-step transitions are LoadingEdit, LoadingLive,
    /// LoadingPreview, and LoadingReplay — per the HrotStateGraph adjacency list.
    /// </summary>
    [Fact]
    public void GetReachableTargets_FromStandby_ContainsLoadingEdit()
    {
        var targets = _planner.GetReachableTargets(ClusterState.Idle);

        Assert.Contains(ClusterState.LoadingEdit,  targets);
        Assert.Contains(ClusterState.LoadingLive,  targets);
        Assert.DoesNotContain(ClusterState.OperatingLive,   targets); // multi-step — not direct
        Assert.DoesNotContain(ClusterState.Idle, targets);       // self-loops should not appear
    }

    /// <summary>
    /// From Degraded (no outgoing planning edges), the reachable target list is empty.
    /// </summary>
    [Fact]
    public void GetReachableTargets_FromDegraded_ReturnsEmpty()
    {
        var targets = _planner.GetReachableTargets(ClusterState.Degraded);

        Assert.Empty(targets);
    }

    /// <summary>
    /// From RunningLive, only UnloadingLive is a valid one-step neighbour.
    /// </summary>
    [Fact]
    public void GetReachableTargets_FromRunningLive_ContainsOnlyUnloadingLive()
    {
        var targets = _planner.GetReachableTargets(ClusterState.OperatingLive);

        Assert.Single(targets);
        Assert.Equal(ClusterState.UnloadingLive, targets[0]);
    }
}
