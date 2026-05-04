using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics.BTreeNodes
{
    /// <summary>
    /// Lightweight configuration object for a single raycast query.
    /// Delegates to <see cref="RaycastBatchHelper"/> so it can be used from
    /// <c>NodeLogicDelegate</c> implementations or directly in unit tests.
    /// </summary>
    public sealed class Action_QueryRaycast
    {
        /// <summary>ECS entity index of the querying entity.</summary>
        public int EntityIndex { get; set; }

        /// <summary>ECS entity generation of the querying entity.</summary>
        public ushort EntityGeneration { get; set; }

        /// <summary>World-space ray origin (metres).</summary>
        public Vector3 Origin { get; set; }

        /// <summary>Normalised world-space ray direction.</summary>
        public Vector3 Direction { get; set; }

        /// <summary>Maximum ray range (metres).</summary>
        public float MaxDistance { get; set; }

        /// <summary>
        /// Publishes a <see cref="RaycastRequestEvent"/> for the configured ray and returns the
        /// assigned ray ID, or 0 if the singleton is absent.
        /// </summary>
        public long Execute(EntityRepository world)
            => RaycastBatchHelper.RequestRaycast(world, EntityIndex, EntityGeneration, Origin, Direction, MaxDistance);

        /// <summary>
        /// Retrieves the <see cref="RaycastHit"/> matching <paramref name="rayId"/>,
        /// or <c>default</c> if not yet resolved or not found.
        /// </summary>
        public RaycastHit QueryResult(EntityRepository world, long rayId)
            => RaycastBatchHelper.GetRaycastResult(world, rayId);
    }
}
