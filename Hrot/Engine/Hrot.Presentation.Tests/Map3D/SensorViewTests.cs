using System.Text.Json;
using Fdp.Toolkit.Input;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.UI.Common.Map3D;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-1989</c> (📄 docs/designs/sensor-view/DESIGN_Sensor_View.md §10, §11 KEY) — the map's sensor-view key, headless: F4 is a keymap row (Global, qualifier
/// <c>map-view</c>) the sensor view reads; three presses return to normal (night vision → thermal once it is offered); the View ▸ Sensor View items show the
/// key as their shortcut, and the menu JSON parses.
/// </summary>
public sealed class SensorViewTests
{
    private static (SensorView View, ManualChordInput Keys) Make()
    {
        var keys = new ManualChordInput();
        var view = new SensorView(new MapViewSwitch(new MapCanvas(input: null), new MapCamera()), bus: null, keys, new Keymap());
        return (view, keys);
    }

    /// <summary>⭐ KEY — F4 cycles normal → night vision → normal while thermal is not offered (CE-1985), one step per press, nothing without one.
    /// 🔴 Red-proof: the <c>Pressed(SensorViewKeys.Next)</c> line removed from <c>SensorView.Update</c> ⇒ the mode stays None.</summary>
    [Fact]
    public void CE1989_F4_CyclesTheView_AndAFrameWithoutAPressDoesNothing()
    {
        var (view, keys) = Make();
        view.Update(0.016f);
        Assert.Equal(SceneSensor.None, view.Mode);
        keys.Press(EditorKey.F4); view.Update(0.016f); keys.NextFrame();
        Assert.Equal(SceneSensor.NightVision, view.Mode);
        view.Update(0.016f);
        Assert.Equal(SceneSensor.NightVision, view.Mode);   // held, not pressed again
        keys.Release(EditorKey.F4);
        keys.Press(EditorKey.F4); view.Update(0.016f); keys.NextFrame();
        Assert.Equal(SensorView.ThermalOffered ? SceneSensor.Thermal : SceneSensor.None, view.Mode);
        Assert.Equal("F4", view.CycleKey);
    }

    /// <summary>⭐ KEY — the View ▸ Sensor View submenu: its three mode items carry the key as a shortcut, the current mode checked; the whole View menu
    /// still parses as the menu adapter's DTO array.</summary>
    [Fact]
    public void CE1989_TheSensorViewMenu_ShowsTheKey_AndParses()
    {
        var (view, _) = Make();
        view.SetMode(SceneSensor.NightVision);
        string json = SensorViewMenu.Json(view.MenuState, view.CycleKey);
        Assert.Contains("\"shortcut\":\"F4\"", json);
        using var doc = JsonDocument.Parse("[" + json + "]");
        var items = doc.RootElement[0].GetProperty("children");
        Assert.True(items[1].GetProperty("checked").GetBoolean());              // Night Vision checked
        Assert.Equal("F4", items[1].GetProperty("shortcut").GetString());
        var full = LayerControlGizmo.MainMenuJsonFor(false, false);
        Assert.EndsWith("}]}]", full);   // the splice point the per-frame menu relies on
    }
}
