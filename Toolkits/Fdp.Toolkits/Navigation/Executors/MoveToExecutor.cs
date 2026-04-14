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

            // ── Read current IntentId (default 0 if new) then increment ──────────────────────
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode             = NavigationMode.DirectPoint;
            intent.FinalDestination = p.Destination;   // raw Cartesian copy — no geo conversion
            intent.TargetSpeed      = p.Speed;
            intent.ArrivalRadius    = p.ArrivalRadius;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
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
            var status = world.GetComponent<NavigationStatus>(entity);

            // ── Stale-check: ignore status reports for a different intent ──────────────────────
            if (status.IntentId != intent.IntentId)
                return;   // keep Running; Muscle layer hasn't caught up yet

            // ── Map NavigationResult to channel status ─────────────────────────────────────────
            switch (status.Result)
            {
                case NavigationResult.Arrived:
                    channel.Status = NodeStatus.Success;
                    break;

                case NavigationResult.FailedBlocked:
                case NavigationResult.FailedUnreachable:
                    channel.Status = NodeStatus.Failure;
                    break;

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
            world.SetComponent(entity, intent);
        }
    }
}

