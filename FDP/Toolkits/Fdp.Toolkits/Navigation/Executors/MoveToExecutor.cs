using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for the <see cref="NavigationConstants.ActionIdMoveTo"/> action.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a pure CQRS <em>command writer</em>: it has no physics awareness.
    /// The full lifecycle is:
    /// </para>
    /// <list type="number">
    ///   <item><see cref="OnEnter"/> — copies <see cref="MoveToParams"/> into
    ///     <see cref="NavigationIntent"/> (raw Cartesian copy, no geo conversion) and
    ///     increments <see cref="NavigationIntent.IntentId"/>.</item>
    ///   <item><see cref="Execute"/> — observes <see cref="NavigationStatus"/> written
    ///     by the Muscle layer (<c>NavigationExecutionSystem</c>).  Returns Success,
    ///     Failure, or keeps Running based on <see cref="NavigationStatus.Result"/>.</item>
    ///   <item><see cref="OnExit"/> — clears the <see cref="NavigationIntent.Mode"/> so
    ///     the Muscle layer stops executing the command.</item>
    /// </list>
    /// <para>
    /// <b>No geo conversion:</b> <see cref="MoveToParams.Destination"/> is a Cartesian
    /// <c>Vector2</c> and is written directly into
    /// <see cref="NavigationIntent.FinalDestination"/> without any coordinate transform.
    /// Conversion to WGS-84 is the responsibility of the egress translator, not the executor.
    /// </para>
    /// </remarks>
    public sealed class MoveToExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes a new <see cref="NavigationIntent"/> from <see cref="MoveToParams"/> and sets
        /// the channel to Running.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            MoveToParams p;
            fixed (byte* src = channel.Params)
                p = *(MoveToParams*)src;
            WriteIntent(world, entity, in p);
            channel.Status = NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ The ONE write of a MoveTo's <see cref="NavigationIntent"/> from its params (a new <c>IntentId</c>, so the muscle re-plans).
        /// <see cref="OnEnter"/> uses it, and so does behaviors' under-fire reflex (AQ96) when it re-paces the move, sends him a side step or
        /// a crawl, or resumes the move — so the intent is never built twice. Returns the new intent id.
        /// </summary>
        public static uint WriteIntent(EntityRepository world, Entity entity, in MoveToParams p)
        {
            // ── Read current IntentId (default 0 if new) then increment ──────────────────────
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode             = NavigationMode.PathToPoint;   // CE-3026 — a MoveTo FINDS A PATH (the vehicle side plans)
            // ⭐ AQ93 R-4023 — "in traffic": SUMO drives it on its own roads; the result (Arrived) comes back the same way.
            if (NavigationConstants.RoadUseOf(p.Flags) == RoadUse.InTraffic) intent.Mode = NavigationMode.Traffic;
            intent.FinalDestination = p.Destination;   // raw Cartesian copy — no geo conversion
            intent.RouteEntityId    = 0;   // ⭐ Q94 C — a plain point: never a door (or route) left from an earlier move
            intent.TargetSpeed      = p.Speed;
            intent.ArrivalRadius    = p.ArrivalRadius;
            intent.ReverseAllowed   = p.ReverseAllowed;
            intent.Flags            = p.Flags;
            intent.MaxReplans       = p.MaxReplans;
            intent.RouteHandle      = p.RouteHandle;
            intent.LayerMask        = p.LayerMask;      // CE-3026 — the vehicle side plans on what the Brain asked for
            intent.BackendForce     = p.BackendForce;
            intent.ReplanTimeBudget = 0f;  // no time limit by default; set post-enter if needed
            world.SetComponent(entity, intent);
            return intent.IntentId;
        }

        /// <summary>⭐ AQ96 C2 — stops the body where it stands without ending the move: the intent goes to <c>Mode None</c> (a new id).
        /// The caller marks the id as a hold (<see cref="UnderFireMove.HoldIntentId"/>), so <see cref="Execute"/> keeps the move Running.</summary>
        public static uint Halt(EntityRepository world, Entity entity)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.TargetSpeed = 0f;
            intent.IntentId++;
            world.SetComponent(entity, intent);
            return intent.IntentId;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Observes <see cref="NavigationStatus"/> written by the Muscle layer.
        /// Ignores stale status (when <c>status.IntentId != intent.IntentId</c>).
        /// </summary>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            var intent = world.GetComponent<NavigationIntent>(entity);

            // ⭐ AQ96 C2 — an intent the under-fire reflex wrote (a halt, a side step, a crawl to cover) is not this move: its result never
            //   ends it. The play keeps Running while he is down; the reflex resumes the move with a fresh intent of its own.
            if (world.IsComponentTypeRegistered<UnderFireMove>() && world.HasComponent<UnderFireMove>(entity))
            {
                uint hold = world.GetComponentRO<UnderFireMove>(entity).HoldIntentId;
                if (hold != 0 && hold == intent.IntentId) return;
            }

            // ── CE-229: NO NavigationStatus YET (or ever) is the SAME situation as a stale one ──
            //
            // NavigationStatus is written by the MUSCLE layer. This executor runs on the BRAIN, so on
            // an entity with no muscle tier the component is simply never there — measured on a platoon
            // commander, which carries NavigationIntent but not NavigationStatus. GetComponent threw,
            // the exception escaped ModuleHostKernel.UpdateInternal, and the whole HOST died on the
            // frame after a mission task named MoveToLocation for that entity.
            //
            // ⭐ Tolerating it is not a new policy — it is the policy two lines below. The stale-check
            //   already returns "keep Running; Muscle layer hasn't caught up yet" for a status that
            //   exists but reports a different intent; a status that does not exist yet is that same
            //   condition one step earlier. HillAttackCommanderNodes.cs:155 already treats a missing
            //   NavigationStatus exactly this way (trace + NodeStatus.Running), and the generator proof
            //   test notes the same shape. This executor was the outlier that threw.
            //
            // ⛔ It does NOT silently succeed: the channel stays Running, so a behaviour waiting on
            //   arrival keeps waiting rather than being told it arrived.
            if (!world.HasComponent<NavigationStatus>(entity))
                return;   // keep Running; no muscle layer has reported on this entity

            var status = world.GetComponent<NavigationStatus>(entity);

            // ── Stale-check: ignore status reports for a different intent ──────────────────────
            if (status.IntentId != intent.IntentId)
                return;   // keep Running; Muscle layer hasn't caught up yet

            // ── Map NavigationResult to channel status ─────────────────────────────────────────
            switch (status.Result)
            {
                case NavigationResult.Arrived:
                    channel.Status = NodeStatus.Success;
                    world.Bus.Publish(new MoveCompletedEvent
                    {
                        Target      = entity,
                        Reason      = NavigationResult.Arrived,
                        RouteHandle = status.RouteHandle,
                    });
                    break;

                case NavigationResult.FailedBlocked:
                case NavigationResult.FailedUnreachable:
                case NavigationResult.NoPath:
                case NavigationResult.FailedNoLayer:
                case NavigationResult.FailedInvalidHandle:
                    channel.Status = NodeStatus.Failure;
                    world.Bus.Publish(new MoveCompletedEvent
                    {
                        Target      = entity,
                        Reason      = status.Result,
                        RouteHandle = status.RouteHandle,
                    });
                    break;

                case NavigationResult.PathFound:
                case NavigationResult.InProgress:
                default:
                    // Keep Running — nothing to do.
                    break;
            }
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Clears the <see cref="NavigationIntent.Mode"/> so the Muscle layer stops
        /// processing this command.
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

