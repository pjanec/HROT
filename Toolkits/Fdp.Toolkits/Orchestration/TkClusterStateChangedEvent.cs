using Fdp.Kernel;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Published to the local <see cref="FdpEventBus"/> whenever the cluster 
    /// state transitions at the toolkit level.
    ///
    /// <para>
    /// Uses generic integer state IDs rather than Hrot's <c>ClusterState</c> enum
    /// so it can be consumed by FDP toolkit code without a dependency on the
    /// Hrot DDS layer.  The Hrot wiring layer forwards this event to the
    /// Hrot-specific <c>DsmStateChangedEvent { ClusterState Previous, Next }</c>.
    /// </para>
    ///
    /// <para>EventId 7002 — adjacent to Hrot's DsmStateChangedEvent (7001).</para>
    /// </summary>
    [EventId(7002)]
    public struct TkClusterStateChangedEvent
    {
        /// <summary>The integer state ID the cluster was in before the transition.</summary>
        public int PreviousStateId;

        /// <summary>The integer state ID the cluster has transitioned to.</summary>
        public int NextStateId;
    }
}
