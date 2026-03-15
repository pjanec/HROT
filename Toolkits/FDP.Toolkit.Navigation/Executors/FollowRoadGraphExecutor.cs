using System.Runtime.CompilerServices;
using CarKinem.Core;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;

namespace FDP.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdFollowRoadGraph"/> action.
    /// Configures <see cref="NavState"/> for road-graph navigation and reports
    /// <see cref="NodeStatus.Success"/> when <see cref="NavState.HasArrived"/> is set.
    /// </summary>
    public sealed class FollowRoadGraphExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ───────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FollowRoadGraphParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowRoadGraphParams*)src;

            var nav = world.GetComponent<NavState>(entity);
            nav.Mode           = KinematicsMode.RoadGraph;
            nav.RoadPhase      = RoadGraphPhase.Approaching;
            // Store the caller-supplied target node ID as the initial segment reference.
            // The road-graph navigator (CarKinematicsSystem) will resolve the actual road segments
            // from this starting point via CurrentSegmentId during its Approaching phase.
            nav.CurrentSegmentId = p.TargetNodeId;
            nav.TargetSpeed    = p.Speed;
            nav.HasArrived     = 0;
            world.SetComponent(entity, nav);

            channel.Status = Fbt.NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            var nav = world.GetComponent<NavState>(entity);
            if (nav.HasArrived != 0)
                channel.Status = Fbt.NodeStatus.Success;
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var nav = world.GetComponent<NavState>(entity);
            nav.TargetSpeed = 0f;
            nav.Mode        = KinematicsMode.None;
            world.SetComponent(entity, nav);
        }
    }
}
