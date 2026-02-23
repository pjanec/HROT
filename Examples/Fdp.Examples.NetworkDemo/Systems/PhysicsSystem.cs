using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Components;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class PhysicsSystem : IModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            var query = view.Query()
                .With<SimTransform>()
                .With<SimVelocity>()
                 // Only move local entities (remote positions come from network)
                .With<ModuleHost.Core.Network.NetworkOwnership>() 
                .Build();

            foreach (var e in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<ModuleHost.Core.Network.NetworkOwnership>(e);
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                    continue;

                ref readonly var transform = ref view.GetComponentRO<SimTransform>(e);
                ref readonly var velocity = ref view.GetComponentRO<SimVelocity>(e);
                
                var newPos = transform.Position + velocity.Linear * deltaTime;
                
                // We update SimTransform
                cmd.SetComponent(e, new SimTransform { Position = newPos, Rotation = transform.Rotation });
            }
        }
    }
}
