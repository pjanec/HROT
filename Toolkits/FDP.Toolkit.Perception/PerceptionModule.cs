using System.Collections.Generic;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Perception.Systems;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception
{
    /// <summary>
    /// Async perception module — runs at 10 Hz on a background thread using the
    /// Snapshot-on-Demand (SoD) execution policy.
    /// <para>
    /// This module orchestrates the two async perception subsystems:
    /// <list type="bullet">
    ///   <item><see cref="VisionBroadphaseSystem"/> — faction + FOV filtering → emits <see cref="LosCheckRequestEvent"/>.</item>
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
    /// </summary>
    public class PerceptionModule : IModule
    {
        /// <inheritdoc/>
        public string Name => "Perception";

        /// <summary>
        /// Runs asynchronously at 10 Hz on a background thread.
        /// The kernel provides a read-only snapshot of the simulation state.
        /// </summary>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        // The two subsystems executed in Tick().
        private readonly VisionBroadphaseSystem _visionBroadphase = new();
        private readonly ThreatEvaluationSystem _threatEvaluation = new();

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
            // Order matters: broadphase emits LosCheckRequestEvents into the ECB;
            // the LosRequestBatchingSystem (main thread) will consume them next frame.
            // ThreatEvaluation consumes TargetVisibleEvents that were published by
            // LosRequestBatchingSystem in the *previous* frame's ECB flush.
            _visionBroadphase.Execute(view, deltaTime);
            _threatEvaluation.Execute(view, deltaTime);
        }
    }
}
