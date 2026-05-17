using Hrot.NED.Descriptors.Orchestration;
using Fdp.Core;

namespace Hrot.Common.Orchestration
{
    /// <summary>
    /// Published to the local <see cref="FdpEventBus"/> whenever the cluster Cluster state
    /// transitions.  Domain systems (physics, recording, AI, etc.) subscribe to this event
    /// to react to state changes without coupling to DDS or the orchestration layer.
    ///
    /// <para>
    /// The event is published by <c>ClusterSlave</c> immediately after a
    /// <see cref="NodeOpType.CommitState"/> command is processed, and optionally also by
    /// <see cref="IClusterOpHandler"/> implementations (e.g. <c>LiveLoadClusterStateHandler</c>) if not
    /// already published by the slave for that transaction.
    /// </para>
    ///
    /// <para>
    /// <b>Layering note:</b> <c>ClusterState</c> is defined in <c>Hrot.NED</c>
    /// (Hrot application layer).  This event therefore lives in <c>Hrot.Common</c>
    /// rather than in any <c>FDP/</c> project.
    /// </para>
    ///
    /// <para>
    /// <b>Migration note (Phase 4):</b> The preferred toolkit-level equivalent is
    /// <c>FDP.Toolkit.Orchestration.TkClusterStateChangedEvent</c> — which uses generic
    /// integer state IDs and has no dependency on <c>ClusterState</c>.  The Hrot wiring
    /// layer forwards <c>TkClusterStateChangedEvent</c> to this event for backward
    /// compatibility with existing Hrot subscribers.
    /// </para>
    /// </summary>
    [EventId(7001)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct ClusterStateChangedEvent
    {
        /// <summary>The Cluster state the cluster was in before the transition.</summary>
        public ClusterState Previous;

        /// <summary>The Cluster state the cluster has transitioned to.</summary>
        public ClusterState Next;
    }
}
