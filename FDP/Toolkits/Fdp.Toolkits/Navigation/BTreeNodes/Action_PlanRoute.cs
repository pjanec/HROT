using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.BTreeNodes
{
    /// <summary>
    /// Lightweight configuration object for a single pathfinding query.
    /// Delegates to <see cref="PathfindingBatchHelper"/> so it can be used from
    /// <c>NodeLogicDelegate</c> implementations or directly in unit tests.
    /// All positions must be in FDP Cartesian metres.
    /// </summary>
    public sealed class Action_PlanRoute
    {
        /// <summary>ECS entity index of the querying entity.</summary>
        public int EntityIndex { get; set; }

        /// <summary>ECS entity generation of the querying entity.</summary>
        public ushort EntityGeneration { get; set; }

        /// <summary>Start position in FDP Cartesian metres.</summary>
        public Vector3 From { get; set; }

        /// <summary>Goal position in FDP Cartesian metres.</summary>
        public Vector3 To { get; set; }

        /// <summary>Mobility type: 0 = Wheeled, 1 = Tracked, 2 = Infantry.</summary>
        public byte MobilityProfile { get; set; }

        /// <summary>
        /// Appends the configured path request to <see cref="PathfindingBatchData"/>
        /// and returns the assigned request ID (long), or -1 if the batch is full.
        /// </summary>
        public long Execute(EntityRepository world)
            => PathfindingBatchHelper.RequestPath(world, EntityIndex, From, To, MobilityProfile);

        /// <summary>
        /// Retrieves the <see cref="PathResult"/> matching <paramref name="requestId"/>,
        /// or <c>default</c> if not yet resolved or not found.
        /// </summary>
        public PathResult QueryResult(EntityRepository world, long requestId)
            => PathfindingBatchHelper.GetPathResult(world, requestId);
    }
}
