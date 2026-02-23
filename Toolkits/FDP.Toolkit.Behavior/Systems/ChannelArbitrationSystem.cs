using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using Fbt; 

namespace FDP.Toolkit.Behavior.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class ChannelArbitrationSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            // Process Locomotion Channels
            var qLoco = World.Query()
                .With<DoctrineState>()
                .With<LocomotionChannel>()
                .Build();

            foreach (var entity in qLoco)
            {
                var doctrine = World.GetComponent<DoctrineState>(entity);
                var channel = World.GetComponent<LocomotionChannel>(entity);

                if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
                {
                    // Stale channel - clear it
                    channel = default;
                    World.SetComponent(entity, channel);
                }
            }

            // Process Weapon Channels
            var qWpn = World.Query()
                .With<DoctrineState>()
                .With<WeaponChannel>()
                .Build();

            foreach (var entity in qWpn)
            {
                var doctrine = World.GetComponent<DoctrineState>(entity);
                var channel = World.GetComponent<WeaponChannel>(entity);

                if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
                {
                    channel = default;
                    World.SetComponent(entity, channel);
                }
            }

            // Process Interaction Channels
            var qInt = World.Query()
                .With<DoctrineState>()
                .With<InteractionChannel>()
                .Build();

            foreach (var entity in qInt)
            {
                var doctrine = World.GetComponent<DoctrineState>(entity);
                var channel = World.GetComponent<InteractionChannel>(entity);

                if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
                {
                    channel = default;
                    World.SetComponent(entity, channel);
                }
            }
        }
    }
}
