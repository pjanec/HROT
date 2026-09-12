using System;
using CarKinem.Spatial;
using Fdp.Core.Collections;

namespace Fdp.Toolkit.Perception.Modules
{
    /// <summary>
    /// Owns the perception <see cref="SpatialHashGrid"/> — one per world, allocated once, freed once.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this class exists (<c>B3</c>).</b> The grid used to be allocated in
    /// <c>CognitiveSpatialModule</c>'s constructor, which made that class both a <i>capability</i> (five
    /// perception systems) and a <i>resource owner</i> (persistent native memory). That fusion is the
    /// blocker for role-based composition: a node's capability set is the union of its roles, so selecting
    /// a fused module through two roles would allocate the grid twice — a native-memory leak, not a wasted
    /// tick. Splitting the resource out lets the base allocate the union of declared needs exactly once,
    /// independently of which capability asked for it.</para>
    ///
    /// <para><b>Shaped after <c>PhysicsToolkitModule</c></b>, the only resource owner in the codebase that
    /// was already clean, and therefore the one that made the concept visible: allocate, retain the handle,
    /// free on <see cref="Dispose"/>.</para>
    ///
    /// <para><b>It publishes no singleton, and that is deliberate.</b> <c>PhysicsToolkitModule</c> calls
    /// <c>SetSingleton</c> because its consumers read <c>RaycastBatchData</c> off the world. Every consumer
    /// of this grid — <c>LocalGridBuilderSystem</c>, <c>AreaQuerySolverSystem</c>,
    /// <c>VisionBroadphaseSystem</c> — takes it as a <b>constructor parameter</b> instead. Publishing it as
    /// well would create a second way to reach the same memory, which is the duplication this work removes.
    /// (Note <c>SpatialGridData</c> is a different singleton, owned by <c>SpatialHashSystem</c>.)</para>
    /// </remarks>
    public sealed class PerceptionGridProvider : IDisposable
    {
        private bool _disposed;

        /// <summary>Allocates the grid with <see cref="Allocator.Persistent"/>.</summary>
        public PerceptionGridProvider()
        {
            Grid = SpatialHashGrid.Create(
                PerceptionConstants.LocalGridWidth,
                PerceptionConstants.LocalGridHeight,
                PerceptionConstants.LocalGridCellSize,
                PerceptionConstants.LocalGridMaxEntities,
                Allocator.Persistent);
        }

        /// <summary>The grid. Consumers receive this by constructor; they never allocate their own.</summary>
        public SpatialHashGrid Grid { get; }

        /// <summary>Frees the grid. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Grid.Dispose();
        }
    }
}
