using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdFlee"/> action.
    /// Moves the entity away from a threat entity. Replans the flee vector every
    /// <see cref="NavigationConstants.FleeReplanIntervalTicks"/> ticks.
    /// Reports <see cref="NodeStatus.Success"/> when the threat is dead or safe distance is reached.
    ///
    /// <para><b>CQRS compliance (BS1-T018):</b> navigation commands are issued via
    /// <see cref="NavigationIntent"/> (Brain-side CQRS component) rather than writing
    /// <see cref="CarKinem.Core.NavState"/> directly, so this executor is safe to run on a
    /// Brain node that has no physics layer.</para>
    /// </summary>
    public sealed class FleeExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── FleeState storage ─────────────────────────────────────────────────────────────────────
        // FleeState (NextReplanTick) is persisted in LocomotionChannel.State (the 32-byte state slot).
        // This keeps all per-action-instance state in the channel, consistent with the channel design.
        // The executor does not need any class-level fields for per-entity state.

        // ── OnEnter ───────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FleeParams p;
            fixed (byte* src = channel.Params)
                p = *(FleeParams*)src;

            channel.Status = Fbt.NodeStatus.Running;

            // Compute initial flee destination immediately.
            var gt = world.GetSingletonUnmanaged<GlobalTime>();
            ComputeAndWriteFleeDestination(entity, p, world);
            WriteFleeState(ref channel, new FleeState
            {
                NextReplanTick = (uint)gt.FrameNumber + (uint)NavigationConstants.FleeReplanIntervalTicks
            });
        }


        // ── Execute ─────────────────────────────────────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            FleeParams p;
            fixed (byte* src = channel.Params)
                p = *(FleeParams*)src;

            // ── Stale threat guard (MANDATORY every tick) ─────────────────────────────────────
            // Uses the full Entity handle (Index + Generation) via world.IsAlive, which validates
            // the generation counter. A destroyed entity whose slot was reused has a new generation,
            // so the stored handle becomes stale and IsAlive returns false. This is the fix
            // propagated from DEBT-009.
            if (!world.IsAlive(p.Threat))
            {
                channel.Status = Fbt.NodeStatus.Success;
                return;
            }

            var myTf     = world.GetComponent<SimTransform>(entity);
            var threatTf = world.GetComponent<SimTransform>(p.Threat);

            var myPos     = new Vector2(myTf.Position.X,     myTf.Position.Y);
            var threatPos = new Vector2(threatTf.Position.X, threatTf.Position.Y);

            // ── Safe-distance check ───────────────────────────────────────────────────────────
            float dist = Vector2.Distance(myPos, threatPos);
            if (dist > p.SafeDistance)
            {
                channel.Status = Fbt.NodeStatus.Success;
                return;
            }

            // ── Throttled replan ──────────────────────────────────────────────────────────────
            var gt         = world.GetSingletonUnmanaged<GlobalTime>();
            var fleeState  = ReadFleeState(ref channel);
            uint currentTick = (uint)gt.FrameNumber;

            if (currentTick >= fleeState.NextReplanTick)
            {
                ComputeAndWriteFleeDestination(entity, p, world);
                fleeState.NextReplanTick = currentTick + (uint)NavigationConstants.FleeReplanIntervalTicks;
                WriteFleeState(ref channel, fleeState);
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // BS1-T018: cancel locomotion via NavigationIntent (DO NOT write NavState directly).
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.TargetSpeed = 0f;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────────

        private static unsafe void ComputeAndWriteFleeDestination(
            Entity entity, in FleeParams p, EntityRepository world)
        {
            var myTf     = world.GetComponent<SimTransform>(entity);
            var threatTf = world.GetComponent<SimTransform>(p.Threat);

            var myPos     = new Vector2(myTf.Position.X,     myTf.Position.Y);
            var threatPos = new Vector2(threatTf.Position.X, threatTf.Position.Y);

            Vector2 awayVec = myPos - threatPos;
            // Guard against zero-length vector (self and threat at same position).
            if (awayVec.LengthSquared() < 1e-6f)
                awayVec = Vector2.UnitX;
            awayVec = Vector2.Normalize(awayVec);

            // BS1-T018: write NavigationIntent (CQRS pattern) instead of NavState directly.
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode             = NavigationMode.DirectPoint;
            intent.FinalDestination = myPos + awayVec * p.SafeDistance;
            intent.TargetSpeed      = p.Speed;
            world.SetComponent(entity, intent);
        }

        private static unsafe FleeState ReadFleeState(ref LocomotionChannel channel)
        {
            fixed (byte* src = channel.State)
                return *(FleeState*)src;
        }

        private static unsafe void WriteFleeState(ref LocomotionChannel channel, FleeState state)
        {
            fixed (byte* dst = channel.State)
                *(FleeState*)dst = state;
        }
    }
}
