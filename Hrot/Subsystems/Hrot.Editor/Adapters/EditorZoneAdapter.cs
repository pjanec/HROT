using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
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
    ///     <see cref="StartObstaclePlacementMode"/> registers an <see cref="ObstaclePlacementGizmo"/>
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
        private readonly GlobalGizmoManager? _globalGizmoManager;

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="bus">The local FDP event bus for publishing zone commands.</param>
        /// <param name="globalGizmoManager">The global gizmo manager for placement gizmos.</param>
        public EditorZoneAdapter(MapCanvas canvas, FdpEventBus bus, GlobalGizmoManager? globalGizmoManager = null)
        {
            _canvas = canvas;
            _bus    = bus;
            _globalGizmoManager = globalGizmoManager;
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

            var id = GlobalGizmoManager.NewId();
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
                onRemove: () => _globalGizmoManager!.Unregister(id));
            _globalGizmoManager!.Register(id, gizmo);
        }
    }
}
