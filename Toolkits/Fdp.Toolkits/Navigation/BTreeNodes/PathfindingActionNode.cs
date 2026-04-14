using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.BTreeNodes
{
    /// <summary>
    /// Static helpers for submitting and retrieving batched pathfinding requests from the ECS
    /// <see cref="PathfindingBatchData"/> singleton.  Intended to be called from FastBTree
    /// <c>NodeLogicDelegate</c> implementations registered with <c>ActionRegistry</c>.
    /// Coordinates must be supplied in FDP Cartesian metres, <em>not</em> GeoPosition.
    /// </summary>
    public static class PathfindingBatchHelper
    {
        /// <summary>
        /// Appends a pathfinding request to <see cref="PathfindingBatchData"/> this frame.
        /// </summary>
        /// <param name="mobilityProfile">0 = Wheeled, 1 = Tracked, 2 = Infantry (default 0).</param>
        /// <returns>
        /// A non-negative request identifier used to retrieve the result via
        /// <see cref="GetPathResult"/>, or <c>-1</c> if the batch is full.
        /// </returns>
        public static int RequestPath(
            EntityRepository world,
            int entityIndex,
            ushort entityGeneration,
            Vector3 from,
            Vector3 to,
            byte mobilityProfile = 0)
        {
            ref var batch = ref world.GetSingleton<PathfindingBatchData>();
            if (batch.Count >= batch.Requests.Length) return -1;
            long requestId = ((long)entityIndex << 20) | (uint)batch.Count;
            batch.Requests[batch.Count++] = new PathRequest
            {
                RequestId       = requestId,
                Start           = from,
                End             = to,
                MobilityProfile = mobilityProfile,
            };
            return (int)(requestId & int.MaxValue);
        }

        /// <summary>
        /// Looks up the pathfinding result matching the supplied <paramref name="requestId"/>.
        /// </summary>
        /// <returns>
        /// The matching <see cref="PathResult"/>, or <c>default</c> when no result is
        /// present (still pending or <paramref name="requestId"/> not found).
        /// </returns>
        public static PathResult GetPathResult(EntityRepository world, int requestId)
        {
            ref readonly var batch = ref world.GetSingleton<PathfindingBatchData>();
            for (int i = 0; i < batch.Count; i++)
                if (batch.Results[i].RequestId == (long)requestId) return batch.Results[i];
            return default;
        }
    }
}
