using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Unmanaged ECS event published by Brain-tier BTree nodes to request a pathfinding solve.
    /// Consumed by <c>PathfindingSolverSystem</c> running at 10 Hz on a background thread.
    /// The <see cref="FdpEventBus"/> EventAccumulator buffers these events across frames so
    /// no requests are lost between slow solver ticks.
    /// </summary>
    [EventId(2032)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathfindingRequestEvent
    {
        /// <summary>Stable request identifier echoed in the matching <see cref="PathfindingResultEvent"/>.</summary>
        public long RequestId;
        /// <summary>Start position in FDP Cartesian metres.</summary>
        public Vector3 Start;
        /// <summary>Goal position in FDP Cartesian metres.</summary>
        public Vector3 End;
        /// <summary>Mobility type: 0 = Wheeled, 1 = Tracked, 2 = Infantry.</summary>
        public byte MobilityProfile;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad0;
        public byte _pad1;
        public byte _pad2;
        /// <summary>Originating Brain node ID for routing responses back.</summary>
        public int SourceNodeId;
    }

    /// <summary>
    /// Unmanaged ECS event published by <c>PathfindingSolverSystem</c> (via <see cref="IEntityCommandBuffer"/>)
    /// once a pathfinding request has been resolved.
    /// Consumed by <c>PathfindingResultMaterializationSystem</c> running synchronously on the main thread
    /// and by <c>PathResponseSolverEgressTranslator</c> for DDS forwarding in distributed deployments.
    /// </summary>
    [EventId(2033)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathfindingResultEvent
    {
        /// <summary>Echoed request identifier for correlation.</summary>
        public long RequestId;
        /// <summary><c>true</c> if a valid path was found.</summary>
        public bool IsReachable;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad0;
        public byte _pad1;
        public byte _pad2;
        /// <summary>Total arc-length in metres, or 0 if unreachable.</summary>
        public float TotalDistanceMeters;
        /// <summary>Handle into the shared route/waypoint store. -1 if unreachable.</summary>
        public int RouteHandle;
        /// <summary>Originating Brain node ID, propagated from the request.</summary>
        public int SourceNodeId;
    }
}
