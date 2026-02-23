using System;
using System.Collections.Generic;
using System.Numerics;
using ModuleHost.Core.Abstractions;
using Fdp.Kernel;
using Fdp.Examples.BattleRoyale.Components;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network;

namespace Fdp.Examples.BattleRoyale.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class NetworkSyncSystem : IModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            
            // Query entities that have both local and network representation
            var query = view.Query()
                .With<NetworkPosition>()
                .With<SimTransform>()
                .With<NetworkOwnership>()
                .Build();

            foreach (var entity in query)
            {
                var ownership = view.GetComponentRO<NetworkOwnership>(entity);
                
                if (ownership.PrimaryOwnerId == ownership.LocalNodeId)
                {
                    // EGRESS: Local Authority -> Network State
                    // We own this entity, so we push our simulation state to the network component
                    var localTransform = view.GetComponentRO<SimTransform>(entity);
                    cmd.SetComponent(entity, new NetworkPosition { Value = localTransform.Position });
                }
                else
                {
                    // INGRESS: Network State -> Local Simulation
                    // Someone else owns this, so we pull their state into our simulation
                    var netPos = view.GetComponentRO<NetworkPosition>(entity);
                    
                    // We should preserve rotation if possible, but here we only sync position
                    // We'll update just the position part of SimTransform if it exists, or create new.
                    // Since query requires SimTransform, it exists.
                    // But SetComponent overwrites. We need to handle Rotation.
                    // For now, let's just create a new SimTransform with Identity rotation if we don't have better way,
                    // OR if SimTransform is already there (it is), we should read it to get rotation.
                    var currentTransform = view.GetComponentRO<SimTransform>(entity);
                    cmd.SetComponent(entity, new SimTransform { 
                        Position = netPos.Value,
                        Rotation = currentTransform.Rotation 
                    });
                }
            }
        }
    }
}
