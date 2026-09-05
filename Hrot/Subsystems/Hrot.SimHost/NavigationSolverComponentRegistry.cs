using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.SimHost;

/// <summary>
/// Shared registration for navigation-solver and EQS runtime schema.
/// </summary>
/// <remarks>
/// <para><b>This class is the de-facto owner of four persistent native arrays</b> —
/// <c>PathfindingBatchData.Results</c>, <c>AreaQueryBatchData.Results</c>,
/// <c>EqsTargetPool.Targets</c> and <c>EqsResultPool.Results</c>. That makes it a <i>resource
/// owner</i> in the sense of the composition design (<c>B3</c>), even though it is a static
/// function rather than a module, which is why it needs both halves of an ownership contract:
/// allocate at most once (<see cref="RegisterAll"/>) and free (<see cref="DisposeAll"/>).</para>
///
/// <para><b>Why the allocations are guarded.</b> <c>SetSingleton</c> is "set or update": it
/// overwrites the stored struct unconditionally (<c>EntityRepository.SetSingletonUnmanaged</c>).
/// A second unguarded call therefore replaced each singleton with a fresh
/// <c>Allocator.Persistent</c> array and <b>orphaned the first one</b> — a native-memory leak with
/// no error and no test. Two production hosts do exactly that: <c>EditorSubsystem</c> and
/// <c>EditorStrideSubsystem</c> both call <c>SimHostComponentRegistry.RegisterAll</c> and
/// <c>CgfComponentRegistry.RegisterAll</c> on the <b>same</b> world, and both delegate here.
/// This is the memory-owning form of the double-registration hazard <c>[SingleInstance]</c>
/// catches on the system axis, reached through the registry path instead of the module path.</para>
///
/// <para><b>Idempotence is the contract, not an optimisation.</b> A node's capability set is the
/// union of its roles, so any number of roles may ask for the navigation/EQS schema; the second
/// and later asks must be no-ops on the memory-owning slots. Component and event registration
/// below is already idempotent by construction.</para>
/// </remarks>
public static class NavigationSolverComponentRegistry
{
    /// <summary>
    /// Registers pathfinding/EQS singletons and request/result event streams.
    /// Safe to call more than once per world: the four persistent pools are allocated at most once.
    /// </summary>
    public static void RegisterAll(EntityRepository world)
    {
        if (!world.HasSingleton<PathfindingBatchData>())
        {
            world.SetSingleton(new PathfindingBatchData
            {
                Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            });
        }

        world.RegisterEvent<PathfindingRequestEvent>();
        world.RegisterEvent<PathfindingResultEvent>();

        if (!world.HasSingleton<AreaQueryBatchData>())
        {
            world.SetSingleton(new AreaQueryBatchData
            {
                Results = new NativeArray<AreaQueryResult>(AreaQueryBatchData.DefaultCapacity, Allocator.Persistent),
            });
        }

        if (!world.HasSingleton<EqsTargetPool>())
        {
            world.SetSingleton(new EqsTargetPool
            {
                Targets = new NativeArray<long>(EqsTargetPool.PoolCapacity, Allocator.Persistent),
            });
        }

        world.RegisterEvent<AreaQueryRequestEvent>();
        world.RegisterEvent<AreaQueryResultEvent>();

        // EQS v1.3 result pool: pre-allocated ring buffer for ranked candidate data.
        // ⚠ EqsSolverSystem.Execute carries the same guarded lazy-init for this slot, so a world
        // that never reached this registry still gets a pool. That is a fallback, not a second
        // owner: DisposeAll frees whichever of the two allocated it.
        if (!world.HasSingleton<EqsResultPool>())
        {
            world.SetSingleton(new EqsResultPool
            {
                Results = new NativeArray<EqsResult>(EqsResultPool.PoolCapacity, Allocator.Persistent),
            });
        }

        // EqsResultEvent: unmanaged event published by EqsSolverSystem (offline path).
        world.RegisterEvent<EqsResultEvent>();
    }

    /// <summary>
    /// Frees the four persistent native arrays <see cref="RegisterAll"/> owns. Safe to call on a
    /// world that never reached <see cref="RegisterAll"/>, and safe to call more than once.
    /// </summary>
    /// <remarks>
    /// The singletons are taken by <c>ref</c> so the stored copy's <c>IsCreated</c> flag is cleared
    /// — a <c>NativeArray</c> read by value would leave the world holding a stale "created" handle
    /// and a later call would double-free.
    /// </remarks>
    public static void DisposeAll(EntityRepository world)
    {
        if (world is null) return;

        if (world.HasSingleton<PathfindingBatchData>())
        {
            ref var pathfinding = ref world.GetSingletonUnmanaged<PathfindingBatchData>();
            if (pathfinding.Results.IsCreated) pathfinding.Results.Dispose();
        }

        if (world.HasSingleton<AreaQueryBatchData>())
        {
            ref var areaQuery = ref world.GetSingletonUnmanaged<AreaQueryBatchData>();
            if (areaQuery.Results.IsCreated) areaQuery.Results.Dispose();
        }

        if (world.HasSingleton<EqsTargetPool>())
        {
            ref var targets = ref world.GetSingletonUnmanaged<EqsTargetPool>();
            if (targets.Targets.IsCreated) targets.Targets.Dispose();
        }

        if (world.HasSingleton<EqsResultPool>())
        {
            ref var results = ref world.GetSingletonUnmanaged<EqsResultPool>();
            if (results.Results.IsCreated) results.Results.Dispose();
        }
    }
}
