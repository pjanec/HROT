using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdFollowPath"/>.
    /// Instructs the Muscle layer to follow a pre-loaded route identified by
    /// <see cref="FollowPathParams.RouteHandle"/>.
    /// Returns Success on <see cref="NavigationResult.Arrived"/>; Failure if the handle
    /// is invalid or the path becomes unreachable.
    /// </summary>
    public sealed class FollowPathExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Copies <see cref="FollowPathParams.RouteHandle"/> into
        /// <see cref="NavigationIntent"/> and sets the channel Running.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FollowPathParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowPathParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.None;
            intent.RouteHandle = p.RouteHandle;
            intent.TargetSpeed = p.Speed;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Observes <see cref="NavigationStatus"/>. Returns Success on Arrived;
        /// Failure on FailedInvalidHandle, FailedUnreachable, or NoPath.
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
                case NavigationResult.Arrived:
                    channel.Status = NodeStatus.Success;
                    break;

                case NavigationResult.FailedBlocked:
                case NavigationResult.FailedInvalidHandle:
                case NavigationResult.FailedUnreachable:
                case NavigationResult.NoPath:
                case NavigationResult.FailedNoLayer:
                    channel.Status = NodeStatus.Failure;
                    break;

                case NavigationResult.InProgress:
                default:
                    break;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

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
