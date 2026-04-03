using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.NED.Descriptors.Orchestration;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.Orchestrator;

// ── Step abstractions ──────────────────────────────────────────────────────────

/// <summary>A single entry in a planned transition trajectory.</summary>
public abstract class ISysOpStep { }

/// <summary>
/// Instructs all cluster nodes to transition to <see cref="TargetState"/> as part of a 2PC round.
/// </summary>
public sealed class TransitionStep : ISysOpStep
{
    public ClusterState TargetState { get; }
    public TransitionStep(ClusterState target) => TargetState = target;
}

/// <summary>
/// An out-of-band operation appended after the final <see cref="TransitionStep"/>,
/// for example a replay-seek to a specific wall-clock position or an episode management step.
/// </summary>
public sealed class OperationStep : ISysOpStep
{
    public ClusterOpType Operation    { get; }
    /// <summary>
    /// Strongly-typed payload (e.g. <c>string</c> scenarioId, <c>long</c> ticks,
    /// <see cref="EpisodeHandlerPayload"/>, etc.). Null for operations that carry no domain data.
    /// </summary>
    public object?       DomainPayload { get; }
    public OperationStep(ClusterOpType operation, object? domainPayload = null)
    {
        Operation    = operation;
        DomainPayload = domainPayload;
    }
}

// ── Planner ────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves <see cref="ClusterOpRequest"/> targets into an ordered <see cref="Queue{T}"/> of
/// <see cref="ISysOpStep"/> entries via Breadth-First Search over the cluster state machine directed graph.
/// Pure application-layer class — no DDS dependency.
/// </summary>
/// <remarks>
/// <b>Adjacency definition</b> follows CGF-1-DESIGN §4.1.  Failure-recovery edges
/// (e.g. <c>LoadingEdit → Standby</c>) are excluded from the planning graph; they are
/// automatic rollback paths triggered by node-side errors, not plannable transitions.
/// <c>ClusterState.Degraded</c> is a system-imposed state with no outgoing planning edges
/// and is therefore unreachable/invalid as a planning target.
///
/// <para>
/// BFS is delegated to <see cref="FDP.Toolkit.Orchestration.TransitionPlanner"/> using
/// the graph provided at construction time.  Use <see cref="HrotStateGraph.Build()"/>
/// to create the canonical Hrot cluster state machine graph.
/// </para>
/// </remarks>
public sealed class ClusterMasterPlanner
{
    private readonly FDP.Toolkit.Orchestration.TransitionPlanner _tkPlanner;
    private readonly ITransitionGraph _graph;

    /// <summary>
    /// Creates a planner backed by the provided transition graph.
    /// </summary>
    /// <param name="graph">
    /// Cluster state machine graph to use for BFS path-finding.
    /// Pass <see cref="HrotStateGraph.Build()"/> in production.
    /// </param>
    public ClusterMasterPlanner(ITransitionGraph graph)
    {
        _graph     = graph ?? throw new ArgumentNullException(nameof(graph));
        _tkPlanner = new FDP.Toolkit.Orchestration.TransitionPlanner(graph);
    }

    /// <summary>
    /// Returns the Cluster states directly reachable from <paramref name="current"/> via a
    /// single planning edge (i.e. one-step neighbours in the transition graph).
    /// </summary>
    /// <remarks>
    /// Used by the Orchestrator UI panel to enumerate valid next-state buttons without
    /// needing the full BFS trajectory.
    /// </remarks>
    public IReadOnlyList<ClusterState> GetReachableTargets(ClusterState current)
    {
        var neighbors = _graph.GetNeighbors((int)current);
        return neighbors.Select(i => (ClusterState)i).ToList();
    }

    /// <summary>
    /// Computes the shortest path from <paramref name="current"/> to <paramref name="target"/>
    /// using BFS over the cluster state machine graph.
    /// </summary>
    /// <returns>
    /// The list of states to visit (excluding <paramref name="current"/>), in traversal order.
    /// An empty list when <paramref name="current"/> == <paramref name="target"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no path exists (e.g. <paramref name="current"/> or <paramref name="target"/>
    /// is <c>Degraded</c>, or the graph is otherwise disconnected for the given pair).
    /// The message includes both state names.
    /// </exception>
    public IReadOnlyList<ClusterState> CalculateShortestPath(ClusterState current, ClusterState target)
    {
        try
        {
            var intPath = _tkPlanner.CalculateShortestPath((int)current, (int)target);
            return intPath.Select(i => (ClusterState)i).ToList();
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"[TransitionPlanner] No valid cluster state machine path from {current} to {target}. " +
                $"The transition '{current}' → '{target}' is not reachable in the planning graph.");
        }
    }

    /// <summary>
    /// Plans a full trajectory for a <see cref="TransitionStateIntent"/>, returning an ordered queue
    /// of steps to execute as part of the next distributed transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the resolved target is <see cref="ClusterState.OperatingReplay"/> AND
    /// <c>TargetWallTicks</c> is non-zero, an
    /// <see cref="OperationStep"/>(<see cref="ClusterOpType.ReplaySeek"/>) is appended after
    /// the final <see cref="TransitionStep"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Propagated from <see cref="CalculateShortestPath"/> when the requested path is unreachable.
    /// </exception>
    public Queue<ISysOpStep> PlanTrajectory(ClusterState current, TransitionStateIntent intent)
    {
        var targetState = (ClusterState)(int)intent.TargetState;
        var path  = CalculateShortestPath(current, targetState);
        var queue = new Queue<ISysOpStep>();

        // CGF1-S0307: If the intent carries a ScenarioId, prepend a PrefetchScenario
        // step so the StorageGateway copies scenario files to all nodes before the first
        // cluster state machine transition executes.
        if (!string.IsNullOrWhiteSpace(intent.ScenarioId))
            queue.Enqueue(new OperationStep(ClusterOpType.PrefetchScenario, intent.ScenarioId));

        foreach (var state in path)
            queue.Enqueue(new TransitionStep(state));

        // Append a ReplaySeek operation when targeting OperatingReplay with a seek hint.
        if (targetState == ClusterState.OperatingReplay && intent.TargetWallTicks != 0)
            queue.Enqueue(new OperationStep(ClusterOpType.ReplaySeek,
                new ReplaySeekPayload(intent.TargetWallTicks)));

        return queue;
    }

    /// <summary>
    /// Plans a <see cref="ClusterOpType.ManageEpisode"/> operation for an in-flight exercise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Precondition:</b> <paramref name="current"/> must be
    /// <see cref="ClusterState.OperatingLive"/>.  Any other state causes an
    /// <see cref="InvalidOperationException"/> with <c>OpStatus.InvalidState</c> semantics.
    /// </para>
    /// <para>
    /// <b>Start trajectory:</b>
    /// <list type="bullet">
    ///   <item><see cref="OperationStep"/>(<see cref="ClusterOpType.PrefetchScenario"/>, scenarioId) —
    ///         ensures episode asset files are staged on all nodes.</item>
    ///   <item><see cref="OperationStep"/>(<see cref="ClusterOpType.ManageEpisode"/>, <see cref="EpisodeHandlerPayload"/>) —
    ///         signals <c>ClusterMaster</c> to fan out <c>StartEpisode</c> to nodes.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Stop trajectory:</b>
    /// <list type="bullet">
    ///   <item><see cref="OperationStep"/>(<see cref="ClusterOpType.ManageEpisode"/>, <see cref="EpisodeHandlerPayload"/>) —
    ///         signals <c>ClusterMaster</c> to fan out <c>StopEpisode</c> to nodes.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="current"/> is not <see cref="ClusterState.OperatingLive"/>,
    /// or when the intent is missing required fields (<c>EpisodeId</c>, or
    /// <c>ScenarioId</c> for Start mode).
    /// </exception>
    public Queue<ISysOpStep> PlanManageEpisode(ClusterState current, ManageEpisodeIntent intent)
    {
        if (current != ClusterState.OperatingLive)
            throw new InvalidOperationException(
                $"[TransitionPlanner] ManageEpisode requires OperatingLive; current state is {current}.");

        if (intent.EpisodeId == Guid.Empty)
            throw new InvalidOperationException(
                "[TransitionPlanner] ManageEpisode intent missing required EpisodeId.");

        var queue = new Queue<ISysOpStep>();

        if (intent.IsStart)
        {
            if (string.IsNullOrWhiteSpace(intent.ScenarioId))
                throw new InvalidOperationException(
                    "[TransitionPlanner] ManageEpisode IsStart=true missing required ScenarioId.");

            // Prefetch episode asset files to all nodes before injection.
            queue.Enqueue(new OperationStep(ClusterOpType.PrefetchScenario, intent.ScenarioId));
            // Fan out StartEpisode to all nodes; carry intent as payload for ClusterMaster.
            queue.Enqueue(new OperationStep(ClusterOpType.ManageEpisode, intent));
        }
        else
        {
            queue.Enqueue(new OperationStep(ClusterOpType.ManageEpisode, intent));
        }

        return queue;
    }
}
