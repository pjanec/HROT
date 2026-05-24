using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost;

/// <summary>
/// Shared registration for navigation-solver and EQS runtime schema.
/// </summary>
public static class NavigationSolverComponentRegistry
{
    /// <summary>
    /// Registers pathfinding/EQS singletons and request/result event streams.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        world.SetSingleton(new PathfindingBatchData
        {
            Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
        });

        world.RegisterEvent<PathfindingRequestEvent>();
        world.RegisterEvent<PathfindingResultEvent>();

        world.SetSingleton(new AreaQueryBatchData
        {
            Results = new NativeArray<AreaQueryResult>(AreaQueryBatchData.DefaultCapacity, Allocator.Persistent),
        });
        world.SetSingleton(new EqsTargetPool
        {
            Targets = new NativeArray<long>(EqsTargetPool.PoolCapacity, Allocator.Persistent),
        });

        world.RegisterEvent<AreaQueryRequestEvent>();
        world.RegisterEvent<AreaQueryResultEvent>();

        // EQS v1.3 result pool: pre-allocated ring buffer for ranked candidate data.
        world.SetSingleton(new EqsResultPool
        {
            Results = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, Allocator.Persistent),
        });

        // EqsResultEvent: unmanaged event published by EqsSolverSystem (offline path).
        world.RegisterEvent<EqsResultEvent>();
    }
}
