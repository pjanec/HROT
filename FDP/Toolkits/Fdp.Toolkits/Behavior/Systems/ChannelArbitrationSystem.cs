using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fbt; 

namespace Fdp.Toolkit.Behavior.Systems
{
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateBefore(typeof(LocomotionDispatcherSystem))] -- ordering maintained by array position in ActionDispatchModule.
    // [UpdateBefore(typeof(WeaponDispatcherSystem))] -- ordering maintained by array position in ActionDispatchModule.
    // [UpdateBefore(typeof(InteractionDispatcherSystem))] -- ordering maintained by array position in ActionDispatchModule.
    public class ChannelArbitrationSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(ChannelArbitrationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // Process Locomotion Channels
            var qLoco = repo.Query()
                .With<BehaviorState>()
                .With<LocomotionChannel>()
                .Build();

            foreach (var entity in qLoco)
            {
                var behavior = repo.GetComponent<BehaviorState>(entity);
                ref var channel = ref repo.GetComponentRW<LocomotionChannel>(entity);

                if (channel.ActiveAction != 0 && channel.BehaviorInstanceId != behavior.InstanceId)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
                }
            }

            // Process Weapon Channels
            var qWpn = repo.Query()
                .With<BehaviorState>()
                .With<WeaponChannel>()
                .Build();

            foreach (var entity in qWpn)
            {
                var behavior = repo.GetComponent<BehaviorState>(entity);
                ref var channel = ref repo.GetComponentRW<WeaponChannel>(entity);

                if (channel.ActiveAction != 0 && channel.BehaviorInstanceId != behavior.InstanceId)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
                }
            }

            // Process Interaction Channels
            var qInt = repo.Query()
                .With<BehaviorState>()
                .With<InteractionChannel>()
                .Build();

            foreach (var entity in qInt)
            {
                var behavior = repo.GetComponent<BehaviorState>(entity);
                ref var channel = ref repo.GetComponentRW<InteractionChannel>(entity);

                if (channel.ActiveAction != 0 && channel.BehaviorInstanceId != behavior.InstanceId)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; }
                }
            }
        }
    }
}
