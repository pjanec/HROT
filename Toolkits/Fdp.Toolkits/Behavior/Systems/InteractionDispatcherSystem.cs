using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fbt;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Routes the active <see cref="InteractionChannel"/> to the registered executor.
    /// Checks <see cref="ActorCapabilities.CanInteract"/> before dispatching.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in ActionDispatchModule.
    public class InteractionDispatcherSystem : DispatcherSystemBase<InteractionChannel>
    {
        public override void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(InteractionDispatcherSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<InteractionChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var channel = ref repo.GetComponentRW<InteractionChannel>(entity);
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                // Capability check: no interaction -- fail the channel immediately.
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanInteract)
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

                    // Note: at the time OnExit is called, channel.ActiveAction and channel.ActionInstanceId
                    // still hold the OUTGOING action's values. DispatchedInstanceId is updated after this call.
                    // This allows OnExit to identify what it is cleaning up.
                    _executors[oldAction]?.OnExit(entity, ref channel, repo);
                    _executors[channel.ActiveAction]?.OnEnter(entity, ref channel, repo);

                    channel.DispatchedInstanceId = channel.ActionInstanceId;
                    _previousAction[entity.Index] = channel.ActiveAction;
                }

                // Execute: drive the current action each tick.
                if (channel.ActiveAction != 0 && channel.Status == NodeStatus.Running)
                {
                    _executors[channel.ActiveAction]?.Execute(entity, ref channel, repo, deltaTime);

                    // Guard: if Execute() destroyed the entity, call OnExit to avoid state leaks.
                    // Note: one-frame gap remains for entities destroyed by other systems
                    // (DEBT-024 partial mitigation).
                    if (!repo.IsAlive(entity))
                    {
                        if (channel.ActiveAction != 0)
                            _executors[channel.ActiveAction]?.OnExit(entity, ref channel, repo);
                        continue;
                    }
                }
            }
        }
    }
}
