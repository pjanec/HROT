using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// Builds the <see cref="ITransitionGraph"/> for the Hrot Cluster State Machine using all
/// valid forward planning edges from CGF-1-DESIGN §4.1.
///
/// <para>
/// Failure-recovery edges (e.g. <c>LoadingEdit → Standby</c>) are intentionally excluded;
/// they are automatic rollback paths triggered by node-side errors, not plannable
/// transitions.  <see cref="ClusterState.Degraded"/> has no outgoing planning edges.
/// </para>
///
/// <para>
/// <b>Note on <c>RunningEdit → LoadingLive</c>:</b> this edge is absent even though the
/// design adjacency list includes it.  Routing requires unloading the Edit session before
/// starting a Live session; the edge is considered a documentation error in the design.
/// </para>
/// </summary>
public static class HrotStateGraph
{
    /// <summary>
    /// Constructs and returns the canonical Hrot cluster transition graph.
    /// </summary>
    public static ITransitionGraph Build()
    {
        return new TransitionGraphBuilder()
            // Standby outgoing
            .AddTransition((int)ClusterState.Idle,         (int)ClusterState.LoadingEdit)
            .AddTransition((int)ClusterState.Idle,         (int)ClusterState.LoadingLive)
            .AddTransition((int)ClusterState.Idle,         (int)ClusterState.LoadingReplay)
            // Edit cycle
            .AddTransition((int)ClusterState.LoadingEdit,     (int)ClusterState.OperatingEdit)
            .AddTransition((int)ClusterState.OperatingEdit,     (int)ClusterState.LoadingPreview)
            .AddTransition((int)ClusterState.OperatingEdit,     (int)ClusterState.UnloadingEdit)
            .AddTransition((int)ClusterState.LoadingPreview,   (int)ClusterState.OperatingPreview)
            .AddTransition((int)ClusterState.OperatingPreview,   (int)ClusterState.UnloadingPreview)
            .AddTransition((int)ClusterState.UnloadingPreview, (int)ClusterState.OperatingEdit)
            .AddTransition((int)ClusterState.UnloadingEdit,   (int)ClusterState.Idle)
            // Live cycle
            .AddTransition((int)ClusterState.LoadingLive,     (int)ClusterState.OperatingLive)
            .AddTransition((int)ClusterState.OperatingLive,     (int)ClusterState.UnloadingLive)
            .AddTransition((int)ClusterState.UnloadingLive,   (int)ClusterState.Idle)
            // Replay cycle
            .AddTransition((int)ClusterState.LoadingReplay,   (int)ClusterState.OperatingReplay)
            .AddTransition((int)ClusterState.OperatingReplay,   (int)ClusterState.UnloadingReplay)
            .AddTransition((int)ClusterState.OperatingReplay,   (int)ClusterState.LoadingLive)
            .AddTransition((int)ClusterState.UnloadingReplay, (int)ClusterState.Idle)
            .Build();
    }
}
