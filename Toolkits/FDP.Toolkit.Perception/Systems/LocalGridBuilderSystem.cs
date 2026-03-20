using System.Numerics;
using CarKinem.Spatial;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception.Systems
{
    /// <summary>
    /// Async module system that rebuilds the <see cref="PerceptionModule"/>'s private
    /// <see cref="SpatialHashGrid"/> from the current simulation snapshot each tick.
    /// <para>
    /// This system runs <b>first</b> inside <see cref="PerceptionModule.Tick"/> so that
    /// subsequent systems (e.g. <see cref="VisionBroadphaseSystem"/>) can query the freshly
    /// rebuilt grid without touching the main-thread's <see cref="SpatialHashGrid"/> singleton.
    /// </para>
    /// <para>
    /// <b>SoD contract:</b> uses only <c>view.GetComponentRO&lt;SimTransform&gt;</c>.
    /// Zero direct world writes — all output is expressed through the shared native-memory
    /// backing of the grid struct passed in at construction time.
    /// </para>
    /// <para>
    /// <b>Struct-copy / shared-memory note:</b>
    /// <see cref="SpatialHashGrid"/> is a value type whose fields are
    /// <see cref="Fdp.Kernel.Collections.NativeArray{T}"/> wrappers around native pointers.
    /// Passing a struct copy here shares those native pointers with the caller's copy, so
    /// all <c>Clear()</c> and <c>Add()</c> mutations are visible to every other holder of a
    /// struct copy (e.g. <see cref="VisionBroadphaseSystem"/>). <see cref="EntityCount"/>
    /// is updated locally in this system's copy and is irrelevant to <c>QueryNeighbors</c>,
    /// which iterates the linked-list chains rather than a count-bounded range.
    /// </para>
    /// </summary>
    public class LocalGridBuilderSystem : IEcsModuleSystem
    {
        // Value-copy of the PerceptionModule's grid struct.
        // Shares native-memory pointers with the caller's copy and VisionBroadphaseSystem's copy.
        private SpatialHashGrid _grid;

        /// <summary>
        /// Initialises the builder with a copy of the module's private grid.
        /// Ownership of the underlying native memory remains with <see cref="PerceptionModule"/>.
        /// </summary>
        public LocalGridBuilderSystem(SpatialHashGrid grid)
        {
            _grid = grid;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Reset all cell heads to empty and zero the entity counter on this local copy.
            _grid.Clear();

            // Rebuild from snapshot — all entities that have a spatial position are indexed.
            // This includes Perception entities (SimTransform but no VehicleState) which the
            // main-thread SpatialHashSystem also indexes after DEBT-001 was resolved.
            var query = view.Query().With<SimTransform>().Build();
            foreach (var entity in query)
            {
                ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
                _grid.Add(entity, new Vector2(tf.Position.X, tf.Position.Y));
            }
        }
    }
}
