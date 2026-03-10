using Bagira.IOS.Panels;
using Moq;
using Newtonsoft.Json.Linq;

namespace Bagira.IOS.Tests;

/// <summary>
/// Unit tests for <see cref="ConfigPanel"/>.
///
/// Because ImGui cannot be driven in a test process, every test drives the
/// panel through the public <c>Handle*</c> methods and state-setter properties
/// rather than through <see cref="ConfigPanel.Draw"/>.
/// </summary>
public class ConfigPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ConfigPanel Panel, Mock<IIosLogic> Logic) CreateSut()
    {
        var logic = new Mock<IIosLogic>();
        return (new ConfigPanel(), logic);
    }

    // ── BuildPatch – JSON structure ───────────────────────────────────────────

    [Fact]
    public void BuildPatch_DefaultState_ContainsNavigationTool()
    {
        var (panel, _) = CreateSut();

        var json = JObject.Parse(panel.BuildPatch());

        Assert.Equal("Navigation", (string?)json["interaction"]?["activeTool"]);
    }

    [Fact]
    public void BuildPatch_ToolSelection_ReflectsChosenTool()
    {
        var (panel, _) = CreateSut();
        panel.SelectedTool = 2; // "Placement"

        var json = JObject.Parse(panel.BuildPatch());

        Assert.Equal("Placement", (string?)json["interaction"]?["activeTool"]);
    }

    [Fact]
    public void BuildPatch_EachTool_ProducesCorrectToolName()
    {
        var (panel, _) = CreateSut();
        for (int i = 0; i < ConfigPanel.Tools.Length; i++)
        {
            panel.SelectedTool = i;
            var json = JObject.Parse(panel.BuildPatch());
            Assert.Equal(ConfigPanel.Tools[i], (string?)json["interaction"]?["activeTool"]);
        }
    }

    [Fact]
    public void BuildPatch_SatelliteLayerOn_JsonTrue()
    {
        var (panel, _) = CreateSut();
        panel.SatelliteLayer = true;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.True((bool?)json["view"]?["layers"]?["satellite"]);
    }

    [Fact]
    public void BuildPatch_SatelliteLayerOff_JsonFalse()
    {
        var (panel, _) = CreateSut();
        panel.SatelliteLayer = false;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.False((bool?)json["view"]?["layers"]?["satellite"]);
    }

    [Fact]
    public void BuildPatch_TacticalGraphicsToggled_JsonReflectsState()
    {
        var (panel, _) = CreateSut();
        panel.TacticalGraphics = false;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.False((bool?)json["view"]?["layers"]?["tactical_graphics"]);
    }

    [Fact]
    public void BuildPatch_AirUnitsEnabled_JsonTrue()
    {
        var (panel, _) = CreateSut();
        panel.AirUnits = true;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.True((bool?)json["view"]?["layers"]?["units_air"]);
    }

    [Fact]
    public void BuildPatch_AirUnitsDisabled_JsonFalse()
    {
        var (panel, _) = CreateSut();
        panel.AirUnits = false;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.False((bool?)json["view"]?["layers"]?["units_air"]);
    }

    [Fact]
    public void BuildPatch_GroundUnitsToggled_JsonReflectsState()
    {
        var (panel, _) = CreateSut();
        panel.GroundUnits = false;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.False((bool?)json["view"]?["layers"]?["units_ground"]);
    }

    [Fact]
    public void BuildPatch_GroundUnitsEnabled_JsonTrue()
    {
        var (panel, _) = CreateSut();
        panel.GroundUnits = true;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.True((bool?)json["view"]?["layers"]?["units_ground"]);
    }

    [Fact]
    public void BuildPatch_VehiclesToggled_JsonReflectsState()
    {
        var (panel, _) = CreateSut();
        panel.Vehicles = false;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.False((bool?)json["view"]?["layers"]?["vehicles"]);
    }

    [Fact]
    public void BuildPatch_VehiclesEnabled_JsonTrue()
    {
        var (panel, _) = CreateSut();
        panel.Vehicles = true;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.True((bool?)json["view"]?["layers"]?["vehicles"]);
    }

    [Fact]
    public void BuildPatch_RoadGraphsToggled_JsonReflectsState()
    {
        var (panel, _) = CreateSut();
        panel.RoadGraphs = false;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.False((bool?)json["view"]?["layers"]?["road_graphs"]);
    }

    [Fact]
    public void BuildPatch_RoadGraphsEnabled_JsonTrue()
    {
        var (panel, _) = CreateSut();
        panel.RoadGraphs = true;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.True((bool?)json["view"]?["layers"]?["road_graphs"]);
    }

    [Fact]
    public void BuildPatch_GridEnabled_JsonTrue()
    {
        var (panel, _) = CreateSut();
        panel.Grid = true;

        var json = JObject.Parse(panel.BuildPatch());

        Assert.True((bool?)json["view"]?["layers"]?["grid"]);
    }

    [Fact]
    public void BuildPatch_IconScale_AppearsInViewObject()
    {
        var (panel, _) = CreateSut();
        panel.IconScale = 1.5f;

        var json   = JObject.Parse(panel.BuildPatch());
        double val = (double?)json["view"]?["iconScale"] ?? -1;

        Assert.True(Math.Abs(val - 1.5) < 0.001);
    }

    // ── State clamp guards ────────────────────────────────────────────────────

    [Fact]
    public void SelectedTool_BelowZero_ClampsToZero()
    {
        var (panel, _) = CreateSut();
        panel.SelectedTool = -5;
        Assert.Equal(0, panel.SelectedTool);
    }

    [Fact]
    public void SelectedTool_AboveMax_ClampsToLastIndex()
    {
        var (panel, _) = CreateSut();
        panel.SelectedTool = 999;
        Assert.Equal(ConfigPanel.Tools.Length - 1, panel.SelectedTool);
    }

    [Fact]
    public void IconScale_BelowMin_ClampsToMin()
    {
        var (panel, _) = CreateSut();
        panel.IconScale = 0.0f;
        Assert.Equal(PanelConstants.IconScaleMin, panel.IconScale);
    }

    [Fact]
    public void IconScale_AboveMax_ClampsToMax()
    {
        var (panel, _) = CreateSut();
        panel.IconScale = 99f;
        Assert.Equal(PanelConstants.IconScaleMax, panel.IconScale);
    }

    // ── HandleSendConfigPatch ─────────────────────────────────────────────────

    [Fact]
    public void HandleSendConfigPatch_CallsLogicSendConfigPatch()
    {
        var (panel, logic) = CreateSut();

        panel.HandleSendConfigPatch(logic.Object);

        logic.Verify(l => l.SendConfigPatch(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_PassesPatchMatchingCurrentState()
    {
        var (panel, logic) = CreateSut();
        panel.SelectedTool     = 1; // "Selection"
        panel.SatelliteLayer   = false;
        panel.TacticalGraphics = true;

        string? capturedPatch = null;
        logic.Setup(l => l.SendConfigPatch(It.IsAny<string>()))
             .Callback<string>(p => capturedPatch = p);

        panel.HandleSendConfigPatch(logic.Object);

        Assert.NotNull(capturedPatch);
        var json = JObject.Parse(capturedPatch!);
        Assert.Equal("Selection",  (string?)json["interaction"]?["activeTool"]);
        Assert.False((bool?)json["view"]?["layers"]?["satellite"]);
        Assert.True((bool?)json["view"]?["layers"]?["tactical_graphics"]);
    }

    [Fact]
    public void HandleSendConfigPatch_NullLogic_Throws()
    {
        var (panel, _) = CreateSut();
        Assert.Throws<ArgumentNullException>(() => panel.HandleSendConfigPatch(null!));
    }

    // ── Negative cases ────────────────────────────────────────────────────────

    [Fact]
    public void HandleSendConfigPatch_CalledTwice_SendsConfigPatchTwice()
    {
        var (panel, logic) = CreateSut();

        panel.HandleSendConfigPatch(logic.Object);
        panel.HandleSendConfigPatch(logic.Object);

        logic.Verify(l => l.SendConfigPatch(It.IsAny<string>()), Times.Exactly(2));
    }
}
