using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

public sealed partial class PossessionStationTests
{
    /// <summary>A pickup at the origin facing east (network id 610): driver seat 0, gunner seat 1 (a man, 611; his place 1.4 m up, 1.5 m
    /// back), a pintle station 2 holding an M2 (612) operated by seat 1, its arc ±60° / −10…+30°.</summary>
    private static (Entity Pickup, Entity Gunner, Entity Gun) Technical(UseWorld u)
    {
        var pickup = u.OfType("Technical", Vector3.Zero, 610, new CarrierDefDto
        {
            Exposure = PassengerExposure.Open,
            Seats =
            {
                new SeatDto { Role = SeatRole.Driver },
                new SeatDto { Role = SeatRole.Gunner, X = -1.5f, Z = 1.4f, Pose = SeatPose.Stand },
                new SeatDto
                {
                    Role = SeatRole.Station, Accepts = MountKinds.Pintle, OperatedBy = 1, NeedsGunner = true, X = -1.5f, Z = 1.4f,
                    TraverseMinDeg = -60f, TraverseMaxDeg = 60f, ElevationMinDeg = -10f, ElevationMaxDeg = 30f,
                },
            },
        });
        var gunner = u.Soldier(new Vector3(0f, 0f, 0f), 100f, 611);
        var gun = u.OfType("M2", Vector3.Zero, 612, weapon: new WeaponSystemDto { MountKinds = MountKinds.Pintle, NeedsOperator = true });
        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, gunner, pickup, SeatRole.Gunner));
        Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(u.W, gun, pickup));
        return (pickup, gunner, gun);
    }

    /// <summary>
    /// ⭐⭐ CE-1210 (docs/DESIGN_Entity_Control.md §8n MS-1…MS-7, R-1018) — seat switching: the seats are the pickup (its driver seat) then the
    /// man at each manned gun; G possesses the next one (Fps — the pickup is released, its brain flies its order); his eye is at his seat,
    /// his look held in the gun's arc, W/A/S/D move nothing, the HUD names the seat; G again goes back to the pickup.
    /// ✅ Red-proof: SeatSwitch.Next returning null ⇒ G asks for nothing.
    /// </summary>
    [Fact]
    public void CE1210_G_SwitchesToTheGunnersSeat_TheEyeIsAtTheSeat_TheLookInTheGunsArc_AndGBack()
    {
        using var u = new UseWorld();
        var (pickup, gunner, _) = Technical(u);
        Assert.Equal(new[] { pickup, gunner }, SeatSwitch.Seats(u.W, pickup));
        Assert.Equal(gunner, SeatSwitch.Next(u.W, pickup));
        Assert.Equal(pickup, SeatSwitch.Next(u.W, gunner));
        Assert.Equal(Entity.Null, SeatSwitch.Next(u.W, u.Me));                              // on foot: no seats

        // the station drives the pickup; G asks to possess the gunner (Fps — which releases the pickup)
        u.W.RemoveComponent<PossessedBy>(u.Me);
        u.W.AddComponent(pickup, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });
        var (station, sw, _) = Station(u, 0);
        Assert.Equal(pickup, station.Unit);
        Assert.Equal("SEAT DRIVER  (G: next seat)", PossessionStation.Hud.SeatLine(u.W, pickup));
        sw.Camera3D.HandleInput(Key(Raylib_cs.KeyboardKey.G));
        u.W.Bus.SwapBuffers();
        var ask = Assert.Single(u.W.Bus.Read<PossessRequestEvent>().ToArray());
        Assert.Equal((gunner, PossessionMode.Fps), (ask.Unit, ask.Mode));

        // the brain applies it: now the gunner is held, the pickup free
        u.W.RemoveComponent<PossessedBy>(pickup);
        u.W.AddComponent(gunner, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 2 });
        station.Update(0.016f);
        sw.Camera3D.Update(0.016f);
        var driver = Assert.IsType<FpsCameraDriver>(sw.Camera3D.Driver);
        Assert.Equal(gunner, driver.Unit);
        Assert.True(driver.Aboard);
        Assert.InRange(Vector3.Distance(driver.Eye, new Vector3(-1.5f, 0f, 1.4f + SeatSwitch.EyeAboveHips)), 0f, 0.01f);   // at his seat
        Assert.StartsWith("SEAT M2", PossessionStation.Hud.SeatLine(u.W, gunner));

        var input = Key(Raylib_cs.KeyboardKey.W, pressed: false);
        input.MouseDelta = new Vector2(-600f, -600f);                                          // ~85° left and up — past the arc
        var frame = Frame(u, sw, input);
        Assert.Equal((0f, 0f), (frame.Forward, frame.Lateral));                                // the seat holds him
        Assert.Equal(60f, driver.Yaw * 180f / MathF.PI, 1);                                    // the arc's left edge
        Assert.Equal(30f, driver.Pitch * 180f / MathF.PI, 1);                                  // its top

        sw.Camera3D.HandleInput(Key(Raylib_cs.KeyboardKey.G));
        u.W.Bus.SwapBuffers();
        Assert.Equal(pickup, Assert.Single(u.W.Bus.Read<PossessRequestEvent>().ToArray()).Unit);   // back to the driver seat
    }

    /// <summary>
    /// ⭐⭐ <c>R-4096</c> S4 (docs/DESIGN_Entity_Control.md C-3′, §8n MW-1) — G never moves into a seat ANOTHER player holds: the driver seat
    /// (the vehicle) while another player drives it, a gun seat while another player plays it — no G, no seat line; the moment that player
    /// lets go the seat switches again (and a seat nobody holds always did). ✅ Red-proof: <c>SeatSwitch.SeatsFor</c> without its
    /// <c>RemoveAll</c> ⇒ G offers the held seat and the first assert fails.
    /// </summary>
    [Fact]
    public void R4096_G_NeverGoesToASeatAnotherPlayerHolds_ASeatNobodyHoldsStillSwitches()
    {
        using var u = new UseWorld();
        var (pickup, gunner, _) = Technical(u);
        u.W.RemoveComponent<PossessedBy>(u.Me);
        u.W.AddComponent(gunner, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 1 });   // I play the gun
        Assert.Equal(pickup, SeatSwitch.Next(u.W, gunner));                                        // the driver seat is nobody's: as before

        u.W.AddComponent(pickup, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 1 });   // another player drives
        Assert.Equal(Entity.Null, SeatSwitch.Next(u.W, gunner));                                   // never pushed from it
        Assert.Null(PossessionStation.Hud.SeatLine(u.W, gunner));

        u.W.RemoveComponent<PossessedBy>(pickup);                                                  // I drive, he plays the gun
        u.W.RemoveComponent<PossessedBy>(gunner);
        u.W.AddComponent(pickup, new PossessedBy { StationNodeId = 0, Mode = PossessionMode.Fps, Epoch = 2 });
        u.W.AddComponent(gunner, new PossessedBy { StationNodeId = 77, Mode = PossessionMode.Fps, Epoch = 1 });
        Assert.Equal(Entity.Null, SeatSwitch.Next(u.W, pickup));
        Assert.Equal(new[] { pickup, gunner }, SeatSwitch.Seats(u.W, pickup));                     // the seats exist; they are just not mine to take

        u.W.RemoveComponent<PossessedBy>(gunner);                                                  // he lets go (or is killed)
        Assert.Equal(gunner, SeatSwitch.Next(u.W, pickup));
        Assert.Equal("SEAT DRIVER  (G: next seat)", PossessionStation.Hud.SeatLine(u.W, pickup));
    }
}
