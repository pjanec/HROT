using Bagira.BDC.SSTD.Orchestration;
using Fdp.Kernel;

namespace Bagira.Common.Orchestration
{
    /// <summary>
    /// Published to the local <see cref="FdpEventBus"/> whenever the cluster DSM state
    /// transitions.  Domain systems (physics, recording, AI, etc.) subscribe to this event
    /// to react to state changes without coupling to DDS or the orchestration layer.
    ///
    /// <para>
    /// The event is published by <c>DrillSlave</c> immediately after a
    /// <see cref="NodeOpType.CommitState"/> command is processed, and optionally also by
    /// <see cref="IDsmHandler"/> implementations (e.g. <c>LiveLoadDsmHandler</c>) if not
    /// already published by the slave for that transaction.
    /// </para>
    ///
    /// <para>
    /// <b>Layering note:</b> <c>DSMState</c> is defined in <c>Bagira.DDS.DataModel</c>
    /// (Bagira application layer).  This event therefore lives in <c>Bagira.Common</c>
    /// rather than in any <c>FDP/</c> project.
    /// </para>
    /// </summary>
    [EventId(7001)]
    public struct DsmStateChangedEvent
    {
        /// <summary>The DSM state the cluster was in before the transition.</summary>
        public DSMState Previous;

        /// <summary>The DSM state the cluster has transitioned to.</summary>
        public DSMState Next;
    }
}
