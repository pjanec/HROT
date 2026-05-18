using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics.BTreeNodes
{
    /// <summary>
    /// Static helpers for submitting and retrieving batched raycast queries via the ECS event bus.
    /// Requests are published as <see cref="RaycastRequestEvent"/> events;
    /// results are written into the <see cref="RaycastBatchData"/> ring buffer by
    /// <c>RaycastResultMaterializationSystem</c> and polled here.
    /// </summary>
    public static class RaycastBatchHelper
    {
        /// <summary>
        /// Publishes a <see cref="RaycastRequestEvent"/> and primes the ring-buffer slot
        /// so the BTree does not read stale results from an earlier cast.
        /// </summary>
        /// <returns>
        /// The <c>RayId</c> used to poll the result via <see cref="GetRaycastResult"/>.
        /// </returns>
        public static long RequestRaycast(
            EntityRepository world,
            int entityIndex,
            ushort entityGeneration,
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            int sourceNodeId = 0)
        {
            // Generate ID using entity index + current world version to avoid collision.
            long rayId = ((long)entityIndex << 32) | world.GlobalVersion;
            Vector3 end = origin + direction * maxDistance;

            // Prime the ring-buffer slot to prevent stale-result reads.
            if (world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref world.GetSingleton<RaycastBatchData>();
                int slot = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);
                batch.Hits[slot] = new RaycastHit
                {
                    RayId  = rayId,
                    HasHit = 0,
                };
            }

            world.Bus.Publish(new RaycastRequestEvent
            {
                Start        = origin,
                End          = end,
                RayId        = rayId,
                IgnoreEntity = new Entity(entityIndex, entityGeneration),
                LayerMask    = ~0,
                SourceNodeId = sourceNodeId,
            });

            return rayId;
        }

        /// <summary>
        /// Polls the <see cref="RaycastBatchData"/> ring buffer for a result matching
        /// <paramref name="rayId"/>.
        /// </summary>
        /// <returns>
        /// The matching <see cref="RaycastHit"/>, or <c>default</c> when still pending.
        /// </returns>
        public static RaycastHit GetRaycastResult(EntityRepository world, long rayId)
        {
            if (!world.HasSingleton<RaycastBatchData>())
                return default;

            ref readonly var batch = ref world.GetSingleton<RaycastBatchData>();
            int slot = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            ref readonly var hit = ref batch.Hits[slot];
            if (hit.RayId == rayId)
                return hit;
            return default;
        }
    }
}
