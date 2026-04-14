using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using Fdp.Interfaces; 
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.ModuleHost.Core.Network;
using Fdp.ModuleHost.Core.Network.Interfaces;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.ModuleHost.Network.Cyclone.Services;
using Fdp.ModuleHost.Network.Cyclone.Systems;
using Fdp.ModuleHost.Network.Cyclone.Providers;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services; 

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap; 
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using INetworkTopology = Fdp.Interfaces.INetworkTopology;
using NetworkGatewaySystem = FDP.Toolkit.Replication.Systems.NetworkGatewaySystem;

namespace Fdp.ModuleHost.Network.Cyclone.Modules
{
    public class CycloneNetworkModule : IEcsModule
    {
        public string Name => "CycloneNetwork";
        
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly DdsParticipant _participant;
        private readonly NodeIdMapper _nodeMapper;
        private readonly INetworkIdAllocator _idAllocator;
        private readonly INetworkTopology _topology;
        private readonly EntityLifecycleModule _elm;
        
        // Translators and Services
        private NetworkEntityMap _entityMap;
        
        // Dynamic / Custom Translators
        private readonly List<IDescriptorTranslator> _customTranslators = new();
        
        private NetworkGatewaySystem _gatewaySystem;
        private readonly int _reliableInitTimeoutFrames;

        public CycloneNetworkModule(
            DdsParticipant participant,
            NodeIdMapper nodeMapper,
            INetworkIdAllocator idAllocator,
            INetworkTopology topology,
            EntityLifecycleModule elm,
            Fdp.Interfaces.ISerializationRegistry? serializationRegistry = null,
            IEnumerable<IDescriptorTranslator>? customTranslators = null,
            NetworkEntityMap? sharedEntityMap = null,
            int reliableInitTimeoutFrames = -1)
        {
            _participant = participant ?? throw new ArgumentNullException(nameof(participant));
            _nodeMapper = nodeMapper ?? throw new ArgumentNullException(nameof(nodeMapper));
            _idAllocator = idAllocator ?? throw new ArgumentNullException(nameof(idAllocator));
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _elm = elm ?? throw new ArgumentNullException(nameof(elm));
            _reliableInitTimeoutFrames = reliableInitTimeoutFrames;
            
            // Initialize Services
            _entityMap = sharedEntityMap ?? new NetworkEntityMap();

            if (serializationRegistry != null)
            {
                // Register Serialization Providers
                serializationRegistry.Register(1001, new CycloneSerializationProvider<NetworkTransform>());
                serializationRegistry.Register(1002, new CycloneSerializationProvider<NetworkVelocity>());
                serializationRegistry.Register(1003, new CycloneSerializationProvider<NetworkIdentity>());
                serializationRegistry.Register(1004, new CycloneSerializationProvider<TkbIdentity>());
            }

            if (customTranslators != null)
            {
                _customTranslators.AddRange(customTranslators);
            }
            
            _gatewaySystem = new NetworkGatewaySystem(101, _nodeMapper.LocalNodeId, _topology, _elm, _reliableInitTimeoutFrames);
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            // Use only the externally-provided custom translators.
            // The generic EntityMasterTranslator / EntityStateTranslator have been
            // removed; concrete applications supply their own domain translators.
            var allTranslators = new List<IDescriptorTranslator>(_customTranslators);

            // Register Ingress
            registry.RegisterSystem(new CycloneNetworkIngressSystem(
                allTranslators.ToArray()
            ));
            
            // Register Egress
            registry.RegisterSystem(new CycloneEgressSystem(
                allTranslators.ToArray()
            ));

            // NOTE: CycloneNetworkCleanupSystem is NOT registered here.
            // Applications must provide it directly (e.g., SimHostApp registers it
            // with EntityMasterEgressTranslator to handle entity lifecycle disposal).
            
            // Register Gateway
             registry.RegisterSystem(_gatewaySystem);
        }

        public void Tick(ISimulationView view, float deltaTime)
        {
             // Empty - Systems are registered
        }
    }

    // Local implementation of Ingress System since it appears missing from Core
    [UpdateInPhase(SystemPhase.Input)]
    public class CycloneNetworkIngressSystem : IEcsModuleSystem
    {
        private readonly Fdp.Interfaces.IDescriptorTranslator[] _translators;
        
        public CycloneNetworkIngressSystem(Fdp.Interfaces.IDescriptorTranslator[] translators)
        {
             _translators = translators;
        }
        
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            for(int i=0; i<_translators.Length; i++)
            {
                    _translators[i].PollIngress(cmd, view);
            }
        }
    }
}
