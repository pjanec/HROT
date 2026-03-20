using System;
using System.Numerics;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Fdp.Examples.NetworkDemo.Modules
{
    [ExecutionPolicy(ExecutionMode.Synchronous)]
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [WatchEvents(typeof(DetonationEvent))]
    public class DamageControlModule : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float dt)
        {
            // Only executes when DetonationEvent occurs
            var events = view.ConsumeEvents<DetonationEvent>();
            
            foreach (var evt in events) {
                // Apply damage to nearby entities
                var query = view.Query()
                    .With<SimTransform>()
                    .With<Health>()
                    .Build();
                
                var cmd = view.GetCommandBuffer();
                
                foreach (var entity in query) {
                    var tf = view.GetComponentRO<SimTransform>(entity);
                    float distance = Vector3.Distance(tf.Position, evt.Position);
                    
                    if (distance < evt.Radius) {
                        var health = view.GetComponentRO<Health>(entity);
                        float damage = evt.Damage * (1 - distance / evt.Radius);
                        
                        cmd.SetComponent(entity, new Health {
                            Value = Math.Max(0, health.Value - damage)
                        });
                    }
                }
            }
        }
    }
}
