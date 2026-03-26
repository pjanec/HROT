using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;

namespace FDP.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdFollowRoute"/> action.
    /// Issues a <see cref="NavigationMode.FollowRoute"/> <see cref="NavigationIntent"/> and reports
    /// <see cref="NodeStatus.Success"/> when <see cref="NavigationStatus.Result"/> is
    /// <see cref="NavigationResult.Arrived"/>.
    /// <list type="bullet">
    ///   <item>If <see cref="FollowRouteParams.IsLooped"/> is non-zero: re-arms the intent with an
    ///     incremented <c>IntentId</c> to signal the Muscle's bridge system to restart the route
    ///     (stays Running).</item>
    ///   <item>Otherwise: reports <see cref="NodeStatus.Success"/>.</item>
    /// </list>
    ///
    /// <para><b>CQRS compliance (BS1-T020):</b> this executor writes <see cref="NavigationIntent"/>
    /// instead of <c>NavState</c> directly, so it is safe to run on a Brain node.</para>
    /// </summary>
    public sealed class FollowRouteExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ───────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FollowRouteParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowRouteParams*)src;

            // BS1-T020: write NavigationIntent instead of NavState.
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode         = NavigationMode.FollowRoute;
            intent.TrajectoryId = p.TrajectoryId;
            world.SetComponent(entity, intent);

            channel.Status = Fbt.NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            // BS1-T020: poll NavigationStatus (set by NavigationExecutionSystem on the Muscle).
            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            // Ignore stale status reports for a different intent.
            if (status.IntentId != intent.IntentId)
                return;

            if (status.Result != NavigationResult.Arrived)
                return;  // still en route — no status change needed

            FollowRouteParams p;
            fixed (byte* src = channel.Params)
                p = *(FollowRouteParams*)src;

            if (p.IsLooped != 0)
            {
                // Loop: increment IntentId to signal the bridge system to restart the route
                // from ProgressS=0 (DO NOT write NavState.ProgressS directly — BS1-T020).
                //
                // Round-trip latency assumption (TD-12): after incrementing IntentId here,
                // the stale Arrived status is no longer visible on this same tick because
                // Execute() already returned early above.  On the NEXT tick NavigationExecutionSystem
                // (Muscle side) detects the new IntentId, resets NavigationStatus to InProgress,
                // and begins evaluating arrival against the restarted route.  This means there
                // is a minimum one-simulation-tick gap before FollowRouteExecutor can observe
                // the Arrived status for the new lap.  The gap is acceptable and bounded: it
                // equals exactly one tick of NavigationExecutionSystem latency.
                intent.IntentId++;
                world.SetComponent(entity, intent);
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
            // BS1-T020: cancel locomotion via NavigationIntent (DO NOT write NavState directly).
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode     = NavigationMode.None;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
