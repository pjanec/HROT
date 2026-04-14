using System;
using System.Collections.Generic;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception
{
    /// <summary>
    /// Async perception module — runs at 10 Hz on a background thread using the
    /// Snapshot-on-Demand (SoD) execution policy.
    /// <para>
    /// This module orchestrates the three async perception subsystems (executed in order):
    /// <list type="bullet">
    ///   <item><see cref="LocalGridBuilderSystem"/> — rebuilds the module-private
    ///     <see cref="SpatialHashGrid"/> from the snapshot each tick.</item>
    ///   <item><see cref="VisionBroadphaseSystem"/> — faction + FOV filtering via the local grid
    ///     → emits <see cref="LosCheckRequestEvent"/>.</item>
    ///   <item><see cref="ThreatEvaluationSystem"/> — threat score decay + visible-target boost.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>SoD contract (non-negotiable):</b>
    /// <list type="bullet">
    ///   <item>All component reads use <c>view.GetComponentRO&lt;T&gt;</c> (snapshot is read-only).</item>
    ///   <item>All component writes use <c>view.GetCommandBuffer().SetComponent&lt;T&gt;</c>.</item>
    ///   <item>Events are published via <c>view.GetCommandBuffer().PublishEvent&lt;T&gt;</c>.</item>
    ///   <item>Zero direct writes to the live <see cref="EntityRepository"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Memory ownership:</b> <see cref="_localGrid"/> allocates native memory at construction
    /// time and must be freed by calling <see cref="Dispose"/>. The grid's native arrays are
    /// shared by value-copy with <see cref="_localGridBuilder"/> and <see cref="_visionBroadphase"/>;
    /// the actual native memory is only freed once, here in <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public class PerceptionModule : IEcsModule, IDisposable
    {
        /// <inheritdoc/>
        public string Name => "Perception";

        /// <summary>
        /// Runs asynchronously at 10 Hz on a background thread.
        /// The kernel provides a read-only snapshot of the simulation state.
        /// </summary>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        // Module-private spatial grid.
        // Allocated once in the constructor using the named constants in PerceptionConstants.
        // The native arrays are shared (by pointer) with _localGridBuilder and _visionBroadphase.
        private SpatialHashGrid _localGrid;

        // Subsystems executed in order each Tick().
        private readonly LocalGridBuilderSystem _localGridBuilder;
        private readonly VisionBroadphaseSystem _visionBroadphase;
        private readonly ThreatEvaluationSystem _threatEvaluation;

        public PerceptionModule()
        {
            _localGrid = SpatialHashGrid.Create(
                PerceptionConstants.LocalGridWidth,
                PerceptionConstants.LocalGridHeight,
                PerceptionConstants.LocalGridCellSize,
                PerceptionConstants.LocalGridMaxEntities,
                Allocator.Persistent);

            // Both subsystems receive a value-copy of the struct; they share native-memory
            // pointers with _localGrid and with each other.
            _localGridBuilder = new LocalGridBuilderSystem(_localGrid);
            _visionBroadphase = new VisionBroadphaseSystem(_localGrid);
            _threatEvaluation = new ThreatEvaluationSystem();
        }

        /// <summary>
        /// Components read by this module — used by the snapshot filter to reduce
        /// snapshot size by excluding irrelevant component data.
        /// </summary>
        public IEnumerable<System.Type>? GetRequiredComponents() => new System.Type[]
        {
            typeof(SimTransform),
            typeof(Faction),
            typeof(PerceptionReceptor),
            typeof(TargetMemory),
        };

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // This module uses the direct-execution pattern (Tick) rather than
            // system-based registration.  Leave empty.
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
        {
            // Order is critical:
            //   1. LocalGridBuilder rebuilds the native-array-backed grid from the snapshot.
            //   2. VisionBroadphase queries the now-populated grid (no brute-force scan).
            //   3. ThreatEvaluation decays scores and processes TargetVisibleEvents.
            _localGridBuilder.Execute(view, deltaTime);
            _visionBroadphase.Execute(view, deltaTime);
            _threatEvaluation.Execute(view, deltaTime);
        }

        /// <summary>
        /// Releases native memory allocated for <see cref="_localGrid"/>.
        /// Must be called when the module is torn down to avoid memory leaks.
        /// </summary>
        public void Dispose()
        {
            _localGrid.Dispose();
        }
    }
}

