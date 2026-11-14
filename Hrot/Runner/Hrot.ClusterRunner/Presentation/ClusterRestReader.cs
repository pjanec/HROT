using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Presentation.DebugApi;

namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6) — <b>the ONE read of the cluster's rest</b> the start gate is fed: one cluster, one fact, taken from
/// any node in this process (the same argument as <c>PerspectiveScopedDispatcher.ClusterStateAnyNode</c>).
/// </summary>
internal static class ClusterRestReader
{
    /// <summary>The providers every subsystem hands back, plus the subsystems that answer for themselves (<see cref="IClusterRestSource"/> — the Editor).</summary>
    internal static ClusterRest From(IEnumerable<object> subsystems)
    {
        var list = subsystems.ToList();
        return From(Providers(list), list.OfType<IClusterRestSource>().Select(s => s.ReadClusterRest()));
    }

    /// <summary>⭐ Every subsystem's debug provider, built ONCE (<c>CreateDebugProvider</c> is a factory — <c>ClusterDebugApiComposition</c> calls
    /// it once too); its delegates read the node lazily, so a provider built before the node boots still answers later.</summary>
    internal static IReadOnlyList<ISubsystemDebugProvider> Providers(IEnumerable<object> subsystems)
        => subsystems.OfType<IProvidesDebugSurface>().Select(p => p.CreateDebugProvider()).Where(p => p != null).Select(p => p!).ToList();

    /// <summary>
    /// The pure fold. ⭐ State: the first NON-Idle state any node reports (a node that has not yet heard an Idle→Loading broadcast must not hold
    /// Start up, and one that lags into Idle must not let it in), else Idle if anyone says Idle, else <see langword="null"/> (no master seen).
    /// ⭐ MasterGone: any node says so. ⭐ CanOperate: some provider can drive the master. ⚠ A Player host is not identifiable here (R-4096 is a
    /// per-entity <c>OrdersOffered</c> singleton, not a host role), so only <c>Master</c> decides.
    /// </summary>
    internal static ClusterRest From(IEnumerable<ISubsystemDebugProvider> providers, IEnumerable<ClusterRest> sources)
    {
        ClusterState? state = null;
        bool gone = false, can = false;
        void Fold(ClusterState? s)
        {
            if (s is null) return;
            if (state is null || (state == ClusterState.Idle && s != ClusterState.Idle)) state = s;
        }
        foreach (var p in providers)
        {
            Fold(p.FollowedClusterState);
            gone |= p.MasterGone;
            can |= p.Master != null;
        }
        foreach (var r in sources)
        {
            Fold(r.State);
            gone |= r.MasterGone;
            can |= r.CanOperate;
        }
        return new ClusterRest(state, gone, can);
    }
}
