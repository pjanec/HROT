using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Unmanaged ECS event published by Brain-tier BTree nodes to request an area query.
    /// Consumed by <c>AreaQuerySolverSystem</c> running at 10 Hz on a background thread.
    /// The <see cref="FdpEventBus"/> EventAccumulator buffers these events across frames so
    /// no requests are lost between slow solver ticks.
    /// </summary>
    [EventId(2020)]
    [StructLayout(LayoutKind.Sequential)]
    public struct AreaQueryRequestEvent
    {
        /// <summary>Stable request identifier echoed in the matching <see cref="AreaQueryResultEvent"/>.</summary>
        public long RequestId;
        /// <summary>ECS entity referencing the area boundary (must have <c>EditablePolyline</c>).</summary>
        public Entity TargetAreaEntity;
        /// <summary>Only entities with this force affiliation are counted.</summary>
        public ForceId TargetForce;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad0;
        public byte _pad1;
        public byte _pad2;
        /// <summary>Originating Brain node ID for routing responses back.</summary>
        public int SourceNodeId;
    }

    /// <summary>
    /// Unmanaged ECS event published by <c>AreaQuerySolverSystem</c> (via <see cref="IEntityCommandBuffer"/>)
    /// once an area query has been resolved.
    /// Consumed by <c>AreaQueryResultMaterializationSystem</c> running synchronously on the main thread
    /// and by <c>AreaQueryMuscleEgressTranslator</c> for DDS forwarding in distributed deployments.
    /// </summary>
    [EventId(2021)]
    [StructLayout(LayoutKind.Sequential)]
    public struct AreaQueryResultEvent
    {
        /// <summary>Echoed request identifier for correlation.</summary>
        public long RequestId;
        /// <summary>Number of targets found inside the polygon.</summary>
        public int TargetCount;
        /// <summary>Starting index into <c>EqsTargetPool.Targets</c>; -1 if no targets found.</summary>
        public int TargetGroupHandle;
        /// <summary>Originating Brain node ID, propagated from the request.</summary>
        public int SourceNodeId;
        /// <summary>
        /// Pool cursor value after this result's targets were written into <c>EqsTargetPool.Targets</c>.
        /// The materialization system uses this to advance <see cref="EqsTargetPool.NextFreeIndex"/>;
        /// last event written in a single solver tick wins.
        /// </summary>
        public int NewPoolNextFreeIndex;
    }
}
