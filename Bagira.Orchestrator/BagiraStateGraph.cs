using Bagira.BDC.SSTD.Orchestration;
using FDP.Toolkit.Orchestration;

namespace Bagira.Orchestrator;

/// <summary>
/// Builds the <see cref="ITransitionGraph"/> for the Bagira Drill State Machine using all
/// valid forward planning edges from CGF-1-DESIGN §4.1.
///
/// <para>
/// Failure-recovery edges (e.g. <c>LoadingEdit → Standby</c>) are intentionally excluded;
/// they are automatic rollback paths triggered by node-side errors, not plannable
/// transitions.  <see cref="DSMState.Degraded"/> has no outgoing planning edges.
/// </para>
///
/// <para>
/// <b>Note on <c>RunningEdit → LoadingLive</c>:</b> this edge is absent even though the
/// design adjacency list includes it.  Routing requires unloading the Edit session before
/// starting a Live session; the edge is considered a documentation error in the design.
/// </para>
/// </summary>
public static class BagiraStateGraph
{
    /// <summary>
    /// Constructs and returns the canonical Bagira DSM transition graph.
    /// </summary>
    public static ITransitionGraph Build()
    {
        return new TransitionGraphBuilder()
            // Standby outgoing
            .AddTransition((int)DSMState.Standby,         (int)DSMState.LoadingEdit)
            .AddTransition((int)DSMState.Standby,         (int)DSMState.LoadingLive)
            .AddTransition((int)DSMState.Standby,         (int)DSMState.LoadingReplay)
            // Edit cycle
            .AddTransition((int)DSMState.LoadingEdit,     (int)DSMState.RunningEdit)
            .AddTransition((int)DSMState.RunningEdit,     (int)DSMState.LoadingDryRun)
            .AddTransition((int)DSMState.RunningEdit,     (int)DSMState.UnloadingEdit)
            .AddTransition((int)DSMState.LoadingDryRun,   (int)DSMState.RunningDryRun)
            .AddTransition((int)DSMState.RunningDryRun,   (int)DSMState.UnloadingDryRun)
            .AddTransition((int)DSMState.UnloadingDryRun, (int)DSMState.RunningEdit)
            .AddTransition((int)DSMState.UnloadingEdit,   (int)DSMState.Standby)
            // Live cycle
            .AddTransition((int)DSMState.LoadingLive,     (int)DSMState.RunningLive)
            .AddTransition((int)DSMState.RunningLive,     (int)DSMState.UnloadingLive)
            .AddTransition((int)DSMState.UnloadingLive,   (int)DSMState.Standby)
            // Replay cycle
            .AddTransition((int)DSMState.LoadingReplay,   (int)DSMState.RunningReplay)
            .AddTransition((int)DSMState.RunningReplay,   (int)DSMState.UnloadingReplay)
            .AddTransition((int)DSMState.RunningReplay,   (int)DSMState.LoadingLive)
            .AddTransition((int)DSMState.UnloadingReplay, (int)DSMState.Standby)
            .Build();
    }
}
