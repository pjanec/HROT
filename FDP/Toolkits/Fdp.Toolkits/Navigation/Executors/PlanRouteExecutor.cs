using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdPlanRoute"/>.
    /// Issues a pathfinding request without starting movement.
    /// Returns Success when <see cref="NavigationStatus.Result"/> is
    /// <see cref="NavigationResult.PathFound"/>; Failure on <see cref="NavigationResult.NoPath"/>
    /// or other non-recoverable results.
    /// </summary>
    public sealed class PlanRouteExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Increments <see cref="NavigationIntent.IntentId"/> so that the matching
        /// <see cref="NavigationStatus"/> can be identified later, and sets the channel Running.
        /// Mode is left as None because PlanRoute does not start corridor-following.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            PlanRouteParams p;
            fixed (byte* src = channel.Params)
                p = *(PlanRouteParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode             = NavigationMode.None;
            intent.FinalDestination = p.Destination;
            intent.TargetSpeed      = p.Speed;
            intent.ArrivalRadius    = p.ArrivalRadius;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Polls <see cref="NavigationStatus"/> written by the Muscle layer.
        /// <see cref="NavigationResult.PathFound"/> → Success; non-recoverable failures → Failure.
        /// </summary>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            if (status.IntentId != intent.IntentId)
                return;   // stale; keep Running

            switch (status.Result)
            {
                case NavigationResult.PathFound:
                    channel.Status = NodeStatus.Success;
                    break;

                case NavigationResult.NoPath:
                case NavigationResult.FailedUnreachable:
                case NavigationResult.FailedNoLayer:
                case NavigationResult.FailedInvalidHandle:
                    channel.Status = NodeStatus.Failure;
                    break;

                case NavigationResult.InProgress:
                default:
                    break;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Clears <see cref="NavigationIntent.Mode"/> and increments IntentId to cancel any
        /// pending Muscle-side activity.
        /// </summary>
        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.TargetSpeed = 0f;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
