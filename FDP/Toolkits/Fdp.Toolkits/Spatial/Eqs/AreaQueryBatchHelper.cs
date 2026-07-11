using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Static helpers for submitting and polling area query requests.
    /// Requests are published as <see cref="AreaQueryRequestEvent"/> events on the
    /// <see cref="FdpEventBus"/>; the solver consumes them asynchronously and publishes
    /// <see cref="AreaQueryResultEvent"/> back via <see cref="IEntityCommandBuffer"/>.
    /// Results are materialized into the <see cref="AreaQueryBatchData"/> ring buffer by
    /// <c>AreaQueryResultMaterializationSystem</c> running on the main thread.
    /// </summary>
    public static class AreaQueryBatchHelper
    {
        /// <summary>
        /// Allocates the next free slot in <see cref="AreaQueryBatchData"/>, primes it, and
        /// publishes an <see cref="AreaQueryRequestEvent"/> so the solver can resolve it.
        /// The returned <c>RequestId</c> is the slot index (0–63); <c>ComputeSlot(slotIndex) == slotIndex</c>
        /// so all downstream slot calculations remain consistent.
        /// </summary>
        /// <param name="repo">The live ECS world.</param>
        /// <param name="requestingEntity">The entity submitting the query.</param>
        /// <param name="targetAreaEntity">The area polygon entity whose <c>EditablePolyline</c> bounds the query.</param>
        /// <param name="targetForce">Force affiliation filter for the query.</param>
        /// <param name="sourceNodeId">Originating Brain node ID for distributed routing.</param>
        /// <returns>
        /// The non-negative <c>RequestId</c> (slot index) that the BTree must hold to poll the
        /// result; <c>-1</c> if the batch is full (all 64 slots are in-flight).
        /// Call <see cref="FreeAreaQuerySlot"/> when the caller no longer needs the result.
        /// </returns>
        public static long RequestAreaQuery(
            EntityRepository repo,
            Entity requestingEntity,
            Entity targetAreaEntity,
            ForceId targetForce,
            int sourceNodeId = 0)
        {
            if (!repo.HasSingleton<AreaQueryBatchData>())
                return -1;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();

            // Find the first free slot via the occupancy bitmask.
            // A slot is free when its corresponding bit in OccupiedSlots is 0.
            ulong occupied = batch.OccupiedSlots;
            if (occupied == ulong.MaxValue)
                return -1;   // all 64 slots in use

            // Isolate the lowest clear bit position.
            int slot = System.Numerics.BitOperations.TrailingZeroCount(~occupied);

            // Mark the slot as occupied.
            batch.OccupiedSlots = occupied | (1UL << slot);

            // The requestId IS the slot index. ComputeSlot(slot) == slot for 0..63
            // because slot fits in the low 6 bits and the upper 32 bits are zero,
            // so the XOR-fold is a no-op: (slot ^ (slot >> 32)) % 64 == slot % 64 == slot.
            long requestId = slot;

            // Prime the ring-buffer slot to prevent reading stale data from an earlier query.
            batch.Results[slot] = new AreaQueryResult
            {
                RequestId         = requestId,
                IsReady           = false,
                TargetCount       = 0,
                TargetGroupHandle = -1,
                SourceNodeId      = sourceNodeId,
            };

            // Fire-and-forget: the solver will pick this up on its next background tick.
            repo.Bus.Publish(new AreaQueryRequestEvent
            {
                RequestId        = requestId,
                TargetAreaEntity = targetAreaEntity,
                TargetForce      = targetForce,
                SourceNodeId     = sourceNodeId,
            });

            return requestId;
        }

        /// <summary>
        /// Releases the ring-buffer slot allocated by <see cref="RequestAreaQuery"/> so it
        /// can be reused by a subsequent request.
        /// <para>
        /// Call this whenever the BTree sets <c>CachedEqsRequestId = -1</c> (i.e., after
        /// consuming the result, on timeout, on area-clear, or on abort).
        /// Passing <c>requestId &lt; 0</c> or out-of-range is a no-op.
        /// </para>
        /// </summary>
        public static void FreeAreaQuerySlot(EntityRepository repo, long requestId)
        {
            if (requestId < 0 || requestId >= AreaQueryBatchData.DefaultCapacity)
                return;
            if (!repo.HasSingleton<AreaQueryBatchData>())
                return;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
            batch.OccupiedSlots &= ~(1UL << (int)requestId);
        }

        /// <summary>
        /// Polls the <see cref="AreaQueryBatchData"/> ring buffer for a result matching
        /// <paramref name="requestId"/>.
        /// </summary>
        /// <returns>
        /// The matching <see cref="AreaQueryResult"/> if <c>IsReady == true</c> and the
        /// <c>RequestId</c> matches; <c>default</c> otherwise.
        /// </returns>
        public static AreaQueryResult GetAreaQueryResult(EntityRepository repo, long requestId)
        {
            if (!repo.HasSingleton<AreaQueryBatchData>())
                return default;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
            int slot = ComputeSlot(requestId);
            ref var result = ref batch.Results[slot];
            if (result.RequestId == requestId && result.IsReady)
                return result;
            return default;
        }

        private static int ComputeSlot(long requestId)
            => (int)(((ulong)requestId ^ ((ulong)requestId >> 32)) % (uint)AreaQueryBatchData.DefaultCapacity);

        /// <summary>
        /// Retrieves the packed entity handle at position <paramref name="index"/> within
        /// the pool chunk identified by <paramref name="targetGroupHandle"/>.
        /// </summary>
        /// <returns>
        /// The packed <c>long</c> entity value, or <c>0</c> if the handle or index is out of range.
        /// </returns>
        public static long GetTargetFromPool(EntityRepository repo, int targetGroupHandle, int index)
        {
            if (!repo.HasSingleton<EqsTargetPool>())
                return 0L;

            ref var pool = ref repo.GetSingleton<EqsTargetPool>();

            int poolIndex = targetGroupHandle + index;
            if (poolIndex < 0 || poolIndex >= pool.Targets.Length)
                return 0L;

            return pool.Targets[poolIndex];
        }
    }
}
