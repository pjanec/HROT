using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="ZoneEditorPanel"/>.
/// Tests call the <c>internal</c> handler methods directly to bypass ImGui.
/// </summary>
public class ZoneEditorPanelTests
{
    // ── Default state ─────────────────────────────────────────────────────────

    [Fact]
    public void DefaultZoneName_IsUrbanCombatZone()
    {
        var panel = new ZoneEditorPanel();
        Assert.Equal("urban_combat_zone", panel.ZoneName);
    }

    [Fact]
    public void DefaultRoadNetworkPath_IsSampleRoad()
    {
        var panel = new ZoneEditorPanel();
        Assert.Equal("Assets/sample_road.json", panel.RoadNetworkPath);
    }

    [Fact]
    public void DefaultObstacleRadius_IsFive()
    {
        var panel = new ZoneEditorPanel();
        Assert.Equal(5.0f, panel.ObstacleRadius);
    }

    // ── ObstacleRadius clamping ───────────────────────────────────────────────

    [Fact]
    public void ObstacleRadius_BelowMin_ClampsToOne()
    {
        var panel = new ZoneEditorPanel { ObstacleRadius = 0.0f };
        Assert.Equal(1.0f, panel.ObstacleRadius);
    }

    [Fact]
    public void ObstacleRadius_AboveMax_ClampsToFifty()
    {
        var panel = new ZoneEditorPanel { ObstacleRadius = 999.0f };
        Assert.Equal(50.0f, panel.ObstacleRadius);
    }

    // ── HandleApplyRoadNetwork ────────────────────────────────────────────────

    [Fact]
    public void HandleApplyRoadNetwork_CallsSetRoadNetworkPathWithCorrectArguments()
    {
        var panel = new ZoneEditorPanel
        {
            ZoneName        = "test_zone",
            RoadNetworkPath = "Assets/roads/main.json"
        };
        var ctrl = new Mock<IZoneAuthoringController>();

        panel.HandleApplyRoadNetwork(ctrl.Object);

        ctrl.Verify(c => c.SetRoadNetworkPath("test_zone", "Assets/roads/main.json"), Times.Once);
    }

    [Fact]
    public void HandleApplyRoadNetwork_UsesCurrentZoneNameAndPath()
    {
        var panel = new ZoneEditorPanel
        {
            ZoneName        = "harbour_zone",
            RoadNetworkPath = "Assets/harbour_roads.json"
        };
        var ctrl = new Mock<IZoneAuthoringController>();

        panel.HandleApplyRoadNetwork(ctrl.Object);

        ctrl.Verify(c => c.SetRoadNetworkPath("harbour_zone", "Assets/harbour_roads.json"), Times.Once);
    }

    // ── HandlePlaceObstacle ───────────────────────────────────────────────────

    [Fact]
    public void HandlePlaceObstacle_CallsStartObstaclePlacementModeWithCorrectRadius()
    {
        var panel = new ZoneEditorPanel
        {
            ZoneName        = "urban_combat_zone",
            ObstacleRadius  = 15.0f
        };
        var ctrl = new Mock<IZoneAuthoringController>();

        panel.HandlePlaceObstacle(ctrl.Object);

        ctrl.Verify(c => c.StartObstaclePlacementMode("urban_combat_zone", 15.0f), Times.Once);
    }

    [Fact]
    public void HandlePlaceObstacle_DefaultRadius_PassesFiveMetres()
    {
        var panel = new ZoneEditorPanel();
        var ctrl = new Mock<IZoneAuthoringController>();

        panel.HandlePlaceObstacle(ctrl.Object);

        ctrl.Verify(c => c.StartObstaclePlacementMode(It.IsAny<string>(), 5.0f), Times.Once);
    }
}
