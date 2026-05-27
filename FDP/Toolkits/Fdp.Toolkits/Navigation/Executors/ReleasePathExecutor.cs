using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdReleasePath"/>.
    /// ReleasePath is a fire-and-forget command: the actual trajectory pool cleanup is
    /// performed by <see cref="Systems.NavigationIntentBridgeSystem"/> reading the
    /// <see cref="LocomotionChannel"/>.  The executor's job is to record the release
    /// intent and immediately signal Success so the BTree can continue.
    /// </summary>
    public sealed class ReleasePathExecutor : IActionExecutor<LocomotionChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the release intent and sets Success immediately.
        /// </summary>
        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            ReleasePathParams p;
            fixed (byte* src = channel.Params)
                p = *(ReleasePathParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.None;
            intent.RouteHandle = p.RouteHandle;
            world.SetComponent(entity, intent);

            // Fire-and-forget: release is always considered immediately successful.
            channel.Status = NodeStatus.Success;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ReleasePath has no polling logic; the node should never linger in Execute
        /// because OnEnter sets Success.  Defensive pass-through in case the BTree
        /// calls Execute anyway.
        /// </summary>
        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            channel.Status = NodeStatus.Success;
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode     = NavigationMode.None;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
