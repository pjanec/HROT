using System;
using Fdp.Core;
using Fdp.Modules.Geographic.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Modules.Geographic.Systems
{
    /// <summary>
    /// Runs during <see cref="SystemPhase.PostSimulation"/>.
    ///
    /// <para>
    /// Consumes the results written by <see cref="TerrainQuerySolverSystem"/> and
    /// updates each entity's <see cref="GroundClampingState"/> component via the
    /// command buffer.
    /// </para>
    ///
    /// <para><b>Jump-rejection filter:</b> if the incoming <c>HitZ</c> differs from the
    /// entity's <see cref="GroundClampingState.LastValidIgAltitude"/> by more than
    /// <see cref="JumpRejectionThresholdMeters"/>, the result is discarded.
    /// This prevents visual pops at geometry seams, tunnels, or bridge transitions.
    /// </para>
    ///
    /// <para><b>First-frame bootstrap:</b> while <see cref="GroundClampingState.IgAltitudeBaselineEstablished"/>
    /// is 0 the jump-rejection threshold is skipped so the first accepted hit seeds baseline state.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class TerrainQueryResolutionSystem : IEcsModuleSystem
    {
        /// <summary>Maximum Z-delta (metres) that is accepted as a valid terrain hit.</summary>
        public const float JumpRejectionThresholdMeters = 5f;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var world = (EntityRepository)view;

            if (!world.HasSingleton<TerrainQueryBatchData>()) return;
            ref readonly var batch = ref world.GetSingleton<TerrainQueryBatchData>();

            if (batch.Count == 0) return;

            var cmd = view.GetCommandBuffer();

            for (int i = 0; i < batch.Count; i++)
            {
                ref readonly var req = ref batch.Requests[i];
                ref readonly var res = ref batch.Results[i];

                if (!res.HasHit) continue;
                if (!view.IsAlive(req.Entity)) continue;
                if (!view.HasComponent<GroundClampingState>(req.Entity)) continue;

                ref readonly var current = ref view.GetComponentRO<GroundClampingState>(req.Entity);

                // Jump-rejection: skip results that move the reference altitude too abruptly.
                // Bootstrap: first accepted hit for this entity is always applied.
                bool isBootstrap = current.IgAltitudeBaselineEstablished == 0;
                bool withinThreshold = MathF.Abs(res.HitZ - current.LastValidIgAltitude) <= JumpRejectionThresholdMeters;

                if (!isBootstrap && !withinThreshold) continue;

                // TargetZOffset is the extra height the entity's visual node must rise to sit on terrain.
                float newTargetOffset = res.HitZ - req.ReferenceSimZ;

                var updated = new GroundClampingState
                {
                    TargetZOffset       = newTargetOffset,
                    CurrentZOffset      = current.CurrentZOffset, // Smoothed by TransformSyncSystem
                    LastValidIgAltitude = res.HitZ,
                    IgAltitudeBaselineEstablished = 1,
                };

                cmd.SetComponent(req.Entity, updated);
            }
        }
    }
}
