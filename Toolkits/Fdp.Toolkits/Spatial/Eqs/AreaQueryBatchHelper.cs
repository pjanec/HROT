using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Static helpers for submitting and polling area query requests against the
    /// <see cref="AreaQueryBatchData"/> singleton. Mirrors the style of
    /// <c>PathfindingBatchHelper</c> in <c>Fdp.Toolkit.Navigation</c>.
    /// </summary>
    public static class AreaQueryBatchHelper
    {
        /// <summary>
        /// Appends an area query request to the <see cref="AreaQueryBatchData"/> singleton.
        /// </summary>
        /// <param name="repo">The live ECS world.</param>
        /// <param name="requestingEntity">The entity submitting the query (used to build the request ID).</param>
        /// <param name="targetAreaEntity">The area polygon entity whose <c>EditablePolyline</c> bounds the query.</param>
        /// <param name="targetForce">Force affiliation filter for the query.</param>
        /// <param name="sourceNodeId">Originating Brain node ID for distributed routing.</param>
        /// <returns>
        /// The non-negative <c>RequestId</c> on success, or <c>-1</c> if the batch is full.
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

            int batchSlot = batch.Count;
            if (batchSlot >= AreaQueryBatchData.DefaultCapacity)
                return -1;

            // Cast entityIndex to long BEFORE shifting to avoid silent 32-bit truncation
            // for entity indices above 4095.
            long requestId = ((long)requestingEntity.Index << 32) | (uint)batchSlot;

            batch.Requests[batchSlot] = new AreaQueryRequest
            {
                RequestId        = requestId,
                TargetAreaEntity = targetAreaEntity,
                TargetForce      = targetForce,
                SourceNodeId     = sourceNodeId,
            };

            // Initialize the result slot so the Brain does not read stale data.
            batch.Results[batchSlot] = new AreaQueryResult
            {
                RequestId   = requestId,
                IsReady     = false,
                TargetCount = 0,
                TargetGroupHandle = -1,
                SourceNodeId      = sourceNodeId,
            };

            batch.Count = batchSlot + 1;
            return requestId;
        }

        /// <summary>
        /// Polls the <see cref="AreaQueryBatchData"/> for a result matching
        /// <paramref name="requestId"/>.
        /// </summary>
        /// <returns>
        /// The matching <see cref="AreaQueryResult"/> if <c>IsReady == true</c>;
        /// <c>default</c> otherwise.
        /// </returns>
        public static AreaQueryResult GetAreaQueryResult(EntityRepository repo, long requestId)
        {
            if (!repo.HasSingleton<AreaQueryBatchData>())
                return default;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();

            for (int i = 0; i < batch.Count; i++)
            {
                ref var result = ref batch.Results[i];
                if (result.RequestId == requestId && result.IsReady)
                    return result;
            }
            return default;
        }

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

        /// <summary>
        /// Resets <see cref="AreaQueryBatchData.Count"/> to zero and clears the
        /// <see cref="EqsTargetPool"/> free list. Called by <c>AreaQueryInitializationSystem</c>
        /// at the start of each Brain frame.
        /// </summary>
        public static void ResetBatch(EntityRepository repo)
        {
            if (repo.HasSingleton<AreaQueryBatchData>())
            {
                ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
                batch.Count = 0;
            }

            if (repo.HasSingleton<EqsTargetPool>())
            {
                ref var pool = ref repo.GetSingleton<EqsTargetPool>();
                pool.NextFreeIndex = 0;

                // Zero the entire pool so stale packed entity handles do not escape.
                for (int i = 0; i < pool.Targets.Length; i++)
                    pool.Targets[i] = 0L;
            }
        }
    }
}
