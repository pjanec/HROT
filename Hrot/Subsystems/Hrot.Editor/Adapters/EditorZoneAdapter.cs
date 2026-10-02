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

        /// <summary>⭐ <c>UXI-07</c> step 4a — the host's ONE arbiter. See the constructor parameter.</summary>
        private readonly Hrot.ScenarioEditor.Tools.ToolController? _tools;

        // ⚠⚠ The per-invocation parameters, held between Activate() and the arm body.
        //    📐 The arm runs through ToolActivation, whose only argument is an Entity — so the two values
        //    StartObstaclePlacementMode receives have to reach the body some other way. ⭐ This is the same
        //    shape the Spawn tool already uses: its arm calls startPlacementMode(), and the ADAPTER holds
        //    "which type is being placed". ⛔ Widening ToolActivation to carry arbitrary payloads was
        //    rejected — it would make every tool pay for one tool's parameters.
        private string _pendingZoneName = string.Empty;
        private float  _pendingRadius;

        /// <param name="canvas">The map canvas that hosts the tool stack.</param>
        /// <param name="bus">The local FDP event bus for publishing zone commands.</param>
        /// <param name="globalGizmoManager">The global gizmo manager for placement gizmos.</param>
        /// <param name="tools">
        /// ⭐⭐⭐ <c>UXI-07</c> step 4a — the host's ONE tool arbiter (<c>MapInteraction.Tools</c>).
        /// 📐 §4.8's inventory: obstacle placement arms an <c>ObstaclePlacementGizmo</c>, which declares
        /// <c>RequiresExclusiveFocus</c> — so before this it could take focus while a tool still believed
        /// it held it. ⛔ Optional so existing callers compile; ⚠ a host that HAS one must pass it, and
        /// <c>TheViewportInteractionIsSharedTests</c> rails that the production roots do.
        /// </param>
        public EditorZoneAdapter(
            MapCanvas canvas,
            FdpEventBus bus,
            GlobalGizmoManager? globalGizmoManager = null,
            Hrot.ScenarioEditor.Tools.ToolController? tools = null)
        {
            _canvas = canvas;
            _bus    = bus;
            _globalGizmoManager = globalGizmoManager;
            _tools  = tools;

            // ⭐ Registered ONCE, here — ⛔ not per activation, or the duplicate-id guard would throw on
            //   the second placement (and that guard is the G4 lesson, worth keeping strict).
            _tools?.Register(
                new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                    Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceObstacle,
                    "Place Obstacle",
                    Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                    Hrot.ScenarioEditor.Tools.ToolArbiter.Global),
                _ => ArmObstaclePlacement());
        }

        /// <summary>
        /// ⭐ The arm body, unchanged in behaviour — reached either through
        /// <c>ToolController.Activate</c> (production) or directly when no arbiter was wired.
        /// </summary>
        private Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmObstaclePlacement()
        {
            if (_globalGizmoManager == null)
            {
                Hrot.ScenarioEditor.Tools.ToolReport.Unserviceable(
                    null, "Place Obstacle", "this host composes no global gizmo manager");
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;
            }

            var zoneName   = _pendingZoneName; // captured
            var zoneRadius = _pendingRadius;   // captured

            var id = GlobalGizmoManager.NewId();
            var gizmo = new ObstaclePlacementGizmo(
                radius:           zoneRadius,
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
            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
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
            _pendingZoneName = activeZoneName;
            _pendingRadius   = radius;

            // ⭐⭐⭐ UXI-07 step 4a — ACTIVATE through the arbiter instead of arming it directly.
            //   🔴 This used to call _globalGizmoManager.Register straight, which is §4.8's bypass: the
            //   gizmo took focus while ToolController still believed a tool held it.
            if (_tools != null) { _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceObstacle); return; }

            // ⚠ No arbiter wired ⇒ arm anyway and SAY SO. 🔒 R-137: refusing here would cost a capability
            //   on a host that simply has not been converted. ⛔ But silence would hide the bypass, which
            //   is the thing this step exists to remove.
            Hrot.ScenarioEditor.Tools.ToolReport.Say(null,
                "obstacle placement armed WITHOUT an arbiter — EditorZoneAdapter was constructed with no "
              + "ToolController, so this modal cannot displace another (UXI-07 step 4a).");
            ArmObstaclePlacement();
        }
    }
}
