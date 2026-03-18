using System;
using CarKinem.Spatial;
using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Perception.Systems;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception.Modules
{
    /// <summary>
    /// Wraps the four autonomous perception systems into a self-contained
    /// <see cref="IModule"/> that can be installed independently of the Brain modules.
    ///
    /// <para><b>Execution model:</b> <see cref="ExecutionPolicy.SlowBackground"/> at 10 Hz.
    /// The kernel calls <see cref="Tick"/> on a background thread with a read-only
    /// Snapshot-on-Demand view of the simulation state.</para>
    ///
    /// <para><b>Memory:</b> Allocates a module-private <see cref="SpatialHashGrid"/> at
    /// construction time.  Call <see cref="Dispose"/> when the module is torn down to release
    /// the underlying native arrays.</para>
    ///
    /// <para><b>System registration:</b>
    /// All four systems—<see cref="LocalGridBuilderSystem"/>, <see cref="VisionBroadphaseSystem"/>,
    /// <see cref="LosRequestBatchingSystem"/>, and <see cref="ThreatEvaluationSystem"/>—are
    /// registered via <see cref="RegisterSystems"/>. All four implement <see cref="IModuleSystem"/>
    /// and run on the background thread inside <see cref="Tick"/>.</para>
    ///
    /// <para><b>Physics-accurate LOS:</b> Pass a <paramref name="colliderRadiusReader"/> delegate
    /// to enable accurate segment-circle occlusion checks in production LOS mode.  Use:
    /// <code>
    /// (view, e) => view.HasComponent&lt;PhysicsCollider&gt;(e)
    ///              ? view.GetComponentRO&lt;PhysicsCollider&gt;(e).Radius : 0f
    /// </code>
    /// When <c>null</c>, occluders are treated as dimensionless points.</para>
    /// </summary>
    public sealed class AutonomousPerceptionModule : IModule, IDisposable
    {
        /// <inheritdoc/>
        public string Name => "AutonomousPerception";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        // Module-private spatial grid. Shares native-memory pointers with the two grid systems.
        private readonly SpatialHashGrid _localGrid;

        // ── Perception systems ─────────────────────────────────────────────────────

        private readonly LocalGridBuilderSystem   _localGridBuilder;
        private readonly VisionBroadphaseSystem   _visionBroadphase;
        private readonly LosRequestBatchingSystem _losRequestBatching;
        private readonly ThreatEvaluationSystem   _threatEvaluation;

        /// <summary>
        /// Initialises the module and allocates the module-private spatial grid.
        /// </summary>
        /// <param name="colliderRadiusReader">
        /// Optional delegate for reading the bounding radius of each candidate collider entity.
        /// When supplied, enables physics-accurate segment-circle occlusion tests in production
        /// LOS mode.  Pass <c>null</c> to treat all occluders as point entities.
        /// See <see cref="LosRequestBatchingSystem.ColliderRadiusReader"/>.
        /// </param>
        public AutonomousPerceptionModule(
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null)
        {
            _localGrid = SpatialHashGrid.Create(
                PerceptionConstants.LocalGridWidth,
                PerceptionConstants.LocalGridHeight,
                PerceptionConstants.LocalGridCellSize,
                PerceptionConstants.LocalGridMaxEntities,
                Allocator.Persistent);

            _localGridBuilder   = new LocalGridBuilderSystem(_localGrid);
            _visionBroadphase   = new VisionBroadphaseSystem(_localGrid);
            _losRequestBatching = new LosRequestBatchingSystem(
                mockMode: false,
                colliderRadiusReader: colliderRadiusReader);
            _threatEvaluation   = new ThreatEvaluationSystem();
        }

        /// <summary>
        /// Registers all four perception systems into the kernel registry.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry)
        {
            // All four systems are executed directly inside Tick() using the SlowBackground
            // direct-execution pattern (same as PerceptionModule).  No kernel-level
            // system-scheduler registration is required or supported.
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Runs all four perception sub-systems in pipeline order on the background thread:
        /// LocalGridBuilder → VisionBroadphase → LosRequestBatching → ThreatEvaluation.
        /// </remarks>
        public void Tick(ISimulationView view, float dt)
        {
            _localGridBuilder.Execute(view, dt);
            _visionBroadphase.Execute(view, dt);
            _losRequestBatching.Execute(view, dt);
            _threatEvaluation.Execute(view, dt);
        }

        /// <summary>Disposes the module-private <see cref="SpatialHashGrid"/>.</summary>
        public void Dispose() => _localGrid.Dispose();
    }
}
    /// <see cref="IModule"/> that can be installed independently of the Brain modules.
