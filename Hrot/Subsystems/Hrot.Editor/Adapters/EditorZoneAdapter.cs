using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Hrot.Editor.Gizmos;
using Hrot.Map.Common.Events;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.Adapters
{
    /// <summary>
    /// Implements <see cref="IZoneAuthoringController"/> for the offline editor.
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="SetRoadNetworkPath"/> publishes an <see cref="UpdateZoneConfigCommand"/>
    ///     onto the local bus for the zone ingress system to consume.
    ///   </item>
    ///   <item>
    ///     <see cref="StartObstaclePlacementMode"/> pushes a <see cref="PlacementCanvasBridge"/>
    ///     (wrapping <see cref="ObstaclePlacementGizmo"/>) whose click callback publishes
    ///     a <see cref="SpawnZoneObstacleCommand"/>.
    ///   </item>
    /// </list>
    ///
    /// No DDS or CycloneDDS references.
    /// </summary>
    public sealed class EditorZoneAdapter : IZoneAuthoringController
    {
        private readonly MapCanvas   _canvas;
        private readonly FdpEventBus _bus;

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="bus">The local FDP event bus for publishing zone commands.</param>
        public EditorZoneAdapter(MapCanvas canvas, FdpEventBus bus)
        {
            _canvas = canvas;
            _bus    = bus;
        }

        /// <inheritdoc/>
        public void SetRoadNetworkPath(string activeZoneName, string assetPath)
        {
            _bus.PublishManaged(new UpdateZoneConfigCommand
            {
                ZoneName        = activeZoneName,
                RoadNetworkPath = assetPath,
            });
        }

        /// <inheritdoc/>
        public void StartObstaclePlacementMode(string activeZoneName, float radius)
        {
            var zoneName   = activeZoneName; // captured
            var zoneRadius = radius;         // captured

            PlacementCanvasBridge? bridge = null;
            var gizmo = new ObstaclePlacementGizmo(
                radius:           radius,
                onObstaclePlaced: worldPos =>
                {
                    _bus.PublishManaged(new SpawnZoneObstacleCommand
                    {
                        ZoneName = zoneName,
                        Position = new Vector2(worldPos.X, worldPos.Y),
                        Radius   = zoneRadius,
                    });
                },
                onRemove: () => bridge?.RequestPop());
            bridge = new PlacementCanvasBridge(gizmo);
            _canvas.PushTool(bridge);
        }
    }
}
