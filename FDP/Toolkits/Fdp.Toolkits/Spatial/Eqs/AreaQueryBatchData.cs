using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Perception;

namespace Fdp.Toolkit.Spatial.Eqs
{
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
    /// ECS singleton that holds the ring buffer of area query results.
    /// Requests are submitted as <see cref="AreaQueryRequestEvent"/> events via
    /// <see cref="FdpEventBus"/>; results are written here by
    /// <c>AreaQueryResultMaterializationSystem</c> after the solver resolves them.
    /// Indexed via the slot index returned by <c>AreaQueryBatchHelper.RequestAreaQuery</c>.
    /// </summary>
    [ComponentId(GlobalComponentIds.AreaQueryBatchData)]
    public struct AreaQueryBatchData
    {
        /// <summary>Ring-buffer capacity for concurrent area query results.</summary>
        public const int DefaultCapacity = 64;

        /// <summary>Results written by <c>AreaQueryResultMaterializationSystem</c>.
        /// Indexed by the slot (requestId) allocated in <c>AreaQueryBatchHelper.RequestAreaQuery</c>.</summary>
        public NativeArray<AreaQueryResult> Results;

        /// <summary>
        /// Bitmask of occupied slots (bit <c>i</c> set ⇒ slot <c>i</c> is in-flight).
        /// Managed exclusively by <c>AreaQueryBatchHelper</c>:
        /// set on <c>RequestAreaQuery</c>, cleared on <c>FreeAreaQuerySlot</c>.
        /// </summary>
        public ulong OccupiedSlots;
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
