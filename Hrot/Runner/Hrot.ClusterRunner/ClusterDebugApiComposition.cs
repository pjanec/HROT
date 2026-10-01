namespace Hrot.Runner;

/// <summary>
/// ⭐ <b>The cluster debug API's composition, ONCE</b>: <c>Program.Main</c> and the rails that boot a real cluster
/// (<c>TheClusterAiDebugSurfaceAnswersTests</c>) both call it, so a rail cannot pass against a hand copy that has
/// drifted from what the node actually wires. 📄 <c>docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md</c> §2 D1.
/// </summary>
/// <remarks>Every dependency is a <c>Func</c> for the boot-order reason <c>CE-169</c> recorded: CGF builds its registry
/// and its AI debug surface during <c>Initialize</c>, after the debug API is constructed.</remarks>
internal static class ClusterDebugApiComposition
{
    /// <summary>
    /// The perspective-scoped dispatcher over every subsystem that contributes a debug surface (Q54-2), with the
    /// cluster-wide ack-gate read through the orchestrator when this node has one (<c>HN-028</c>).
    /// </summary>
    /// <remarks>⚠ Build AFTER the subsystems' <c>Initialize</c>: a provider carries its subsystem's world and time
    /// adapter. ⚠ The ack-gate is read through a lambda — the master is built in <c>Initialize</c> and disposed in
    /// <c>Shutdown</c>, so a latched value would answer for a controller that no longer exists.</remarks>
    public static Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher Dispatcher(
        IEnumerable<object> subsystems, Func<string> currentPerspective)
    {
        var list = subsystems.ToList();
        var providers = list
            .OfType<Hrot.Presentation.DebugApi.IProvidesDebugSurface>()
            .Select(p => p.CreateDebugProvider())
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var orchestrator = list.OfType<Hrot.Orchestrator.OrchestratorSubsystem>().FirstOrDefault();
        return new Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher(
            providers,
            currentPerspective: currentPerspective,
            acksPending: orchestrator is null ? null : () => orchestrator.IsAwaitingStepAcks);
    }

    /// <summary>
    /// ⭐ <c>CE-169</c> — the node's behaviour registry. A node with no CGF genuinely has none, and the routes say so
    /// rather than fabricating an empty one.
    /// </summary>
    public static Func<Fdp.Toolkit.Behavior.BehaviorRegistry?> BehaviorRegistry(IEnumerable<object> subsystems)
        => () => subsystems.OfType<Hrot.CGF.CgfSubsystem>()
                           .Select(s => s.BehaviorRegistry)
                           .FirstOrDefault(r => r is not null);

    /// <summary>
    /// ⭐ <c>CE-476</c> — the AI debug surface of the subsystem that OWNS <c>world</c> (the active perspective's), so a
    /// SimHost or IG perspective gets <c>null</c> and the routes say "not available" rather than borrowing CGF's.
    /// </summary>
    public static Func<Fdp.Core.EntityRepository, Hrot.Editor.AiComposition.AiDebugSurface?> AiDebugSurface(
        IEnumerable<object> subsystems)
        => world => subsystems.OfType<Hrot.CGF.CgfSubsystem>()
                              .Select(s => s.AiDebugSurface)
                              .FirstOrDefault(a => a is not null && ReferenceEquals(a.World, world));
}
