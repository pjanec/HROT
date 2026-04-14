using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using FDP.Toolkit.Replication.Components;
using System.Numerics;
using System;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class RefactoredPlayerInputSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            var query = view.Query()
                .With<SimVelocity>()
                .With<NetworkIdentity>()
                .With<Fdp.ModuleHost_Core.Network.NetworkOwnership>() // Check Local ownership
                .Build();

            foreach (var e in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<Fdp.ModuleHost_Core.Network.NetworkOwnership>(e);
                // Only modify local entities
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                    continue;

                ref readonly var vel = ref view.GetComponentRO<SimVelocity>(e);
                
                // Simple behavior: turn velocity vector slightly (circle)
                // Rotate vector around Z axis
                var v = vel.Linear;
                if (v.LengthSquared() > 0.001f)
                {
                    // Rotate by 1.0 radians/sec
                    float angle = 1.0f * deltaTime; 
                    float cos = (float)Math.Cos(angle);
                    float sin = (float)Math.Sin(angle);
                    
                    float newX = v.X * cos - v.Y * sin;
                    float newY = v.X * sin + v.Y * cos;
                    
                    cmd.SetComponent(e, new SimVelocity { Linear = new Vector3(newX, newY, v.Z), Angular = vel.Angular });
                }
            }
        }
    }
}
