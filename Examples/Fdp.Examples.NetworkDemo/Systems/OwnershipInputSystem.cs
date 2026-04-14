using System;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class OwnershipInputSystem : IEcsModuleSystem
    {
        private readonly int _localNodeId;
        private readonly IEventBus _eventBus;
        private const long TURRET_DESCRIPTOR = 10;
        
        public OwnershipInputSystem(int localNodeId, IEventBus eventBus)
        {
            _localNodeId = localNodeId;
            _eventBus = eventBus;
        }
        
        public void Execute(ISimulationView view, float dt)
        {
            // Guard against environments without a console (e.g. test runners, headless)
            try
            {
                if (!Console.KeyAvailable) return;
            }
            catch (InvalidOperationException)
            {
                return; // No console attached
            }
            
            var key = Console.ReadKey(true).Key;
            
            if (key == ConsoleKey.O) {
                // Find the tank entity
                var query = view.Query()
                    .With<NetworkIdentity>()
                    .With<TurretState>()
                    .Build();
                
                foreach (var tank in query) {
                    // Check if we already have authority
                    if (view.HasAuthority(tank, TURRET_DESCRIPTOR)) {
                        Console.WriteLine("[Ownership] Already own turret");
                        continue;
                    }
                    
                    // Request ownership transfer
                    var netId = view.GetComponentRO<NetworkIdentity>(tank);
                    
                    var request = new OwnershipUpdateRequest {
                        EntityId = netId.Value,
                        DescriptorOrdinal = TURRET_DESCRIPTOR,
                        InstanceId = 0,
                        NewOwner = _localNodeId,
                        Timestamp = DateTime.UtcNow
                    };
                    
                    _eventBus.Publish(request);
                    Console.WriteLine($"[Ownership] Requesting turret control for entity {netId.Value}");
                    
                    break; // Only one tank for now
                }
            }
        }
    }
}
