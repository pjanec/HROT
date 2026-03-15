using Bagira.SimHost.Components;
using Bagira.SimHost.Events;
using Bagira.SimHost.Modules;
using Fdp.Kernel;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Runs first in the presentation phase and handles dynamic perspective switching.
    ///
    /// <para>On each frame, consumes any pending <see cref="TogglePerspectiveEvent"/>
    /// events. On a toggle event:
    /// <list type="number">
    ///   <item>Flips <see cref="ActivePerspective.Current"/> between
    ///     <see cref="PerspectiveType.IG"/> and <see cref="PerspectiveType.Sim"/>.</item>
    ///   <item>Snaps the incoming camera to the outgoing camera's state so that the
    ///     new view starts at the same world position and zoom.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <see cref="IgMapRenderSystem"/> and <see cref="SimMapRenderSystem"/> are ordered
    /// <c>[UpdateAfter(typeof(PerspectiveCoordinatorSystem))]</c> so they see the updated
    /// <see cref="ActivePerspective.Current"/> in the same frame as the toggle.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public sealed class PerspectiveCoordinatorSystem : ComponentSystem
    {
        private readonly IMapCameraProvider? _igCameraProvider;
        private readonly IMapCameraProvider? _simCameraProvider;

        /// <summary>
        /// Creates the coordinator.
        /// </summary>
        /// <param name="igCameraProvider">
        ///   Camera provider for the IG presentation module (may be <c>null</c> in headless contexts).
        /// </param>
        /// <param name="simCameraProvider">
        ///   Camera provider for the Sim Map presentation module (may be <c>null</c> in headless contexts).
        /// </param>
        public PerspectiveCoordinatorSystem(
            IMapCameraProvider? igCameraProvider  = null,
            IMapCameraProvider? simCameraProvider = null)
        {
            _igCameraProvider  = igCameraProvider;
            _simCameraProvider = simCameraProvider;
        }

        protected override void OnUpdate()
        {
            var toggles = World.Bus.Consume<TogglePerspectiveEvent>();
            if (toggles.Length == 0) return;

            // Only the last toggle event matters if multiple were queued.
            // We flip the current perspective and snap cameras.
            if (!World.HasSingleton<ActivePerspective>()) return;

            ref var perspective = ref World.GetSingletonUnmanaged<ActivePerspective>();

            // Determine outgoing and incoming providers.
            bool wasIG = perspective.Current == PerspectiveType.IG;
            var outgoing = wasIG ? _igCameraProvider  : _simCameraProvider;
            var incoming = wasIG ? _simCameraProvider : _igCameraProvider;

            // Flip the active perspective.
            perspective.Current = wasIG ? PerspectiveType.Sim : PerspectiveType.IG;

            // Snap the incoming camera to the outgoing camera's position/zoom.
            if (incoming != null && outgoing != null)
            {
                incoming.GetCamera().SnapTo(outgoing.GetCamera());
            }
        }
    }
}
