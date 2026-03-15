using System.Runtime.CompilerServices;
using CarKinem.Core;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;

// Disambiguate from FDP.Toolkit.Navigation.NavigationMode (CQRS contract) which
// inherits into this namespace via the parent namespace rule.
using CarKinemNavMode = CarKinem.Core.NavigationMode;

namespace FDP.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdFollowRoute"/> action.
    /// Sets <see cref="NavState.Mode"/> to <see cref="NavigationMode.CustomTrajectory"/> and
    /// assigns <see cref="NavState.TrajectoryId"/>. When the route completes:
    /// <list type="bullet">
    ///   <item>If <see cref="FollowRouteParams.IsLooped"/> is non-zero: re-arms the trajectory (stays Running).</item>
    ///   <item>Otherwise: reports <see cref="NodeStatus.Success"/>.</item>
    /// </list>
    /// </summary>
    public sealed class FollowRouteExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ───────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FollowRouteParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowRouteParams*)src;

            var nav = world.GetComponent<NavState>(entity);
            nav.Mode         = CarKinemNavMode.CustomTrajectory;
            nav.TrajectoryId = p.TrajectoryId;
            nav.ProgressS    = 0f;
            nav.HasArrived   = 0;
            world.SetComponent(entity, nav);

            channel.Status = Fbt.NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            var nav = world.GetComponent<NavState>(entity);

            if (nav.HasArrived == 0)
                return; // Still en route — no status change needed.

            FollowRouteParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowRouteParams*)src;

            if (p.IsLooped != 0)
            {
                // Loop: reset the trajectory so the vehicle starts again from the beginning.
                nav.ProgressS  = 0f;
                nav.HasArrived = 0;
                // Re-write the trajectory ID to signal the kinematics system that the route restarted.
                nav.TrajectoryId = p.TrajectoryId;
                world.SetComponent(entity, nav);
                // Status stays Running — the action is not finished.
            }
            else
            {
                channel.Status = Fbt.NodeStatus.Success;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var nav = world.GetComponent<NavState>(entity);
            nav.Mode = CarKinemNavMode.None;
            world.SetComponent(entity, nav);
        }
    }
}
