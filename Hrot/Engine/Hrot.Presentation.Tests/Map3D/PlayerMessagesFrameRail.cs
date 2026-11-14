using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1461 W-M7 — a FRAME of the possession overlay in a real window (Xvfb + Mesa): a message addressed to the held unit is drawn as a bottom-centre
/// subtitle, the open log as a panel, and with <c>hud.messages</c> off neither appears. Pixels are counted in the regions the text lands in
/// (set <c>HROT_RAIL_SHOTS</c> to also keep the frames; never committed). Skips — never fails — where there is no display.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class PlayerMessagesFrameRail
{
    private static int Bright(Image image, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y += 1)
            for (int x = x0; x < x1; x += 1)
            {
                var c = Raylib.GetImageColor(image, x, y);
                if (c.R > 200 && c.G > 200 && c.B > 200) n++;
            }
        return n;
    }

    private static (int Subtitle, int Log) Frame(bool featureOn, bool logOpen, string name)
    {
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<PossessedBy>();
        world.RegisterComponent<WeaponAmmo>();
        world.RegisterComponent<Health>();
        var unit = world.CreateEntity();
        world.AddComponent(unit, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.Identity });
        world.AddComponent(unit, new Health { Current = 80f, Max = 100f });
        world.AddComponent(unit, new PossessedBy { StationNodeId = (Fdp.Core.NodeId)(0), Mode = PossessionMode.Fps, Epoch = 1 });
        var map = new NetworkEntityMap();
        map.Register(4242, unit);
        world.SetSingletonManaged(map);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 12.0 });
        PlayerMessages.In(world).Record(new PlayerMessageEvent { MessageId = 1, ToKind = "Unit", ToValue = 4242, Text = "Hold the bridge - reinforcements in two minutes", Seconds = 6f, SentAt = (SimInstant)10 });

        using var frame = UiFrameHarness.Begin(1280, 800);
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        var view = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = new Vector2(1280, 800) });
        var gate = new Hrot.Common.Infrastructure.NodeFeatures(new Hrot.Common.Infrastructure.FeatureRegistry(Hrot.Common.Infrastructure.HudFeatures.Declarations(true)), Fdp.Core.NodeRole.Map2D);
        if (!featureOn) gate.SetNodeOverride(Hrot.Common.Infrastructure.HudFeatures.Messages, "{\"Enabled\":false}");
        var station = new PossessionStation(view, () => world, featureGate: () => gate);
        canvas.AddLayer(station);
        station.Update(0.016f);
        view.Camera3D.Update(0.016f);
        Assert.True(station.InFps);
        if (logOpen) station.ToggleMessageLog();

        frame.Step(canvas.Draw);
        var image = Raylib.LoadImageFromScreen();
        try
        {
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); Raylib.ExportImage(image, Path.Combine(dir, name + ".png")); }
            // the subtitle sits in the bottom-centre band; the log panel in the middle of the screen
            return (Bright(image, 320, 590, 960, 672), Bright(image, 280, 300, 1000, 500));
        }
        finally { Raylib.UnloadImage(image); }
    }

    [SkippableFact]
    public void CE1461_TheOverlay_DrawsTheSubtitle_AndTheOpenLog_AndNothingWithTheFeatureOff()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var off = Frame(featureOn: false, logOpen: true, "messages-off");
        var subtitle = Frame(featureOn: true, logOpen: false, "messages-subtitle");
        var log = Frame(featureOn: true, logOpen: true, "messages-log");

        Assert.True(subtitle.Subtitle > off.Subtitle + 150, $"the subtitle is drawn bottom-centre ({subtitle.Subtitle} bright pixels vs {off.Subtitle} with the feature off)");
        Assert.True(log.Log > subtitle.Log + 150, $"the open log is drawn ({log.Log} vs {subtitle.Log} closed)");
        Assert.True(off.Log <= subtitle.Log + 20, "with the feature off, the log key shows nothing either");
    }
}
