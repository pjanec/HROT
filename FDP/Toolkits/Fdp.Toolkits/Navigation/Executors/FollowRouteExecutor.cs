using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
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
            intent.RouteEntityId = p.RouteEntityId;   // ⭐ CE-2171 — the route ENTITY crosses the wire; the local pool id does not
            intent.ReleaseHold = p.ReleaseHold != 0;  // ⭐ Air Ops slice 3 — the Brain's hold release (a level)
            world.SetComponent(entity, intent);

            channel.Status = Fbt.NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            // BS1-T020: poll NavigationStatus (set by NavigationExecutionSystem on the Muscle).
            var intent = world.GetComponent<NavigationIntent>(entity);
            var status = world.GetComponent<NavigationStatus>(entity);

            // ⭐ Air Ops slice 3 (§9b) — mirror the Brain's hold release onto the intent while the route runs (a LEVEL: the motion side
            //   consumes one rising edge per hold, so the bit must drop when the condition does). Only a change is written.
            {
                FollowRouteParams rp;
                fixed (byte* src = channel.Params)
                    rp = *(FollowRouteParams*)src;
                bool release = rp.ReleaseHold != 0;
                if (intent.ReleaseHold != release)
                {
                    intent.ReleaseHold = release;
                    world.SetComponent(entity, intent);
                }
            }

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
