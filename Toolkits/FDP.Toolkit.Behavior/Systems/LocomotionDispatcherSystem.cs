using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using Fbt;

namespace FDP.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Routes the active <see cref="LocomotionChannel"/> to the registered
    /// <see cref="Executors.IActionExecutor{TChannel}"/> using O(1) lookup.
    /// Checks <see cref="ActorCapabilities.CanMove"/> before dispatching.
    /// Fires OnEnter/OnExit lifecycle calls when <see cref="LocomotionChannel.ActionInstanceId"/> changes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class LocomotionDispatcherSystem : DispatcherSystemBase<LocomotionChannel>
    {
        protected override void OnUpdate()
        {
            var q = World.Query()
                .With<LocomotionChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var channel = ref World.GetComponentRW<LocomotionChannel>(entity);
                var caps = World.GetComponent<ActorCapabilityState>(entity);

                // Capability check: no locomotion → fail the channel immediately.
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanMove)
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
