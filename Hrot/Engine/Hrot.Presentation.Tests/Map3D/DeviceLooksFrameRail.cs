using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ SD-S5d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10a D-164 / D-166; the acceptance spec is <c>designs/devices/mockups/device-looks-visual.svg</c>) — devices
/// on a REAL GL frame: a man viewing his rangefinder holds a rangefinder, viewing binoculars holds binoculars (the two pictures differ); a carried device he has
/// not raised is not drawn (removing it changes nothing); the observer HMMWV's rangefinder is drawn at its station and turns with its <see cref="DevicePose"/>.
/// ⏳ Stacked on a device: SD-S5b (the pose chain) is not merged — the rail follows it. ⛔ SKIPS without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~DeviceLooksFrameRail</c>; <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> keeps <c>device-*.png</c> (never in git).
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DeviceLooksFrameRail
{
    private readonly ITestOutputHelper _out;
    public DeviceLooksFrameRail(ITestOutputHelper output) => _out = output;

    private static void Ensure<T>(EntityRepository w) where T : unmanaged { if (!w.IsComponentTypeRegistered<T>()) w.RegisterComponent<T>(); }

    private static AirCombatLooksRail.JetScene Scene()
    {
        var s = new AirCombatLooksRail.JetScene();
        Ensure<WeaponPosture>(s.World); Ensure<PassengerBuffer>(s.World); Ensure<IsEmbarkedTag>(s.World);
        Ensure<DeviceInfo>(s.World); Ensure<DeviceInput>(s.World); Ensure<DevicePose>(s.World);
        return s;
    }

    private static Entity Device(AirCombatLooksRail.JetScene s, long type, Entity holder, byte seat, Vector3 at, bool viewed)
    {
        var d = s.World.CreateEntity();
        s.World.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        s.World.AddComponent(d, new TkbIdentity { TkbType = (TkbTypeId)type });
        s.World.AddComponent(d, new DeviceInfo { DeviceType = type });
        s.World.AddComponent(d, new DeviceInput { Control = new DeviceControl { Viewed = viewed ? (byte)1 : (byte)0 } });
        s.World.AddComponent(d, new IsEmbarkedTag { VehicleEntity = holder, Seat = seat });
        if (s.World.HasComponent<PassengerBuffer>(holder))
        {
            var b = s.World.GetComponentRO<PassengerBuffer>(holder); b.Passengers[seat] = d; s.World.SetComponent(holder, b);
        }
        else { var b = new PassengerBuffer(); b.Passengers[seat] = d; s.World.AddComponent(holder, b); }
        return d;
    }

    /// <summary>A man raising <paramref name="device"/> (or nothing raised when <paramref name="held"/> is None), close up from his side.</summary>
    private AirCombatLooksRail.Pixels ManFrame(AirCombatLooksRail.JetScene s, Entity man, string? shot)
    {
        var at = s.Where(man);
        s.Look(new Vector3(at.X, at.Y, at.Z + 1.5f), 3.2f, MathF.PI * 0.5f + 0.5f, -0.12f);
        var px = AirCombatLooksRail.ScreenOf(s);
        if (shot != null) s.Shot(AirCombatLooksRail.ShotsDir(), shot);
        return px;
    }

    [SkippableFact]
    public void D164_AManHoldsTheDeviceHeViews_AndAnUnraisedOneIsNotDrawn()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        var man = s.Jet(TkbEntityTypes.Infantry_Rifleman, 0f, 0f);
        s.World.AddComponent(man, new WeaponPosture { HeldItem = HeldItemKind.Binoculars });
        var rangefinder = Device(s, TkbEntityTypes.Device_LaserRangefinder, man, 0, s.Where(man), viewed: true);
        var withRangefinder = ManFrame(s, man, "device-held-rangefinder.png");
        // the same man viewing binoculars instead
        s.World.SetComponent(rangefinder, new DeviceInput { Control = new DeviceControl { Viewed = 0 } });
        var binoculars = Device(s, TkbEntityTypes.Device_Binoculars, man, 1, s.Where(man), viewed: true);
        s.World.DestroyEntity(rangefinder);
        var withBinoculars = ManFrame(s, man, "device-held-binoculars.png");
        int differ = AirCombatLooksRail.Diff(withRangefinder, withBinoculars);
        _out.WriteLine($"rangefinder vs binoculars in his hands: {differ} px differ");
        Assert.True(differ > 40, $"a rangefinder is drawn as a rangefinder, not as binoculars: {differ} px");

        // not raised: he carries the binoculars but holds nothing — removing the device changes nothing (it used to draw as an unknown box)
        s.World.SetComponent(man, new WeaponPosture { HeldItem = HeldItemKind.None });
        var carried = ManFrame(s, man, "device-carried-not-raised.png");
        s.World.DestroyEntity(binoculars);
        var gone = ManFrame(s, man, null);
        Assert.Equal(0, AirCombatLooksRail.Diff(carried, gone));
    }

    [SkippableFact]
    public void D164_TheObserverHmmwvsRangefinder_IsDrawnAtItsStation_AndTurnsWithItsPose()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        var hmmwv = s.Jet(TkbEntityTypes.Truck_HMMWV_Observer, 0f, 0f);
        // the station's place, as EmbarkedStateSystem puts it: station 7 of the observer HMMWV (X 0.6, Y −0.45, Z 1.45 in its frame)
        var station = s.Where(hmmwv) + new Vector3(0.6f, -0.45f, 1.45f);
        Look(s, station);
        var without = AirCombatLooksRail.ScreenOf(s);
        var rf = Device(s, TkbEntityTypes.Device_LaserRangefinder, hmmwv, 7, station, viewed: false);
        s.World.SetComponent(rf, new IsEmbarkedTag { VehicleEntity = hmmwv, Seat = 7, Exposed = 0 });   // the seat is not declared exposed (CarrierSeats) — still drawn: it is MOUNTED
        Look(s, station);
        var ahead = AirCombatLooksRail.ScreenOf(s);
        s.Shot(AirCombatLooksRail.ShotsDir(), "device-station-ahead.png");
        int drawn = AirCombatLooksRail.Diff(without, ahead);
        _out.WriteLine($"the station's rangefinder: {drawn} px");
        Assert.True(drawn > 40, $"the rangefinder is drawn at the station: {drawn} px");
        s.World.AddComponent(rf, new DevicePose { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f) });   // the trainee looks left
        var left = AirCombatLooksRail.ScreenOf(s);
        s.Shot(AirCombatLooksRail.ShotsDir(), "device-station-left.png");
        int turned = AirCombatLooksRail.Diff(ahead, left);
        _out.WriteLine($"turned 90°: {turned} px");
        Assert.True(turned > 20, $"it turns with its DevicePose: {turned} px");
    }

    private static void Look(AirCombatLooksRail.JetScene s, Vector3 at) => s.Look(at, 2.6f, MathF.PI * 0.5f + 0.7f, -0.35f);
}
