using System;
using System.Collections.Generic;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using Fdp.Interfaces;
using ModuleHost.Network.Cyclone.Topics;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;

namespace ModuleHost.Network.Cyclone.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public class CycloneNetworkCleanupSystem : IModuleSystem
    {
        private readonly Fdp.Interfaces.IDescriptorTranslator _translator;
        private readonly Dictionary<long, Entity> _trackedEntities = new();
        
        public CycloneNetworkCleanupSystem(Fdp.Interfaces.IDescriptorTranslator translator)
        {
            _translator = translator;
        }

        public void Execute(ISimulationView view, float dt)
        {
            // 1. Scan for new entities to track (all lifecycle states — entities may be in
            //    Constructing, Active, or TearDown when they first need to be tracked).
            var query = view.Query()
                .WithLifecycle(EntityLifecycle.All)
                .With<NetworkIdentity>()
                .With<NetworkOwnership>()
                .Build();
            
            foreach (var entity in query)
            {
                 ref readonly var ownership = ref view.GetComponentRO<NetworkOwnership>(entity);
                 if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;
                 
                 ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(entity);
                 long netId = identity.Value;
                 
                 if (!_trackedEntities.ContainsKey(netId))
                 {
                     _trackedEntities[netId] = entity;
                 }
            }
            
            // 2. Scan tracked entities for deleted ones
            List<long>? toRemove = null;

            foreach (var kvp in _trackedEntities)
            {
                if (!view.IsAlive(kvp.Value)) // Entity is effectively dead if IsAlive returns false
                {
                    if (toRemove == null) toRemove = new List<long>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                foreach (var netId in toRemove)
                {
                    FdpLog<CycloneNetworkCleanupSystem>.Info(
                        "Detected entity destruction {0}, sending dispose.",
                        netId);
                    try 
                    {
                        _translator.Dispose(netId);
                    }
                    catch (Exception ex)
                    {
                         FdpLog<CycloneNetworkCleanupSystem>.Error(
                             "Failed to dispose entity {0}: {1}",
                             netId,
                             ex.Message);
                    }
                    _trackedEntities.Remove(netId);
                }
            }
        }
    }
}
