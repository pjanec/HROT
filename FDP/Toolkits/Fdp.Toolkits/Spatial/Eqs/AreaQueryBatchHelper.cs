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
        /// Publishes an <see cref="AreaQueryRequestEvent"/> and primes the corresponding ring-buffer
        /// slot so the BTree does not read stale results from an earlier query.
        /// </summary>
        /// <param name="repo">The live ECS world.</param>
        /// <param name="requestingEntity">The entity submitting the query (used to build the request ID).</param>
        /// <param name="targetAreaEntity">The area polygon entity whose <c>EditablePolyline</c> bounds the query.</param>
        /// <param name="targetForce">Force affiliation filter for the query.</param>
        /// <param name="sourceNodeId">Originating Brain node ID for distributed routing.</param>
        /// <returns>The non-negative <c>RequestId</c> that the BTree must hold to poll the result.</returns>
        public static long RequestAreaQuery(
            EntityRepository repo,
            Entity requestingEntity,
            Entity targetAreaEntity,
            ForceId targetForce,
            int sourceNodeId = 0)
        {
            // Generate a monotonically driven ID using entity index + current world version.
            // Cast to long before shifting to prevent silent 32-bit truncation.
            long requestId = ((long)requestingEntity.Index << 32) | repo.GlobalVersion;

            // Prime the ring-buffer slot to prevent reading stale data from a previous query.
            if (repo.HasSingleton<AreaQueryBatchData>())
            {
                ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
                int slot = ComputeSlot(requestId);
                batch.Results[slot] = new AreaQueryResult
                {
                    RequestId         = requestId,
                    IsReady           = false,
                    TargetCount       = 0,
                    TargetGroupHandle = -1,
                    SourceNodeId      = sourceNodeId,
                };
            }

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
