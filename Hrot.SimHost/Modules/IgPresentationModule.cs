using Hrot.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// Formal presentation module for the Image Generator (IG) 3-D window.
    ///
    /// <para>Registers <see cref="IgMapRenderSystem"/> into the
    /// <see cref="PresentationSystemGroup"/>. The render system gates its <c>Draw</c>
    /// call on <see cref="Components.ActivePerspective.Current"/> ==
    /// <see cref="Components.PerspectiveType.IG"/>.</para>
    ///
    /// <para>Implements <see cref="IMapCameraProvider"/> so that
    /// <see cref="PerspectiveCoordinatorSystem"/> can snap cameras on perspective switch.</para>
    /// </summary>
    public sealed class IgPresentationModule : IMapCameraProvider
    {
        private readonly MapCanvas          _canvas;
        private readonly IgMapRenderSystem  _renderSystem;

        /// <summary>
        /// Wraps a pre-configured <see cref="MapCanvas"/> in the IG presentation module.
        /// </summary>
        /// <param name="canvas">
        ///   The IG map canvas. Pass a canvas configured with a <c>NedVisualizerAdapter</c>
        ///   for production use. Pass <c>null</c> only in headless/test contexts; doing so
        ///   creates a default headless canvas.
        /// </param>
        public IgPresentationModule(MapCanvas? canvas = null)
        {
            _canvas       = canvas ?? new MapCanvas(input: null);
            _renderSystem = new IgMapRenderSystem(canvas); // null in headless/test contexts → no Raylib call
        }

        /// <summary>Returns the render system for test-time inspection.</summary>
        public IgMapRenderSystem RenderSystem => _renderSystem;

        /// <inheritdoc/>
        public MapCamera GetCamera() => _canvas.Camera;

        /// <summary>
        /// Registers <see cref="IgMapRenderSystem"/> into the provided
        /// <see cref="PresentationSystemGroup"/> group.
        /// </summary>
        public void RegisterSystems(SystemGroup group) =>
            group.AddSystem(_renderSystem);
    }
}
