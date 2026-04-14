using System;
using System.Numerics;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Simulation)]
    public class RadarSystem : IEcsModuleSystem
    {
        private readonly IEventBus _eventBus;
        
        public RadarSystem(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }
        
        public void Execute(ISimulationView view, float dt)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<SimTransform>()
                .Build();
            
            foreach (var entity in query) {
                var tf = view.GetComponentRO<SimTransform>(entity);
                if (Vector3.Distance(tf.Position, Vector3.Zero) < 1000f) {
                    _eventBus.Publish(new RadarContactEvent {
                        EntityId = view.GetComponentRO<NetworkIdentity>(entity).Value,
                        Position = tf.Position,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
    }
}
