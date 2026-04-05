using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Moq;

namespace Hrot.ExCon.Tests;

/// <summary>
/// Unit tests for <see cref="ConfigPanel"/> (now in <c>Hrot.UI.Common.Panels</c>).
/// Tests drive the panel via <c>Handle*</c> methods and state-setter properties
/// without requiring an active ImGui render frame.
/// </summary>
public class ConfigPanelTests
{
    private static (ConfigPanel Panel, Mock<IMapConfigController> Ctrl) CreateSut()
    {
        var ctrl = new Mock<IMapConfigController>();
        return (new ConfigPanel(), ctrl);
    }

    // ── Default state ─────────────────────────────────────────────────────────

    [Fact] public void SatelliteLayer_DefaultIsTrue()   { var (p, _) = CreateSut(); Assert.True(p.SatelliteLayer); }
    [Fact] public void GroundUnits_DefaultIsTrue()       { var (p, _) = CreateSut(); Assert.True(p.GroundUnits); }
    [Fact] public void AirUnits_DefaultIsTrue()          { var (p, _) = CreateSut(); Assert.True(p.AirUnits); }
    [Fact] public void Grid_DefaultIsFalse()             { var (p, _) = CreateSut(); Assert.False(p.Grid); }

    [Fact]
    public void RoadGraphs_DefaultValue_IsTrue()
    {
        var (panel, _) = CreateSut();
        Assert.True(panel.RoadGraphs);
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

    // ── HandleSendConfigPatch – calls ApplyConfig ─────────────────────────────

    [Fact]
    public void HandleSendConfigPatch_CallsApplyConfig()
    {
        var (panel, ctrl) = CreateSut();
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.IsAny<MapLayerState>()), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_SatelliteLayerOff_AppliesWithSatelliteFalse()
    {
        var (panel, ctrl) = CreateSut();
        panel.SatelliteLayer = false;
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.Is<MapLayerState>(s => s.Satellite == false)), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_SatelliteLayerOn_AppliesWithSatelliteTrue()
    {
        var (panel, ctrl) = CreateSut();
        panel.SatelliteLayer = true;
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.Is<MapLayerState>(s => s.Satellite == true)), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_GroundUnitsFalse_AppliesWithGroundUnitsFalse()
    {
        var (panel, ctrl) = CreateSut();
        panel.GroundUnits = false;
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.Is<MapLayerState>(s => s.GroundUnits == false)), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_AirUnitsOff_AppliesWithAirUnitsFalse()
    {
        var (panel, ctrl) = CreateSut();
        panel.AirUnits = false;
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.Is<MapLayerState>(s => s.AirUnits == false)), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_GridOn_AppliesWithGridTrue()
    {
        var (panel, ctrl) = CreateSut();
        panel.Grid = true;
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.Is<MapLayerState>(s => s.Grid == true)), Times.Once);
    }

    [Fact]
    public void HandleSendConfigPatch_AllLayersExplicit_AppliesCorrectState()
    {
        var (panel, ctrl) = CreateSut();
        panel.SatelliteLayer = false;
        panel.GroundUnits    = true;
        panel.AirUnits       = false;
        panel.Grid           = true;

        MapLayerState? captured = null;
        ctrl.Setup(c => c.ApplyConfig(It.IsAny<MapLayerState>()))
            .Callback<MapLayerState>(s => captured = s);

        panel.HandleSendConfigPatch(ctrl.Object);

        Assert.NotNull(captured);
        Assert.False(captured!.Satellite);
        Assert.True(captured.GroundUnits);
        Assert.False(captured.AirUnits);
        Assert.True(captured.Grid);
    }

    [Fact]
    public void HandleSendConfigPatch_NullCtrl_Throws()
    {
        var (panel, _) = CreateSut();
        Assert.Throws<ArgumentNullException>(() => panel.HandleSendConfigPatch(null!));
    }

    [Fact]
    public void HandleSendConfigPatch_CalledTwice_CallsApplyConfigTwice()
    {
        var (panel, ctrl) = CreateSut();
        panel.HandleSendConfigPatch(ctrl.Object);
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.IsAny<MapLayerState>()), Times.Exactly(2));
    }

    // ── Vehicles / TacticalGraphics / RoadGraphs remain as panel-only state ──

    [Fact]
    public void HandleSendConfigPatch_DoesNotExposeVehiclesViaController()
    {
        var (panel, ctrl) = CreateSut();
        panel.Vehicles = false;
        panel.HandleSendConfigPatch(ctrl.Object);
        ctrl.Verify(c => c.ApplyConfig(It.IsAny<MapLayerState>()), Times.Once);
    }

    // ── LoadConfig syncs panel state ──────────────────────────────────────────

    [Fact]
    public void LoadConfig_SatelliteFalse_UpdatesPanelState()
    {
        var (panel, ctrl) = CreateSut();
        ctrl.Setup(c => c.GetCurrentConfig())
            .Returns(new MapLayerState(false, true, true, false));

        panel.LoadConfig(ctrl.Object);

        Assert.False(panel.SatelliteLayer);
    }

    [Fact]
    public void LoadConfig_NullCtrl_Throws()
    {
        var (panel, _) = CreateSut();
        Assert.Throws<ArgumentNullException>(() => panel.LoadConfig(null!));
    }
}

