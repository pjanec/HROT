using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Perception;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// A single area query request submitted by a Brain-tier behavior node.
    /// Cleared at the start of each Brain frame by <c>AreaQueryInitializationSystem</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AreaQueryRequest
    {
        /// <summary>Stable request identifier: ((long)entityIndex &lt;&lt; 32) | (uint)batchSlot.</summary>
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
    /// Result of a resolved area query, written by <c>AreaQuerySolverSystem</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AreaQueryResult
    {
        /// <summary>Echoed request identifier for correlation.</summary>
        public long RequestId;
        /// <summary>True once the solver has processed this slot (success or empty result).</summary>
        public bool IsReady;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad0;
        public byte _pad1;
        public byte _pad2;
        /// <summary>Number of targets found inside the polygon.</summary>
        public int TargetCount;
        /// <summary>Starting index into <c>EqsTargetPool.Targets</c>; -1 if pool exhausted.</summary>
        public int TargetGroupHandle;
        /// <summary>Originating Brain node ID.</summary>
        public int SourceNodeId;
    }

    /// <summary>
    /// ECS singleton that holds the current-frame area query batch.
    /// Allocated once at startup via <c>SimHostComponentRegistry.RegisterAll</c>.
    /// Cleared each Brain frame by <c>AreaQueryInitializationSystem</c>.
    /// </summary>
    [ComponentId(GlobalComponentIds.AreaQueryBatchData)]
    public struct AreaQueryBatchData
    {
        /// <summary>Maximum number of concurrent area queries per Brain frame.</summary>
        public const int DefaultCapacity = 64;

        /// <summary>Number of valid entries currently in <see cref="Requests"/>.</summary>
        public int Count;

        /// <summary>Pending requests submitted by Brain behavior nodes.</summary>
        public NativeArray<AreaQueryRequest> Requests;

        /// <summary>Results written by <c>AreaQuerySolverSystem</c> on the Muscle node.</summary>
        public NativeArray<AreaQueryResult> Results;
    }

    /// <summary>
    /// ECS singleton pool of packed entity handles (<c>long</c>) returned by area queries.
    /// Pool capacity = DefaultCapacity * MaxTrackedTargets * 4 to support up to 64 concurrent
    /// queries with up to 16 results each.
    /// Cleared each Brain frame by <c>AreaQueryInitializationSystem</c>.
    /// </summary>
    [ComponentId(GlobalComponentIds.EqsTargetPool)]
    public struct EqsTargetPool
    {
        /// <summary>Total number of target slots across all concurrent queries.</summary>
        public const int PoolCapacity = AreaQueryBatchData.DefaultCapacity * PerceptionConstants.MaxTrackedTargets * 4;

        /// <summary>Next free index in <see cref="Targets"/>.</summary>
        public int NextFreeIndex;

        /// <summary>Packed entity handles (<c>(long)entity.PackedValue</c>). Zero = empty slot.</summary>
        public NativeArray<long> Targets;
    }
}
