using System;
using System.Collections.Generic;
using System.Linq;
using Bagira.BDC.SSTD.Orchestration;

namespace Bagira.Orchestrator.Tests;

/// <summary>
/// Pure unit tests for <see cref="TransitionPlanner"/> -- no DDS, no ECS.
/// All tests are deterministic and run in-process only.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class TransitionPlannerTests
{
    private readonly TransitionPlanner _planner = new();

    // -- Helpers --

    private Queue<ISysOpStep> PlanInt(DSMState from, DSMState to) =>
        _planner.PlanTrajectory(from, new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)to).ToString(),
        });

    private Queue<ISysOpStep> PlanWithSeek(DSMState from, DSMState to, long targetWallTicks) =>
        _planner.PlanTrajectory(from, new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)to},\"TargetWallTicks\":{targetWallTicks}}}",
        });

    private static IReadOnlyList<DSMState> TransitionStates(Queue<ISysOpStep> queue) =>
        queue.OfType<TransitionStep>().Select(s => s.TargetState).ToList();

    // -- CGF1-S0201 success conditions --

    /// <summary>Standby to LoadingEdit is a direct edge; exactly one TransitionStep.</summary>
    [Fact]
    public void StandbyToLoadingEdit_Produces_SingleStep()
    {
        var queue = PlanInt(DSMState.Standby, DSMState.LoadingEdit);

        var steps = TransitionStates(queue);
        Assert.Single(steps);
        Assert.Equal(DSMState.LoadingEdit, steps[0]);
    }

    /// <summary>
    /// RunningLive to RunningReplay: unload through Standby, 4 TransitionSteps.
    /// </summary>
    [Fact]
    public void RunningLiveToRunningReplay_Produces_FourSteps()
    {
        var queue = PlanInt(DSMState.RunningLive, DSMState.RunningReplay);

        var steps = TransitionStates(queue);
        Assert.Equal(4, steps.Count);
        Assert.Equal(new[]
        {
            DSMState.UnloadingLive,
            DSMState.Standby,
            DSMState.LoadingReplay,
            DSMState.RunningReplay,
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
        var queue = PlanWithSeek(DSMState.RunningLive, DSMState.RunningReplay, seekTicks);

        Assert.Equal(5, queue.Count);

        var steps = TransitionStates(queue);
        Assert.Equal(4, steps.Count);
        Assert.Equal(DSMState.RunningReplay, steps[^1]);

        var seekStep = queue.OfType<OperationStep>().LastOrDefault();
        Assert.NotNull(seekStep);
        Assert.Equal(SysOpType.ReplaySeek, seekStep!.Operation);
        Assert.Contains(seekTicks.ToString(), seekStep.PayloadJson);
    }

    /// <summary>
    /// RunningEdit to RunningLive: unload through Standby, 4 TransitionSteps.
    /// </summary>
    [Fact]
    public void RunningEditToRunningLive_Produces_FourSteps()
    {
        var queue = PlanInt(DSMState.RunningEdit, DSMState.RunningLive);

        var steps = TransitionStates(queue);
        Assert.Equal(4, steps.Count);
        Assert.Equal(new[]
        {
            DSMState.UnloadingEdit,
            DSMState.Standby,
            DSMState.LoadingLive,
            DSMState.RunningLive,
        }, steps);
    }

    /// <summary>
    /// DSMState.Degraded has no outgoing planning edges; any request from that state
    /// must throw InvalidOperationException with both state names in the message.
    /// Note: The original task-detail cited RunningDryRun to RunningReplay as impossible,
    /// but BFS proves that path is reachable in 6 steps. Degraded is the canonical
    /// impossible source. CGF-1-TASK-DETAIL S0201 has been updated accordingly.
    /// </summary>
    [Fact]
    public void ImpossibleRequest_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PlanInt(DSMState.Degraded, DSMState.RunningLive));

        Assert.Contains("Degraded",    ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RunningLive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Requesting a transition to the currently-active state returns an empty queue (no-op).
    /// </summary>
    [Fact]
    public void SameState_ReturnsEmptyQueue()
    {
        var queue = PlanInt(DSMState.Standby, DSMState.Standby);
        Assert.Empty(queue);
    }

    // -- Additional coverage --

    /// <summary>
    /// Verifies that BFS finds the 6-step path RunningDryRun to RunningReplay via
    /// UnloadingDryRun, RunningEdit, UnloadingEdit, Standby, LoadingReplay, RunningReplay.
    /// The original task-detail entry incorrectly stated this was impossible.
    /// See updated CGF-1-TASK-DETAIL S0201.
    /// </summary>
    [Fact]
    public void RunningDryRunToRunningReplay_Produces_SixSteps()
    {
        var path = _planner.CalculateShortestPath(DSMState.RunningDryRun, DSMState.RunningReplay);

        Assert.Equal(6, path.Count);
        Assert.Equal(new[]
        {
            DSMState.UnloadingDryRun,
            DSMState.RunningEdit,
            DSMState.UnloadingEdit,
            DSMState.Standby,
            DSMState.LoadingReplay,
            DSMState.RunningReplay,
        }, path);
    }

    /// <summary>
    /// DSMState.Degraded as the target is also unreachable from any normal state
    /// (no incoming planning edges exist for Degraded).
    /// </summary>
    [Fact]
    public void TransitionToDegraded_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.CalculateShortestPath(DSMState.Standby, DSMState.Degraded));

        Assert.Contains("Degraded", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Standby",  ex.Message, StringComparison.OrdinalIgnoreCase);
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
        var request = new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = "   ",
        };

        Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(DSMState.Standby, request));
    }

    /// <summary>
    /// An empty PayloadJson for a TransitionState request must throw rather than silently
    /// default to Standby.
    /// </summary>
    [Fact]
    public void PlanTrajectory_EmptyPayload_ThrowsInvalidOperationException()
    {
        var request = new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = string.Empty,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(DSMState.Standby, request));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Garbage (non-parseable) JSON must throw rather than silently default to Standby.
    /// </summary>
    [Fact]
    public void PlanTrajectory_GarbageJson_ThrowsInvalidOperationException()
    {
        var request = new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = "not-valid-json!!!",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(DSMState.Standby, request));

        Assert.NotNull(ex.Message);
    }

    /// <summary>
    /// A valid JSON object that lacks the TargetState property must throw rather than
    /// silently default to Standby (which could produce a seemingly-valid plan).
    /// </summary>
    [Fact]
    public void PlanTrajectory_JsonWithoutTargetState_ThrowsInvalidOperationException()
    {
        var request = new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = "{}",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => _planner.PlanTrajectory(DSMState.Standby, request));

        Assert.Contains("TargetState", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -- CGF1-S0302 success condition (B.4) --

    /// <summary>
    /// A payload with <c>ScenarioId</c> targeting <see cref="DSMState.LoadingEdit"/>
    /// must inject a storage-gateway prefetch step as the very
    /// first entry in the planned queue, before the
    /// <see cref="TransitionStep"/>(<see cref="DSMState.LoadingEdit"/>).
    /// CGF1-S0302 success condition: <c>PlanWithScenarioId_InjectsStorageGatewayStep</c>.
    /// </summary>
    [Fact]
    public void PlanWithScenarioId_InjectsStorageGatewayStep()
    {
        var request = new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)DSMState.LoadingEdit},\"ScenarioId\":\"Alpha\"}}",
        };

        var queue = _planner.PlanTrajectory(DSMState.Standby, request);

        // Must have at least the prefetch step + the LoadingEdit transition step.
        Assert.True(queue.Count >= 2);

        var first = queue.Dequeue();
        var prefetch = Assert.IsType<OperationStep>(first);
        Assert.Equal(SysOpType.PrefetchScenario, prefetch.Operation);
        Assert.Equal("Alpha", prefetch.PayloadJson);

        var second = queue.Dequeue();
        var transition = Assert.IsType<TransitionStep>(second);
        Assert.Equal(DSMState.LoadingEdit, transition.TargetState);
    }
}
