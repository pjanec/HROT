using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Look.Devices;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using DeviceLooks = Hrot.Look.Devices.DeviceLooks;   // ⚠ Fdp.Toolkit.Behavior.Devices.DeviceLooks is a different thing (a device FOLLOWS its operator's look) — same name
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ SD-S5d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10a D-163 / D-164) — THE device-look rule and the raised form, without a GL context: every
/// device type in the catalogue has a kind and the <see cref="VisualFamily.Device"/> family; the kind is TKB data (<see cref="DeviceDefDto.Look"/>), defaulted by
/// the behaviour, never from a name; a man who raises a device holds THAT device (a rangefinder with its red emitter, binoculars with their objectives), his own
/// pocket binoculars (no device) the generic box as before; and the rule's reads are 0 B. The pictures are <see cref="DeviceLooksFrameRail"/>'s.
/// </summary>
public sealed class DeviceLooksTests
{
    [Fact]
    public void D163_EveryDeviceTypeInTheCatalogue_HasAKind_AndTheDeviceFamily()
    {
        var tkb = Map3DTests.BuiltIn();
        int devices = 0;
        foreach (var t in tkb.GetAll())
        {
            var dto = t.GetDescriptor<DeviceDefDto>();
            if (dto == null) continue;
            devices++;
            Assert.Equal(VisualFamily.Device, VisualFamilies.Of(t));
            Assert.False(string.IsNullOrWhiteSpace(dto.Look), $"device type {(long)t.TkbType} states its look (D-163: TKB data)");
            Assert.True(Enum.TryParse<DeviceKind>(dto.Look, true, out var stated), $"{(long)t.TkbType}: '{dto.Look}' is a DeviceKind");
            Assert.Equal(stated, DeviceLooks.KindOf(t));
            var look = EntityBodyLayer3D.Resolve(t);
            Assert.Equal(VisualFamily.Device, look.Family);
            Assert.Equal(stated, look.Device);
            Assert.Equal(DeviceLooks.SizeOf(stated), look.Size);                              // its kind's size, never the vehicle box it was defined with
            Assert.Same(DeviceKits.For(stated), EntityBodyLayer3D.KitOf(look));
        }
        Assert.True(devices >= 2, $"anti-vacuity: {devices} device types");
        Assert.Equal(DeviceKind.Rangefinder, DeviceLooks.KindOf(tkb.GetByType(TkbEntityTypes.Device_LaserRangefinder)));
        Assert.Equal(DeviceKind.Binoculars, DeviceLooks.KindOf(tkb.GetByType(TkbEntityTypes.Device_Binoculars)));
    }

    [Fact]
    public void D163_TheKind_IsStated_ElseDefaultedByTheBehaviour_NeverByAName()
    {
        Assert.Equal(DeviceKind.Rangefinder, DeviceLooks.KindOf(new DeviceDefDto { Behavior = Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceRangefinder }));
        Assert.Equal(DeviceKind.Binoculars, DeviceLooks.KindOf(new DeviceDefDto { Behavior = Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceBinoculars }));
        Assert.Equal(DeviceKind.Designator, DeviceLooks.KindOf(new DeviceDefDto { Behavior = Hrot.Map.Definitions.Behavior.BehaviorNames.DeviceRangefinder, Look = "designator" }));
        Assert.Equal(DeviceKind.Box, DeviceLooks.KindOf(new DeviceDefDto { Behavior = "SomethingElse", Look = "not-a-kind" }));
        Assert.Equal(DeviceKind.Box, DeviceLooks.KindOf((DeviceDefDto?)null));
        foreach (DeviceKind k in Enum.GetValues<DeviceKind>())
        {
            Assert.True(DeviceLooks.SizeOf(k).X > 0f && DeviceLooks.SizeOf(k).Y > 0f && DeviceLooks.SizeOf(k).Z > 0f);
            Assert.NotEmpty(DeviceKits.For(k).Parts);
            foreach (var p in DeviceKits.For(k).Parts)   // every part inside its kind's box (x / y about the centre, z up from the bottom)
                Assert.True(MathF.Abs(p.Centre.X) + p.Extent.X / 2f <= 0.5f + 0.02f && MathF.Abs(p.Centre.Y) + p.Extent.Y / 2f <= 0.5f + 0.02f
                            && p.Centre.Z - p.Extent.Z / 2f >= -0.02f && p.Centre.Z + p.Extent.Z / 2f <= 1.02f, $"{k}: a part outside the box {p.Centre} ± {p.Extent}");
        }
    }

    internal static (EntityRepository World, Entity Man) ManWith(long? deviceType, bool viewed, HeldItemKind held = HeldItemKind.Binoculars)
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>(); w.RegisterComponent<TkbIdentity>(); w.RegisterComponent<WeaponPosture>();
        w.RegisterComponent<PassengerBuffer>(); w.RegisterComponent<IsEmbarkedTag>(); w.RegisterComponent<DeviceInfo>(); w.RegisterComponent<DeviceInput>();
        var man = w.CreateEntity();
        w.AddComponent(man, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        w.AddComponent(man, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
        w.AddComponent(man, new WeaponPosture { HeldItem = held });
        var buffer = new PassengerBuffer();
        if (deviceType is { } type)
        {
            var d = w.CreateEntity();
            w.AddComponent(d, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(d, new TkbIdentity { TkbType = (TkbTypeId)type });
            w.AddComponent(d, new DeviceInfo { DeviceType = type });
            w.AddComponent(d, new DeviceInput { Control = new DeviceControl { Viewed = viewed ? (byte)1 : (byte)0 } });
            w.AddComponent(d, new IsEmbarkedTag { VehicleEntity = man, Seat = 0 });
            buffer.Passengers[0] = d;
        }
        w.AddComponent(man, buffer);
        return (w, man);
    }

    private static KitPart[] HeldParts(long? deviceType, bool viewed = true)
    {
        var (w, man) = ManWith(deviceType, viewed);
        using (w)
        {
            var plain = BlockFigure.Parts(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, soldier: true);
            var (parts, item) = EntityBodyLayer3D.HeldItemShown(w, man, true, FigureCarriage.Free, plain, MotionPose.Still, Map3DTests.BuiltIn());
            Assert.Equal(HeldItemKind.Binoculars, item);
            return parts.AsSpan(plain.Length).ToArray();   // what his hands hold
        }
    }

    /// <summary>D-164 ① — viewing his rangefinder he holds a RANGEFINDER (its red emitter aperture, its olive body), viewing binoculars he holds binoculars (two
    /// glass objectives, no emitter); with no device (his own pocket binoculars) the generic dark box, as before. 🔴 Red-proof (run): <c>HeldItemShown</c> passing
    /// no device ⇒ the rangefinder hand holds the one dark box ⇒ red.</summary>
    [Fact]
    public void D164_ARaisedDevice_IsHeldAsItself()
    {
        var rangefinder = HeldParts(TkbEntityTypes.Device_LaserRangefinder);
        Assert.Contains(rangefinder, p => p.Colour == ColourRole.Red);
        Assert.Contains(rangefinder, p => p.Colour == ColourRole.Canvas);                   // the olive body (a man's parts shade HIS colour: the device keeps its own)
        Assert.Equal(DeviceKits.For(DeviceKind.Rangefinder).Parts.Length, rangefinder.Length);
        var binoculars = HeldParts(TkbEntityTypes.Device_Binoculars);
        Assert.DoesNotContain(binoculars, p => p.Colour == ColourRole.Red);
        Assert.Equal(2, binoculars.Count(p => p.Colour == ColourRole.Glass));
        var pocket = HeldParts(null);
        Assert.Single(pocket);
        Assert.Equal(ColourRole.Dark, pocket[0].Colour);
        // held at the eyes: every part of the rangefinder between 1.5 and 1.75 m (fractions × the figure's 1.8 m)
        Assert.All(rangefinder, p => Assert.InRange(p.Centre.Z * 1.8f, 1.5f, 1.75f));
    }

    /// <summary>The one a view shows is the one raised: a man carrying binoculars (first seat) and a VIEWED rangefinder holds the rangefinder.</summary>
    [Fact]
    public void D164_TheViewedDevice_IsTheOneRaised()
    {
        var (w, man) = ManWith(TkbEntityTypes.Device_Binoculars, viewed: false);
        using (w)
        {
            var r = w.CreateEntity();
            w.AddComponent(r, new SimTransform { Rotation = Quaternion.Identity });
            w.AddComponent(r, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Device_LaserRangefinder });
            w.AddComponent(r, new DeviceInfo { DeviceType = TkbEntityTypes.Device_LaserRangefinder });
            w.AddComponent(r, new DeviceInput { Control = new DeviceControl { Viewed = 1 } });
            w.AddComponent(r, new IsEmbarkedTag { VehicleEntity = man, Seat = 1 });
            var b = w.GetComponentRO<PassengerBuffer>(man); b.Passengers[1] = r; w.SetComponent(man, b);
            Assert.True(DeviceLooks.RaisedBy(w, man, out var raised));
            Assert.Equal(r, raised);
            Assert.Equal(DeviceKind.Rangefinder, DeviceLooks.RaisedKind(w, man, Map3DTests.BuiltIn()));
            Assert.Equal(man, DeviceLooks.HolderOf(w, r));
        }
    }

    /// <summary>0 B: the per-frame reads the drawing added (the kind of a type, the raised device, its holder, its world pose) allocate nothing once warm.</summary>
    [Fact]
    public void TheAddedReads_AllocateNothing()
    {
        var (w, man) = ManWith(TkbEntityTypes.Device_LaserRangefinder, viewed: true);
        using (w)
        {
            var tkb = Map3DTests.BuiltIn();
            var device = w.GetComponentRO<PassengerBuffer>(man).Passengers[0];
            for (int i = 0; i < 10; i++) { DeviceLooks.RaisedKind(w, man, tkb); DeviceLooks.HolderOf(w, device); DevicePoses.TryWorld(w, device, out _, out _); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sink = 0;
            for (int i = 0; i < 1_000; i++)
            {
                sink += (int)DeviceLooks.RaisedKind(w, man, tkb) + (DeviceLooks.HolderOf(w, device).IsNull ? 0 : 1);
                if (DevicePoses.TryWorld(w, device, out var at, out _)) sink += (int)at.X;
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(sink > 0);
        }
    }
}
