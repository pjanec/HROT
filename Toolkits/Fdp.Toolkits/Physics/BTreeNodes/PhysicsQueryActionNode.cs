using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics.BTreeNodes
{
    /// <summary>
    /// Static helpers for submitting and retrieving batched raycast queries from the ECS
    /// <see cref="RaycastBatchData"/> singleton.  Intended to be called from FastBTree
    /// <c>NodeLogicDelegate</c> implementations registered with <c>ActionRegistry</c>.
    /// </summary>
    public static class RaycastBatchHelper
    {
        /// <summary>
        /// Appends a raycast request to <see cref="RaycastBatchData"/> this frame.
        /// </summary>
        /// <returns>
        /// A non-negative ray identifier used to retrieve the result via
        /// <see cref="GetRaycastResult"/>, or <c>-1</c> if the batch is full.
        /// </returns>
        public static int RequestRaycast(
            EntityRepository world,
            int entityIndex,
            ushort entityGeneration,
            Vector3 origin,
            Vector3 direction,
            float maxDistance)
        {
            ref var batch = ref world.GetSingleton<RaycastBatchData>();
            if (batch.Count >= batch.Requests.Length) return -1;
            int idx   = batch.Count++;
            long rayId = ((long)entityIndex << 20) | (uint)idx;
            batch.Requests[idx] = new RaycastRequest
            {
                Start        = origin,
                End          = origin + direction * maxDistance,
                RayId        = rayId,
                IgnoreEntity = new Entity(entityIndex, entityGeneration),
            };
            return (int)(rayId & int.MaxValue);
        }

        /// <summary>
        /// Looks up the raycast result matching the supplied <paramref name="rayId"/>.
        /// </summary>
        /// <returns>
        /// The matching <see cref="RaycastHit"/>, or <c>default</c> when no result is
        /// present (ray still pending or <paramref name="rayId"/> not found).
        /// </returns>
        public static RaycastHit GetRaycastResult(EntityRepository world, int rayId)
        {
            ref readonly var batch = ref world.GetSingleton<RaycastBatchData>();
            for (int i = 0; i < batch.Count; i++)
                if (batch.Hits[i].RayId == (long)rayId) return batch.Hits[i];
            return default;
        }
    }
}
