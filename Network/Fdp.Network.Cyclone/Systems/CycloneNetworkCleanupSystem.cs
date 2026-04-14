using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Kernel;
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.ModuleHost.Core.Network;
using Fdp.Interfaces;
using Fdp.ModuleHost.Network.Cyclone.Topics;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;

namespace Fdp.ModuleHost.Network.Cyclone.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public class CycloneNetworkCleanupSystem : IEcsModuleSystem
    {
        private readonly Fdp.Interfaces.IDescriptorTranslator[] _translators;
        private readonly Dictionary<long, Entity> _trackedEntities = new();
        
        public CycloneNetworkCleanupSystem(IEnumerable<Fdp.Interfaces.IDescriptorTranslator> translators)
        {
            _translators = translators?.ToArray()
                ?? throw new ArgumentNullException(nameof(translators));
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
                 if (!ownership.HasAuthority) continue; // DB-MOD1-03: replaced PrimaryOwnerId != LocalNodeId
                 
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

                    foreach (var translator in _translators)
                    {
                        try
                        {
                            translator.Dispose(netId);
                        }
                        catch (Exception ex)
                        {
                            FdpLog<CycloneNetworkCleanupSystem>.Error(
                                "Translator {0} failed to dispose entity {1}: {2}",
                                translator.GetType().Name,
                                netId,
                                ex.Message);
                        }
                    }

                    _trackedEntities.Remove(netId);
                }
            }
        }
    }
}
