using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fbt;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Routes the active <see cref="LocomotionChannel"/> to the registered
    /// <see cref="Executors.IActionExecutor{TChannel}"/> using O(1) lookup.
    /// Checks <see cref="ActorCapabilities.CanMove"/> before dispatching.
    /// Fires OnEnter/OnExit lifecycle calls when <see cref="LocomotionChannel.ActionInstanceId"/> changes.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in ActionDispatchModule.
    public class LocomotionDispatcherSystem : DispatcherSystemBase<LocomotionChannel>
    {
        public override void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(LocomotionDispatcherSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<LocomotionChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                ref var channel = ref repo.GetComponentRW<LocomotionChannel>(entity);
                var caps = repo.GetComponent<ActorCapabilityState>(entity);

                // Capability check: no locomotion -- fail the channel immediately.
                // Guard applies unconditionally (not only when Running) to prevent a
                // first-activation bypass where Status is Inactive before OnEnter sets Running.
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanMove))
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

                // ── Same-frame OnEnter + Execute safety invariant ────────────────────────────
                // When an action first becomes active, OnEnter and Execute are both called in
                // the same frame (OnEnter sets up state; Execute runs the first tick).
                // ALL IActionExecutor implementations MUST be designed so that:
                //   1. OnEnter writes NavState/channel fields to valid initial values.
                //   2. The first Execute call (same frame) does NOT overwrite those writes
                //      under normal conditions (e.g. HasArrived=0, IsAlive=true, ReplanGate not yet open).
                // This invariant is verified in each Phase 3 executor's tests.
                // See BATCH-07 Q4 for analysis.

                // Execute: drive the current action each tick.
                if (channel.ActiveAction != 0 && channel.Status == NodeStatus.Running)
                {
                    _executors[channel.ActiveAction]?.Execute(entity, ref channel, repo, deltaTime);

                    // Guard: if Execute() destroyed the entity (e.g. lethal damage applied
                    // by the executor itself), call OnExit to avoid state leaks.
                    // Note: there is still a one-frame gap where OnExit is not called when
                    // the entity is destroyed by a DIFFERENT system -- the entity won't appear
                    // in this query on the next tick (DEBT-024 partial mitigation).
                    if (!repo.IsAlive(entity))
                    {
                        if (channel.ActiveAction != 0)
                            _executors[channel.ActiveAction]?.OnExit(entity, ref channel, repo);
                        // Cannot write back -- entity is dead.
                        continue;
                    }
                }
            }
        }
    }
}
