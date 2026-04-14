using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using FDP.Toolkit.Replication.Components;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class PhysicsSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            var query = view.Query()
                .With<SimTransform>()
                .With<SimVelocity>()
                 // Only move local entities (remote positions come from network)
                .With<Fdp.ModuleHost.Network.NetworkOwnership>() 
                .Build();

            foreach (var e in query)
            {
                ref readonly var ownership = ref view.GetComponentRO<Fdp.ModuleHost.Network.NetworkOwnership>(e);
                if (ownership.PrimaryOwnerId != ownership.LocalNodeId)
                    continue;

                ref readonly var velocity = ref view.GetComponentRO<SimVelocity>(e);
                
                // Skip write when stationary: a zero-velocity ECB flushed next frame would
                // overwrite any externally-set SimTransform (e.g. test harness direct writes).
                if (velocity.Linear.LengthSquared() < 1e-8f && velocity.Angular.LengthSquared() < 1e-8f)
                    continue;

                ref readonly var transform = ref view.GetComponentRO<SimTransform>(e);
                var newPos = transform.Position + velocity.Linear * deltaTime;
                
                // We update SimTransform
                cmd.SetComponent(e, new SimTransform { Position = newPos, Rotation = transform.Rotation });
            }
        }
    }
}
