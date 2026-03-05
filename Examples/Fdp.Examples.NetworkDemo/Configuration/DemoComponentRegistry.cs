using System;
using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.Examples.NetworkDemo.Events; // Added
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network;
using ModuleHost.Network.Cyclone.Components;
using FDP.Toolkit.Lifecycle.Events;

namespace Fdp.Examples.NetworkDemo.Configuration
{
    public static class DemoComponentRegistry
    {
        public static void Register(EntityRepository world)
        {
            // Events
            world.RegisterEvent<FireInteractionEvent>();
            world.RegisterEvent<ConstructionOrder>();
            world.RegisterEvent<ConstructionAck>();   // Required for ELM ACK flow
            world.RegisterEvent<DestructionAck>();    // Required for ELM destruction flow
            world.RegisterEvent<DestructionOrder>();  // Required for ELM teardown flow

            // Legacy components
            // Components retired in favor of SimTransform
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<PositionGeodetic>();
            world.RegisterComponent<EntityType>();
            world.RegisterComponent<LifecycleDescriptor>();
            
            // Toolkit components
            world.RegisterComponent<NetworkTransform>();
            world.RegisterComponent<NetworkVelocity>();
            world.RegisterComponent<NetworkOrientation>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkSpawnRequest>();
            world.RegisterComponent<PendingNetworkAck>();
            world.RegisterComponent<ForceNetworkPublish>();

            // Batch-03 Components
            // DemoPosition replaced by SimTransform
            world.RegisterComponent<TurretState>();
            world.RegisterComponent<TimeConfiguration>();
            world.RegisterComponent<ReplayTime>();
            world.RegisterComponent<NetworkAuthority>();
            world.RegisterManagedComponent<DescriptorOwnership>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<TimeModeComponent>();
            world.RegisterComponent<FrameAckComponent>();
            world.RegisterManagedComponent<SquadChat>();

            // Demo tracking
            world.RegisterComponent<NetworkedEntity>(); 
        }

        public static System.Collections.Generic.IEnumerable<System.Type> GetAllTypes()
        {
            return new System.Type[]
            {
                typeof(SimTransform),
                typeof(SimVelocity),
                typeof(PositionGeodetic),
                typeof(EntityType),
                typeof(NetworkTransform),
                typeof(NetworkVelocity),
                typeof(NetworkOrientation),
                typeof(NetworkOwnership),
                typeof(NetworkIdentity),
                typeof(NetworkSpawnRequest),
                typeof(PendingNetworkAck),
                typeof(ForceNetworkPublish),
                typeof(TurretState),
                typeof(TimeConfiguration),
                typeof(ReplayTime),
                typeof(NetworkAuthority),
                typeof(DescriptorOwnership),
                typeof(Health),
                typeof(TimeModeComponent),
                typeof(FrameAckComponent),
                typeof(SquadChat),
                typeof(NetworkedEntity)
            };
        }

        /// <summary>
        /// Returns explicit descriptor ordinal mappings for component types that cannot carry
        /// <see cref="FDP.Interfaces.Abstractions.FdpDescriptorAttribute"/> directly
        /// (e.g. kernel primitives defined in external assemblies).
        /// The ordinal determines which authority key is checked when copying components during replay.
        /// </summary>
        public static IReadOnlyDictionary<Type, long> GetExplicitOrdinalMappings()
        {
            return new Dictionary<Type, long>
            {
                [typeof(SimTransform)]    = DemoDescriptors.Physics,
                [typeof(SimVelocity)]     = DemoDescriptors.Physics,
                // NetworkTransform is the network-transmitted position+rotation; must be restored during replay
                // so that TransformSyncSystem (driveFromNetwork=true) lerps SimTransform toward the
                // correct recorded positions rather than toward (0,0,0).
                [typeof(NetworkTransform)] = DemoDescriptors.Physics,
            };
        }
    }
}
