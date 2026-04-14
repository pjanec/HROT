using System;
using System.Numerics;
using Fdp.Kernel; // SimTransform
using Fdp.ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Modules.Geographic.Components;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class TransformSyncSystem : IEcsModuleSystem
    {
        private const long CHASSIS_KEY = 5; // Chassis descriptor ordinal
        private const float SMOOTHING_RATE = 10.0f;
        private readonly bool _driveFromNetwork;
        private readonly float _groundClampZSmoothingRate;

        /// <param name="driveFromNetwork">When true, treat all entities as remote-driven.</param>
        /// <param name="groundClampZSmoothingRate">
        /// Lerp multiplier for <see cref="GroundClampingState"/> <c>CurrentZOffset</c> toward
        /// <c>TargetZOffset</c> each frame (<c>deltaTime * rate</c>). Defaults to 5; lower for
        /// demos that assert mid-convergence (e.g. DEM1-D007).
        /// </param>
        public TransformSyncSystem(bool driveFromNetwork = false, float groundClampZSmoothingRate = 5f)
        {
            _driveFromNetwork = driveFromNetwork;
            _groundClampZSmoothingRate = groundClampZSmoothingRate;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_driveFromNetwork)
            {
                // In replay mode, treat all entities as remote (driven by network/replay data)
                SyncRemoteEntities(view, deltaTime, forceAll: true);
            }
            else
            {
                SyncOwnedEntities(view);
                SyncRemoteEntities(view, deltaTime);
            }
        }

        private void SyncOwnedEntities(ISimulationView view)
        {
            // Include all lifecycle states: locally-owned entities may be in Constructing
            // (waiting for peer ACKs) but their NetworkTransform must still track SimTransform
            // so that the recorded NetworkTransform reflects actual movement.  If Constructing
            // entities are excluded, the replay's TransformSyncSystem will lerp SimTransform
            // toward the stale NetworkTransform.LastPosition=(0,0,0) and erase all recorded movement.
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkTransform>()
                .With<NetworkAuthority>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            var cmd = view.GetCommandBuffer();

            foreach (var entity in query)
            {
                // If we are the primary owner (PrimaryOwnerId == LocalNodeId), copy SimTransform→NetworkTransform
                ref readonly var auth = ref view.GetComponentRO<NetworkAuthority>(entity);
                if (auth.PrimaryOwnerId == auth.LocalNodeId)
                {
                    var appTf = view.GetComponentRO<SimTransform>(entity);
                    cmd.SetComponent(entity, new NetworkTransform
                    {
                        LastPosition = appTf.Position,
                        LastRotation = appTf.Rotation,
                    });
                }
            }
        }

        private void SyncRemoteEntities(ISimulationView view, float deltaTime, bool forceAll = false)
        {
            // Include all lifecycle states: in replay mode and during recording, entities may be
            // in Constructing lifecycle (waiting for peer ACKs). Excluding them would mean the
            // smoothing/copy never runs and SimTransform stays stale.
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkTransform>()
                .With<NetworkAuthority>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            var cmd = view.GetCommandBuffer();

            foreach (var entity in query)
            {
                // If we don't own it (remote entity), OR if replay forces all, smooth toward network position
                ref readonly var auth = ref view.GetComponentRO<NetworkAuthority>(entity);
                bool isRemote = forceAll || auth.PrimaryOwnerId != auth.LocalNodeId;
                if (isRemote)
                {
                    var netTf = view.GetComponentRO<NetworkTransform>(entity);
                    var currentTf = view.GetComponentRO<SimTransform>(entity);

                    var smoothed = Vector3.Lerp(
                        currentTf.Position,
                        netTf.LastPosition,
                        deltaTime * SMOOTHING_RATE
                    );

                    // ── Ground clamping Z-offset (MOD1-P7T5) ─────────────────────────
                    // When a GroundClampingState is present, lerp CurrentZOffset toward
                    // TargetZOffset and apply it to the visual Z axis.
                    // SimTransform.Position.Z remains the authoritative simulation altitude;
                    // the offset is a pure visual correction that does not feed back into
                    // the dead-reckoning calculation above.
                    if (view.HasComponent<GroundClampingState>(entity))
                    {
                        var clampState = view.GetComponentRO<GroundClampingState>(entity);
                        float newCurrentOffset = clampState.CurrentZOffset +
                            (clampState.TargetZOffset - clampState.CurrentZOffset) * (deltaTime * _groundClampZSmoothingRate);

                        cmd.SetComponent(entity, new GroundClampingState
                        {
                            TargetZOffset       = clampState.TargetZOffset,
                            CurrentZOffset      = newCurrentOffset,
                            LastValidIgAltitude = clampState.LastValidIgAltitude,
                            IgAltitudeBaselineEstablished = clampState.IgAltitudeBaselineEstablished,
                        });

                        smoothed = new Vector3(smoothed.X, smoothed.Y,
                            netTf.LastPosition.Z + newCurrentOffset);
                    }

                    // Preserve rotation
                    cmd.SetComponent(entity, new SimTransform { 
                        Position = smoothed,
                        Rotation = currentTf.Rotation
                    });
                }
            }
        }
    }
}
