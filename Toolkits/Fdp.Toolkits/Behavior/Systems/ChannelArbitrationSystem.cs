using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Components;
using Fbt; 

namespace Fdp.Toolkit.Behavior.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(LocomotionDispatcherSystem))]
    [UpdateBefore(typeof(WeaponDispatcherSystem))]
    [UpdateBefore(typeof(InteractionDispatcherSystem))]
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
                ref var channel = ref World.GetComponentRW<LocomotionChannel>(entity);

                if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
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
                ref var channel = ref World.GetComponentRW<WeaponChannel>(entity);

                if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
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
                ref var channel = ref World.GetComponentRW<InteractionChannel>(entity);

                if (channel.ActiveAction != 0 && channel.DoctrineInstanceId != doctrine.InstanceId)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
                }
            }
        }
    }
}
