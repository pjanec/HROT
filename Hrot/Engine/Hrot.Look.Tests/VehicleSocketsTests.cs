using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Look.Vehicles;

namespace Hrot.Look.Tests;

/// <summary>⭐ CE-4670 (📄 DESIGN_Vehicle_Appearance_State §7d D-1…D-4) — where a vehicle's sockets sit: every socket name the TKB uses places, on the right face, a turret
/// one turns with the turret, the pairs, and a lid opens the way its kind opens. Pure, 0 B.</summary>
public sealed class VehicleSocketsTests
{
    // an MBT-ish box: 7 m long, 3.6 m wide, 1.8 m hull; a turret on top
    private static readonly BodyBox Hull = new(new Vector3(0f, 0f, 0.9f), new Vector3(3.5f, 1.8f, 0.9f));
    private static readonly BodyBox Turret = new(new Vector3(-0.3f, 0f, 2.15f), new Vector3(1.2f, 1.1f, 0.35f));
    private static readonly VehicleBoxes Tank = new(Hull, Turret);
    private static readonly VehicleBoxes Apc = new(Hull);

    /// <summary>Every opening socket in <c>bodies.json</c> (the inventory, §7d).</summary>
    public static TheoryData<string, OpeningKind> OpeningSockets => new()
    {
        { "hatch_commander", OpeningKind.Hatch }, { "hatch_gunner", OpeningKind.Hatch }, { "hatch_driver", OpeningKind.Hatch }, { "ramp_rear", OpeningKind.Ramp },
        { "door_front_left", OpeningKind.Door }, { "door_front_right", OpeningKind.Door }, { "door_rear", OpeningKind.CargoDoor }, { "door_cabin", OpeningKind.Door },
        { "door_cockpit", OpeningKind.Door }, { "door_crew", OpeningKind.Door }, { "canopy", OpeningKind.Hatch },
    };

    [Theory]
    [MemberData(nameof(OpeningSockets))]
    public void EveryOpeningSocket_Places_OnTheBox_AndMovesWhenOpened(string socket, OpeningKind kind)
    {
        Assert.True(VehicleSockets.Opening(socket, kind, Apc, out var p));
        var shut = p.CentreAt(0f);
        Assert.InRange(shut.X, Hull.Rear - 0.2f, Hull.Front + 0.2f);
        Assert.InRange(shut.Y, -Hull.Half.Y - 0.2f, Hull.Half.Y + 0.2f);
        Assert.InRange(shut.Z, Hull.Bottom, Hull.Top + 0.2f);
        Assert.True(Vector3.Distance(shut, p.CentreAt(1f)) > 0.2f, "opening moves the panel");
    }

    [Fact]
    public void TheLids_OpenTheWayTheirKindOpens()
    {
        Assert.True(VehicleSockets.Opening("hatch_commander", OpeningKind.Hatch, Tank, out var hatch));
        Assert.True(hatch.OnTurret);
        Assert.True(hatch.CentreAt(1f).Z > hatch.CentreAt(0f).Z + 0.2f, "a hatch lid goes up");
        Assert.True(hatch.CentreAt(0f).Y > Turret.Centre.Y, "the commander's is on the left (CE-1200)");
        Assert.True(VehicleSockets.Opening("hatch_gunner", OpeningKind.Hatch, Tank, out var gunner));
        Assert.True(gunner.CentreAt(0f).Y < Turret.Centre.Y);
        Assert.True(VehicleSockets.Opening("ramp_rear", OpeningKind.Ramp, Apc, out var ramp));
        Assert.True(ramp.CentreAt(1f).X < Hull.Rear - 0.5f && ramp.CentreAt(1f).Z < ramp.CentreAt(0f).Z, "a ramp falls back and down");
        Assert.True(VehicleSockets.Opening("door_front_left", OpeningKind.Door, Apc, out var left));
        Assert.True(left.CentreAt(1f).Y > left.CentreAt(0f).Y + 0.2f, "a left door swings out to the left");
        Assert.True(VehicleSockets.Opening("door_front_right", OpeningKind.Door, Apc, out var right));
        Assert.True(right.CentreAt(1f).Y < right.CentreAt(0f).Y - 0.2f);
        Assert.True(VehicleSockets.Opening("door_rear", OpeningKind.CargoDoor, Apc, out var rear));
        Assert.True(rear.CentreAt(1f).X < rear.CentreAt(0f).X - 0.2f, "a rear door swings out backward");
        Assert.True(VehicleSockets.Opening("no_such_socket", OpeningKind.Hatch, Apc, out _));   // by kind
    }

    [Fact]
    public void TheExposedCrewman_StandsInHisHatch()
    {
        Assert.True(VehicleSockets.Opening("hatch_commander", OpeningKind.Hatch, Tank, out var hatch));
        var crew = VehicleSockets.ExposedCrew(hatch);
        Assert.True(crew.Head.Z > Turret.Top + 0.3f);
        Assert.True(Vector3.Distance(new Vector3(crew.Head.X, crew.Head.Y, 0f), new Vector3(hatch.CentreAt(0f).X, hatch.CentreAt(0f).Y, 0f)) < 0.01f);
    }

    [Theory]
    [InlineData("light_head", LightKind.Head, 2)]
    [InlineData("light_tail", LightKind.Tail, 2)]
    [InlineData("light_brake", LightKind.Brake, 2)]
    [InlineData("light_nav", LightKind.Nav, 2)]
    [InlineData("light_convoy", LightKind.Convoy, 1)]
    [InlineData("light_beacon", LightKind.AntiCollision, 1)]
    [InlineData("light_landing", LightKind.Landing, 1)]
    [InlineData("light_search", LightKind.Searchlight, 1)]
    [InlineData("light_interior", LightKind.Interior, 1)]
    public void EveryLightSocket_Places(string socket, LightKind kind, int count)
    {
        Span<LightPoint> into = stackalloc LightPoint[2];
        Assert.Equal(count, VehicleSockets.Lights(socket, kind, Tank, into));
        if (kind == LightKind.Head) Assert.True(into[0].At.X >= Hull.Front - 1e-3f && into[0].At.Y > 0f && into[1].At.Y < 0f);
        if (kind == LightKind.Tail) Assert.True(into[0].At.X <= Hull.Rear + 1e-3f && into[0].Colour.R > 150 && into[0].Colour.G < 60);
        if (kind == LightKind.Nav) Assert.True(into[0].Colour.R > 200 && into[1].Colour.G > 200, "red left, green right");
    }

    [Fact]
    public void ThePlacement_AllocatesNothing()
    {
        Span<LightPoint> into = stackalloc LightPoint[2];
        VehicleSockets.Opening("hatch_commander", OpeningKind.Hatch, Tank, out _);
        VehicleSockets.Lights("light_head", LightKind.Head, Tank, into);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            VehicleSockets.Opening("ramp_rear", OpeningKind.Ramp, Apc, out var p);
            _ = p.BoxAt(0.5f);
            VehicleSockets.Lights("light_tail", LightKind.Tail, Tank, into);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
