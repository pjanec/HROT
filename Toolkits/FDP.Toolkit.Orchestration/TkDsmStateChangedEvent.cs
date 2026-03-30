using Fdp.Kernel;

namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Published to the local <see cref="FdpEventBus"/> whenever the cluster DSM
    /// state transitions at the toolkit level.
    ///
    /// <para>
    /// Uses generic integer state IDs rather than Bagira's <c>DSMState</c> enum
    /// so it can be consumed by FDP toolkit code without a dependency on the
    /// Bagira DDS layer.  The Bagira wiring layer forwards this event to the
    /// Bagira-specific <c>DsmStateChangedEvent { DSMState Previous, Next }</c>.
    /// </para>
    ///
    /// <para>EventId 7002 — adjacent to Bagira's DsmStateChangedEvent (7001).</para>
    /// </summary>
    [EventId(7002)]
    public struct TkDsmStateChangedEvent
    {
        /// <summary>The integer state ID the cluster was in before the transition.</summary>
        public int PreviousStateId;

        /// <summary>The integer state ID the cluster has transitioned to.</summary>
        public int NextStateId;
    }
}
