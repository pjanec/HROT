using System.Numerics;
using Fdp.Core; // SimTransform
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;

namespace Fdp.Examples.Common.Systems
{
    /// <summary>
    /// Synchronises <see cref="SimTransform"/> with <see cref="NetworkTransform"/> for all
    /// entities that have network authority.
    ///
    /// <para><b>Owned entities</b> (PrimaryOwnerId == LocalNodeId): copies SimTransform →
    /// NetworkTransform so that the recorded/published position tracks actual movement.</para>
    ///
    /// <para><b>Remote entities</b> (or all entities when <paramref name="driveFromNetwork"/>
    /// is <c>true</c>): lerps the full authoritative <see cref="SimTransform"/> position —
    /// including Z — toward <see cref="NetworkTransform.LastPosition"/> at
    /// <see cref="SMOOTHING_RATE"/> × deltaTime. Since the 3D Cognitive Spatial Awareness
    /// promotion (P3D-103) altitude is authoritative on <c>SimTransform.Position.Z</c>; there is
    /// no separate visual Z correction.</para>
    ///
    /// <para><b>Placement note:</b> Originally defined in
    /// <c>Fdp.Examples.NetworkDemo.Systems</c>; duplicated here so that
    /// <c>Fdp.Examples.Scenarios</c> (and other projects that reference
    /// <c>Fdp.Examples.Common</c>) avoid a dependency on the full NetworkDemo project.
    /// NetworkDemo retains its own copy for its internal pipeline.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class TransformSyncSystem : IEcsModuleSystem
    {
        private const long CHASSIS_KEY = 5; // Chassis descriptor ordinal
        private const float SMOOTHING_RATE = 10.0f;
        private readonly bool _driveFromNetwork;

        /// <param name="driveFromNetwork">When true, treat all entities as remote-driven.</param>
        public TransformSyncSystem(bool driveFromNetwork = false)
        {
            _driveFromNetwork = driveFromNetwork;
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

                    // Smooth the full authoritative position (including Z) toward the network
                    // position. Altitude is authoritative on SimTransform.Position.Z (P3D-103);
                    // there is no separate visual Z correction.
                    var smoothed = Vector3.Lerp(
                        currentTf.Position,
                        netTf.LastPosition,
                        deltaTime * SMOOTHING_RATE
                    );

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
