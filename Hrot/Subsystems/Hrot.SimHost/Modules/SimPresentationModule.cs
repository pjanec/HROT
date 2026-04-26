using Hrot.SimHost.Systems;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Runner;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// Formal presentation module for the Sim Map 2-D tactical overlay.
    ///
    /// <para>Registers <see cref="SimMapRenderSystem"/> into the
    /// <see cref="PresentationSystemGroup"/>. The render system gates its <c>Draw</c>
    /// call on the active perspective name being <c>"Sim"</c>.</para>
    ///
    /// <para>Implements <see cref="IMapCameraProvider"/> so that the perspective
    /// coordinator can snap cameras on perspective switch.</para>
    /// </summary>
    public sealed class SimPresentationModule : IMapCameraProvider
    {
        private readonly MapCanvas           _canvas;
        private readonly SimMapRenderSystem  _renderSystem;

        /// <summary>
        /// Wraps a pre-configured <see cref="MapCanvas"/> in the Sim presentation module.
        /// </summary>
        /// <param name="canvas">
        ///   The Sim Map canvas. Pass a canvas configured with a
        ///   <c>SimHostVehicleVisualizer</c> for production use. Pass <c>null</c>
        ///   only in headless/test contexts; doing so creates a default headless canvas.
        /// </param>
        public SimPresentationModule(MapCanvas? canvas = null)
        {
            _canvas       = canvas ?? new MapCanvas(input: null);
            _renderSystem = new SimMapRenderSystem(canvas); // null in headless/test contexts → no Raylib call
        }

        /// <summary>Returns the render system for test-time inspection.</summary>
        public SimMapRenderSystem RenderSystem => _renderSystem;

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _canvas?.Camera?.GetCameraView();

        public void ApplyCameraView(MapCameraView view) => _canvas?.Camera?.ApplyCameraView(view);

        /// <summary>
        /// Registers <see cref="SimMapRenderSystem"/> into the provided
        /// <see cref="ISystemRegistry"/>.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) =>
            registry.RegisterSystem(_renderSystem);
    }
}
