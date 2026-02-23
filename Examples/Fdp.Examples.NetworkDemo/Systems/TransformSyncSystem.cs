using System;
using System.Numerics;
using Fdp.Kernel; // SimTransform
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using Fdp.Examples.NetworkDemo.Components;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class TransformSyncSystem : IModuleSystem
    {
        private const long CHASSIS_KEY = 5; // Chassis descriptor ordinal
        private const float SMOOTHING_RATE = 10.0f;

        public void Execute(ISimulationView view, float deltaTime)
        {
            SyncOwnedEntities(view);
            SyncRemoteEntities(view, deltaTime);
        }

        private void SyncOwnedEntities(ISimulationView view)
        {
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkPosition>()
                .With<NetworkAuthority>()
                .Build();

            var cmd = view.GetCommandBuffer();

            foreach (var entity in query)
            {
                // If we own the chassis, copy to network buffer
                if (view.HasAuthority(entity, CHASSIS_KEY))
                {
                    var appTf = view.GetComponentRO<SimTransform>(entity);
                    cmd.SetComponent(entity, new NetworkPosition
                    {
                        Value = appTf.Position
                    });
                }
            }
        }

        private void SyncRemoteEntities(ISimulationView view, float deltaTime)
        {
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkPosition>()
                .With<NetworkAuthority>()
                .Build();

            var cmd = view.GetCommandBuffer();

            foreach (var entity in query)
            {
                // If we DON'T own it, smooth toward network position
                if (!view.HasAuthority(entity, CHASSIS_KEY))
                {
                    var netPos = view.GetComponentRO<NetworkPosition>(entity);
                    var currentTf = view.GetComponentRO<SimTransform>(entity);

                    var smoothed = Vector3.Lerp(
                        currentTf.Position,
                        netPos.Value,
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
