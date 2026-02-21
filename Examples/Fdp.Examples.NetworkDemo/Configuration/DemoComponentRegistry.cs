using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Components;
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
            world.RegisterComponent<Position>();
            world.RegisterComponent<PositionGeodetic>();
            world.RegisterComponent<Velocity>();
            world.RegisterComponent<EntityType>();
            world.RegisterComponent<LifecycleDescriptor>();
            
            // Toolkit components
            world.RegisterComponent<NetworkPosition>();
            world.RegisterComponent<NetworkVelocity>();
            world.RegisterComponent<NetworkOrientation>();
            world.RegisterComponent<NetworkOwnership>();
            world.RegisterComponent<NetworkIdentity>();
            world.RegisterComponent<NetworkSpawnRequest>();
            world.RegisterComponent<PendingNetworkAck>();
            world.RegisterComponent<ForceNetworkPublish>();

            // Batch-03 Components
            world.RegisterComponent<DemoPosition>();
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
                typeof(Position),
                typeof(PositionGeodetic),
                typeof(Velocity),
                typeof(EntityType),
                typeof(NetworkPosition),
                typeof(NetworkVelocity),
                typeof(NetworkOrientation),
                typeof(NetworkOwnership),
                typeof(NetworkIdentity),
                typeof(NetworkSpawnRequest),
                typeof(PendingNetworkAck),
                typeof(ForceNetworkPublish),
                typeof(DemoPosition),
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
    }
}
