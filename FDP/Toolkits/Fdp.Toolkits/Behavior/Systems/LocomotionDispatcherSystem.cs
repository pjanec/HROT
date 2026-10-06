using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
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

            // Cleanly terminate active actions for entities entering TearDown.
            // The 1-frame ELM delay guarantees the entity and its channel are still intact.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (repo.HasComponent<LocomotionChannel>(evt.Entity))
                {
                    ref var ch = ref repo.GetComponentRW<LocomotionChannel>(evt.Entity);
                    if (ch.ActiveAction != 0)
                    {
                        _executors[ch.ActiveAction]?.OnExit(evt.Entity, ref ch, repo);
                        ch.ActiveAction = 0;
                    }
                }
            }

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
                    // ⭐ CE-3091 — losing CanMove (death, a mobility hit, embarking) ENDS the running move: its executor's OnExit runs
                    //   ONCE here — for MoveTo that is the STOP (NavigationIntent Mode None) the mover on SimHost follows. ⛔ Before,
                    //   this `continue` skipped the lifecycle block below, so OnExit never ran and SimHost kept driving the last path:
                    //   measured live, a killed unit walked ~80 m to its mission's destination (U4). BD1 §1.0b: finishing clears every
                    //   channel. If the capability comes back, the behaviour's re-issued action (a new ActionInstanceId) enters afresh.
                    EnsurePreviousActionCapacity(entity.Index + 1);
                    ushort running = _previousAction[entity.Index];
                    if (running != 0)
                    {
                        _executors[running]?.OnExit(entity, ref channel, repo);
                        _previousAction[entity.Index] = 0;
                        channel.DispatchedInstanceId = channel.ActionInstanceId;
                    }
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
                }
            }
        }
    }
}
