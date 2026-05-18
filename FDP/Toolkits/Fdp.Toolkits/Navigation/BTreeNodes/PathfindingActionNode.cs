using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.BTreeNodes
{
    /// <summary>
    /// Static helpers for submitting and retrieving batched pathfinding requests via the
    /// ECS event bus. Requests are published as <see cref="PathfindingRequestEvent"/>
    /// events; results are written into the <see cref="PathfindingBatchData"/> ring buffer
    /// by <c>PathfindingResultMaterializationSystem</c> and polled here.
    /// Coordinates must be supplied in FDP Cartesian metres.
    /// </summary>
    public static class PathfindingBatchHelper
    {
        /// <summary>
        /// Publishes a <see cref="PathfindingRequestEvent"/> and primes the ring-buffer slot
        /// so the BTree does not read stale results from an earlier request.
        /// </summary>
        /// <param name="mobilityProfile">0 = Wheeled, 1 = Tracked, 2 = Infantry (default 0).</param>
        /// <returns>
        /// The <c>RequestId</c> used to poll the result via <see cref="GetPathResult"/>.
        /// </returns>
        public static long RequestPath(
            EntityRepository world,
            int entityIndex,
            Vector3 from,
            Vector3 to,
            byte mobilityProfile = 0,
            int sourceNodeId = 0)
        {
            // Generate ID using entity index + current world version to avoid collision
            // with prior requests from the same entity.
            long requestId = ((long)entityIndex << 32) | world.GlobalVersion;

            // Prime the ring-buffer slot to prevent stale-result reads.
            if (world.HasSingleton<PathfindingBatchData>())
            {
                ref var batch = ref world.GetSingleton<PathfindingBatchData>();
                int slot = (int)((uint)requestId % (uint)PathfindingBatchData.DefaultCapacity);
                batch.Results[slot] = new PathResult
                {
                    RequestId           = requestId,
                    IsReachable         = false,
                    TotalDistanceMeters = 0f,
                    RouteHandle         = -1,
                    SourceNodeId        = sourceNodeId,
                };
            }

            world.Bus.Publish(new PathfindingRequestEvent
            {
                RequestId       = requestId,
                Start           = from,
                End             = to,
                MobilityProfile = mobilityProfile,
                SourceNodeId    = sourceNodeId,
            });

            return requestId;
        }

        /// <summary>
        /// Polls the <see cref="PathfindingBatchData"/> ring buffer for a result matching
        /// <paramref name="requestId"/>.
        /// </summary>
        /// <returns>
        /// The matching <see cref="PathResult"/> if available; <c>default</c> when still pending.
        /// </returns>
        public static PathResult GetPathResult(EntityRepository world, long requestId)
        {
            if (!world.HasSingleton<PathfindingBatchData>())
                return default;

            ref readonly var batch = ref world.GetSingleton<PathfindingBatchData>();
            int slot = (int)((uint)requestId % (uint)PathfindingBatchData.DefaultCapacity);
            ref readonly var result = ref batch.Results[slot];
            if (result.RequestId == requestId && result.IsReachable)
                return result;
            return default;
        }
    }
}
