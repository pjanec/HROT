using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.Editor.Tools;
using Hrot.UI.Common.Facades;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="ISpawnController"/> for the offline editor.
    /// Translates spawn requests into <see cref="MapCanvas"/> tool activations:
    /// <list type="bullet">
    ///   <item>Entity placement → <see cref="CreationTool"/> pushed onto the canvas.</item>
    ///   <item>Area authoring → <see cref="AreaPlacementTool"/> stub pushed onto the canvas.</item>
    ///   <item>Route authoring → <see cref="RoutePlacementTool"/> stub pushed onto the canvas.</item>
    /// </list>
    /// No DDS or CycloneDDS references; all dispatch is done through the in-process
    /// <see cref="FdpEventBus"/>.
    /// </summary>
    public sealed class EditorSpawnAdapter : ISpawnController
    {
        private readonly MapCanvas    _canvas;
        private readonly FdpEventBus  _bus;

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="bus">The local FDP event bus used to route spawn commands.</param>
        public EditorSpawnAdapter(MapCanvas canvas, FdpEventBus bus)
        {
            _canvas = canvas;
            _bus    = bus;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Creates a <see cref="CreationTool"/> whose delegate publishes the constructed
        /// <see cref="SpawnEntityCommand"/> onto the local bus, then pushes the tool
        /// onto <see cref="_canvas"/> for single-placement mode.
        /// </remarks>
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
        {
            var tool = new CreationTool(
                onEntityCreated:      cmd => _bus.PublishManaged(cmd),
                tkbType:              tkbType,
                initialPropertiesJson: initialPropertiesJson,
                autoPopOnPlace:       true);

            _canvas.PushTool(tool);
        }

        /// <inheritdoc/>
        public void StartAreaAuthoringMode(string styleOverrideJson = "")
        {
            _canvas.PushTool(new AreaPlacementTool(styleOverrideJson));
        }

        /// <inheritdoc/>
        public void StartRouteAuthoringMode()
        {
            _canvas.PushTool(new RoutePlacementTool());
        }
    }
}
