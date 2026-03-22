using Bagira.IOS.Panels;
using Moq;
using Newtonsoft.Json.Linq;
using System.Reflection;

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

    // BUG2-U001: interaction key removed; verify it is absent
    [Fact]
    public void BuildPatch_DoesNotContainInteractionKey()
    {
        var (panel, _) = CreateSut();

        var json = JObject.Parse(panel.BuildPatch());

        Assert.Null(json["interaction"]);
    }

    // BUG2-U001: Tools static array removed; verify via reflection
    [Fact]
    public void NoToolsField()
    {
        var field = typeof(ConfigPanel)
            .GetField("Tools", BindingFlags.Public | BindingFlags.Static);

        Assert.Null(field);
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
        panel.SatelliteLayer   = false;
        panel.TacticalGraphics = true;

        string? capturedPatch = null;
        logic.Setup(l => l.SendConfigPatch(It.IsAny<string>()))
             .Callback<string>(p => capturedPatch = p);

        panel.HandleSendConfigPatch(logic.Object);

        Assert.NotNull(capturedPatch);
        var json = JObject.Parse(capturedPatch!);
        Assert.Null(json["interaction"]);
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

    // ── OC1-B002: Routes layer default state and JSON key ────────────────────

    /// <summary>
    /// OC1-B002: The routes layer (<c>road_graphs</c> JSON key) must be enabled by
    /// default so newly authored routes are visible without any operator configuration.
    /// </summary>
    [Fact]
    public void RoadGraphs_DefaultValue_IsTrue()
    {
        var (panel, _) = CreateSut();

        Assert.True(panel.RoadGraphs);
    }

    /// <summary>
    /// OC1-B002: The JSON key emitted for the routes toggle must remain <c>"road_graphs"</c>
    /// so it continues to match <c>MapLayerRegistry</c> and the IG layer-processing path.
    /// The display label is "Routes" but the wire key is unchanged.
    /// </summary>
    [Fact]
    public void BuildPatch_RoutesLayerKey_IsRoadGraphs()
    {
        var (panel, _) = CreateSut();

        var json = JObject.Parse(panel.BuildPatch());

        // Key must exist and must not be null.
        Assert.NotNull(json["view"]?["layers"]?["road_graphs"]);
    }
}

