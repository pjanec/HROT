using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Topics;
using Fdp.Core.Logging;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Network.Cyclone.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public class CycloneNetworkCleanupSystem : IEcsModuleSystem
    {
        private readonly Fdp.Interfaces.IDescriptorTranslator[] _translators;
        private readonly Dictionary<long, Entity> _trackedEntities = new();
        private readonly Dictionary<IDescriptorTranslator, SystemProfileData> _translatorProfileData = new();

        public IReadOnlyList<IDescriptorTranslator> Translators => _translators;
        
        public CycloneNetworkCleanupSystem(IEnumerable<Fdp.Interfaces.IDescriptorTranslator> translators)
        {
            _translators = translators?.ToArray()
                ?? throw new ArgumentNullException(nameof(translators));

            foreach (var translator in _translators)
            {
                var translatorName = $"{translator.TopicName} [{translator.DescriptorOrdinal}]";
                _translatorProfileData[translator] = new SystemProfileData(translatorName);
            }
        }

        public SystemProfileData? GetTranslatorProfileData(IDescriptorTranslator translator)
            => _translatorProfileData.TryGetValue(translator, out var data) ? data : null;

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

            // 2. Dispose translators for entities entering TearDown via DestructionOrder.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (!view.HasComponent<NetworkIdentity>(evt.Entity))
                    continue;

                ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(evt.Entity);
                long netId = identity.Value;

                if (_trackedEntities.Remove(netId))
                {
                    FdpLog<CycloneNetworkCleanupSystem>.Info(
                        "Detected entity destruction {0}, sending dispose.",
                        netId);

                    foreach (var translator in _translators)
                    {
                        var sw = Stopwatch.StartNew();
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
                        finally
                        {
                            sw.Stop();
                            if (_translatorProfileData.TryGetValue(translator, out var profile))
                            {
                                profile.RecordExecution(sw.Elapsed.TotalMilliseconds);
                            }
                        }
                    }
                }
            }
        }
    }
}
