using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// Executor for <see cref="NavigationConstants.ActionIdFetchPathDetails"/>.
    /// When <see cref="FetchPathDetailsParams.NonBlocking"/> is non-zero, returns Success
    /// immediately (fire-and-forget fetch).  Otherwise blocks until the Brain-side path
    /// registry reports the handle as cached via <see cref="IPathRegistry.IsCached"/>.
    /// </summary>
    public sealed class FetchPathDetailsExecutor : IActionExecutor<LocomotionChannel>
    {
        private readonly IPathRegistry _pathRegistry;

        /// <param name="pathRegistry">Brain-side path cache polled in blocking mode.</param>
        public FetchPathDetailsExecutor(IPathRegistry pathRegistry)
        {
            _pathRegistry = pathRegistry;
        }

        // ── OnEnter ──────────────────────────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            FetchPathDetailsParams p;
            fixed (byte* src = channel.Params)
                p = *(FetchPathDetailsParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode        = NavigationMode.None;
            intent.RouteHandle = p.RouteHandle;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-reads <see cref="FetchPathDetailsParams"/> every tick to determine blocking mode.
        /// If <see cref="FetchPathDetailsParams.NonBlocking"/> is 1 → Success immediately.
        /// Otherwise polls <see cref="IPathRegistry.IsCached"/> for the active route handle.
        /// </summary>
        public unsafe void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
        {
            if (!world.IsAlive(entity))
                return;

            FetchPathDetailsParams p;
            fixed (byte* src = channel.Params)
                p = *(FetchPathDetailsParams*)src;

            if (p.NonBlocking != 0)
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            var intent = world.GetComponent<NavigationIntent>(entity);
            if (_pathRegistry.IsCached(intent.RouteHandle))
                channel.Status = NodeStatus.Success;
            // otherwise: keep Running
        }

        // ── OnExit ────────────────────────────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.Mode        = NavigationMode.None;
            intent.IntentId++;
            world.SetComponent(entity, intent);
        }
    }
}
