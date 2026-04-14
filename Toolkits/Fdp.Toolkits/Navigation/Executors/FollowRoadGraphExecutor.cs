using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdFollowRoadGraph"/> action.
    /// Issues a <see cref="NavigationMode.RoadGraph"/> <see cref="NavigationIntent"/> and reports
    /// <see cref="NodeStatus.Success"/> when <see cref="NavigationStatus.Result"/> is
    /// <see cref="NavigationResult.Arrived"/>.
    ///
    /// <para><b>CQRS compliance (BS1-T019):</b> this executor writes <see cref="NavigationIntent"/>
    /// instead of <c>NavState</c> directly, so it is safe to run on a Brain node.</para>
    /// </summary>
    public sealed class FollowRoadGraphExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ───────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FollowRoadGraphParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowRoadGraphParams*)src;

            // BS1-T019: write NavigationIntent instead of NavState.
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.RoadGraph;
            intent.TargetNodeId = p.TargetNodeId;
            intent.TargetSpeed  = p.Speed;
            world.SetComponent(entity, intent);

            channel.Status = Fbt.NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            // BS1-T019: poll NavigationStatus (set by NavigationExecutionSystem on the Muscle).
            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            // Ignore stale status reports for a different intent.
            if (status.IntentId != intent.IntentId)
                return;

            switch (status.Result)
            {
                case NavigationResult.Arrived:
                    channel.Status = Fbt.NodeStatus.Success;
                    break;

                case NavigationResult.FailedBlocked:
                case NavigationResult.FailedUnreachable:
                    channel.Status = Fbt.NodeStatus.Failure;
                    break;

                case NavigationResult.InProgress:
                default:
                    break;  // keep Running
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            // BS1-T019: cancel locomotion via NavigationIntent (DO NOT write NavState directly).
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.TargetSpeed = 0f;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
