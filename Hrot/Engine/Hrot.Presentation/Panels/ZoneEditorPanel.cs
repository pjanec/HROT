using ImGuiNET;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Panel for static zone authoring: road network configuration and
/// LOS obstacle placement.
///
/// <para>Maintains zone name, road-network JSON path, and obstacle-radius state.
/// UI buttons call <see cref="IZoneAuthoringController"/> with the current values.</para>
///
/// <para><b>Testing:</b> the button-click handlers are exposed as
/// <c>internal</c> methods (<see cref="HandleApplyRoadNetwork"/> and
/// <see cref="HandlePlaceObstacle"/>) so tests can exercise the controller
/// dispatch without an ImGui render frame.</para>
/// </summary>
public sealed class ZoneEditorPanel
{
    // ── State ─────────────────────────────────────────────────────────────────

    private string _zoneName         = "urban_combat_zone";
    private string _roadNetworkPath  = "Assets/sample_road.json";
    private float  _obstacleRadius   = 5.0f;

    private const float ObstacleRadiusMin = 1.0f;
    private const float ObstacleRadiusMax = 50.0f;

    // ── Public state accessors (test helpers) ─────────────────────────────────

    /// <summary>Active zone name shown in the "Zone Name" input.</summary>
    public string ZoneName
    {
        get => _zoneName;
        set => _zoneName = value ?? string.Empty;
    }

    /// <summary>Road-network asset path shown in the "Road Network JSON" input.</summary>
    public string RoadNetworkPath
    {
        get => _roadNetworkPath;
        set => _roadNetworkPath = value ?? string.Empty;
    }

    /// <summary>Obstacle radius in metres (clamped to [1, 50] on set).</summary>
    public float ObstacleRadius
    {
        get => _obstacleRadius;
        set => _obstacleRadius = Math.Clamp(value, ObstacleRadiusMin, ObstacleRadiusMax);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the zone editor panel.  Must be called inside an active ImGui window.
    /// </summary>
    public void DrawContent(IZoneAuthoringController ctrl)
    {
        ImGui.InputText("Zone Name", ref _zoneName, 128);

        ImGui.Separator();

        ImGui.InputText("Road Network JSON", ref _roadNetworkPath, 256);
        if (ImGui.Button("Apply Road Network"))
            HandleApplyRoadNetwork(ctrl);

        ImGui.Separator();

        ImGui.SliderFloat("Obstacle Radius (m)", ref _obstacleRadius, ObstacleRadiusMin, ObstacleRadiusMax);
        if (ImGui.Button("Place LOS Obstacle"))
            HandlePlaceObstacle(ctrl);
    }

    // ── Internal logic (exposed for unit testing) ─────────────────────────────

    /// <summary>
    /// Handles the "Apply Road Network" button click.
    /// Calls <see cref="IZoneAuthoringController.SetRoadNetworkPath"/> with the
    /// current zone name and road-network path.
    /// </summary>
    internal void HandleApplyRoadNetwork(IZoneAuthoringController ctrl)
    {
        ctrl.SetRoadNetworkPath(_zoneName, _roadNetworkPath);
    }

    /// <summary>
    /// Handles the "Place LOS Obstacle" button click.
    /// Calls <see cref="IZoneAuthoringController.StartObstaclePlacementMode"/> with
    /// the current zone name and obstacle radius.
    /// </summary>
    internal void HandlePlaceObstacle(IZoneAuthoringController ctrl)
    {
        ctrl.StartObstaclePlacementMode(_zoneName, _obstacleRadius);
    }
}
