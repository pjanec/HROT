using Hrot.SimHost.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Renders the IG presentation map canvas when
    /// <see cref="ActivePerspective.Current"/> equals <see cref="PerspectiveType.IG"/>.
    ///
    /// <para><b>Testability:</b>
    /// In production the system calls <see cref="MapCanvas.Draw"/>. In unit tests
    /// a <c>null</c> canvas can be provided; the system still increments
    /// <see cref="DrawCallCount"/> so tests can assert the condition check without
    /// triggering Raylib graphics calls.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(PerspectiveCoordinatorSystem))]
    public sealed class IgMapRenderSystem : ComponentSystem
    {
        private readonly MapCanvas? _canvas;

        /// <summary>Number of frames on which drawing was permitted (for unit-test assertions).</summary>
        public int DrawCallCount { get; private set; }

        /// <param name="canvas">
        ///   The <see cref="MapCanvas"/> to call <c>Draw()</c> on when the perspective is
        ///   <see cref="PerspectiveType.IG"/>. Pass <c>null</c> in headless/test contexts.
        /// </param>
        public IgMapRenderSystem(MapCanvas? canvas = null)
        {
            _canvas = canvas;
        }

        protected override void OnUpdate()
        {
            // Guard: only render when this is the active perspective.
            if (!World.HasSingleton<ActivePerspective>()) return;

            var perspective = World.GetSingletonUnmanaged<ActivePerspective>();
            if (perspective.Current != PerspectiveType.IG) return;

            DrawCallCount++;
            _canvas?.Draw();
        }
    }
}
