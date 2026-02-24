using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using Fbt;

namespace FDP.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Routes the active <see cref="WeaponChannel"/> to the registered executor.
    /// Checks <see cref="ActorCapabilities.CanShoot"/> before dispatching.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class WeaponDispatcherSystem : DispatcherSystemBase<WeaponChannel>
    {
        protected override void OnUpdate()
        {
            var q = World.Query()
                .With<WeaponChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var channel = ref World.GetComponentRW<WeaponChannel>(entity);
                var caps = World.GetComponent<ActorCapabilityState>(entity);

                // Capability check: no shooting → fail the channel immediately.
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanShoot)
                    && channel.Status == NodeStatus.Running)
                {
                    channel.Status = NodeStatus.Failure;
                    continue;
                }

                // Lifecycle: detect when a new action has been dispatched.
                if (channel.ActionInstanceId != channel.DispatchedInstanceId)
                {
                    EnsurePreviousActionCapacity(entity.Index + 1);
                    ushort oldAction = _previousAction[entity.Index];

                    _executors[oldAction]?.OnExit(entity, ref channel, World);
                    _executors[channel.ActiveAction]?.OnEnter(entity, ref channel, World);

                    channel.DispatchedInstanceId = channel.ActionInstanceId;
                    _previousAction[entity.Index] = channel.ActiveAction;
                }

                // Execute: drive the current action each tick.
                if (channel.ActiveAction != 0 && channel.Status == NodeStatus.Running)
                {
                    _executors[channel.ActiveAction]?.Execute(entity, ref channel, World, DeltaTime);
                }
            }
        }
    }
}
