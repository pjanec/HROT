using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinem.Core;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;

namespace FDP.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdMoveTo"/> action.
    /// On entry it writes <see cref="NavState.FinalDestination"/>, <see cref="NavState.ArrivalRadius"/>,
    /// <see cref="NavState.TargetSpeed"/> and <see cref="NavigationMode.Direct"/> into the entity's
    /// <see cref="NavState"/>. Each tick it checks <see cref="NavState.HasArrived"/> for success and
    /// applies a frustration guard to detect stuck vehicles.
    /// </summary>
    public sealed class MoveToExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── Frustration counter storage ───────────────────────────────────────────────────────────
        // IActionExecutor instances are singletons shared across all entities (one per action type,
        // allocated at system startup). A simple `int _stuckTicks` field would be incorrectly shared
        // across entities. We use a Dictionary<int, int> keyed by entity.Index instead.
        // Trade-offs:
        //   + Zero per-entity ECS component overhead; no schema changes needed.
        //   + Allocation happens once at startup; dictionary is reused for the lifetime of the system.
        //   - Dictionary look-up is O(1) amortised but not as cache-friendly as a component array.
        //   Acceptable for the frustration path (called at most once per tick per moving entity).
        private readonly Dictionary<int, int> _stuckTicks = new();

        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            MoveToParams p;
            fixed (byte* src = channel.Params)
                p = *(MoveToParams*)src;

            var nav = world.GetComponent<NavState>(entity);
            nav.Mode             = NavigationMode.Direct;
            nav.FinalDestination = p.Destination;
            nav.ArrivalRadius    = p.ArrivalRadius;
            nav.TargetSpeed      = p.Speed;
            nav.HasArrived       = 0;
            world.SetComponent(entity, nav);

            // Reset the per-entity stuck counter so a fresh action starts clean.
            _stuckTicks[entity.Index] = 0;

            channel.Status = Fbt.NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            var nav = world.GetComponent<NavState>(entity);

            // ── Arrival check ─────────────────────────────────────────────────────────────────
            if (nav.HasArrived != 0)
            {
                channel.Status = Fbt.NodeStatus.Success;
                return;
            }

            // ── Frustration guard ─────────────────────────────────────────────────────────────
            MoveToParams p;
            fixed (byte* src = channel.Params)
                p = *(MoveToParams*)src;

            var tf  = world.GetComponent<SimTransform>(entity);
            var vel = world.GetComponent<SimVelocity>(entity);

            var pos2D  = new Vector2(tf.Position.X, tf.Position.Y);
            float dist = Vector2.Distance(pos2D, p.Destination);

            if (vel.Linear.Length() < NavigationConstants.FrustrationSpeedThreshold
                && dist > p.ArrivalRadius * 2f)
            {
                _stuckTicks.TryGetValue(entity.Index, out int ticks);
                ticks++;
                _stuckTicks[entity.Index] = ticks;

                if (ticks > NavigationConstants.FrustrationTickThreshold)
                {
                    channel.Status = Fbt.NodeStatus.Failure;
                    return;
                }
            }
            else
            {
                // Vehicle is moving (or within double-arrival) — reset counter.
                _stuckTicks[entity.Index] = 0;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // INVARIANT: channel still holds the OUTGOING action's IDs here.
            // Stop the vehicle by zeroing TargetSpeed and clearing navigation mode.
            var nav = world.GetComponent<NavState>(entity);
            nav.TargetSpeed = 0f;
            nav.Mode        = NavigationMode.None;
            world.SetComponent(entity, nav);

            // Clean up the frustration counter to avoid stale entries for recycled entity indices.
            _stuckTicks.Remove(entity.Index);
        }
    }
}
