using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-4670</c> (📄 DESIGN_Vehicle_Appearance_State §7d D-6; mockup docs/UX/vehicle-openings-lights.svg C) — a possessed crewman's H and U: H asks his head out
/// (and back in once it is out) for HIM, not for the vehicle; U asks the next discipline; the HUD lines say the replicated state; a man with no hatch (a passenger) and a
/// vehicle with no lights get neither. 🔴 Red-proof (compiles): <c>ToggleHeadOut</c> asking with <c>!exposed</c> negated ⇒ the first H asks him IN.
/// </summary>
public sealed class CrewControlsTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly Fdp.Interfaces.ITkbDatabase _tkb = Map3DTests.BuiltIn();

    public CrewControlsTests()
    {
        _w.RegisterComponent<TkbIdentity>();
        _w.RegisterComponent<PassengerBuffer>();
        _w.RegisterComponent<CarrierSeats>();
        _w.RegisterComponent<CarrierStations>();
        _w.RegisterComponent<IsEmbarkedTag>();
        _w.RegisterComponent<VehicleOpenings>();
        _w.RegisterComponent<VehicleLights>();
        _w.RegisterEvent<OpeningRequestEvent>();
        _w.RegisterEvent<LightsRequestEvent>();
        _w.SetSingletonManaged(_tkb);
    }

    public void Dispose() => _w.Dispose();

    /// <summary>A T-72 with a commander in seat 1 and a passenger in seat 2.</summary>
    private (Entity Tank, Entity Commander, Entity Passenger) Tank()
    {
        var tank = _w.CreateEntity();
        _w.AddComponent(tank, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Tank_T72 });
        var commander = _w.CreateEntity();
        var passenger = _w.CreateEntity();
        var list = new PassengerBuffer();
        list.Passengers[1] = commander;
        list.Passengers[2] = passenger;
        _w.AddComponent(tank, list);
        var seats = new CarrierSeats { SeatCount = 3 };
        seats.Roles[0] = SeatRole.Driver; seats.Roles[1] = SeatRole.Commander; seats.Roles[2] = SeatRole.Passenger;
        _w.AddComponent(tank, seats);
        _w.AddComponent(commander, new IsEmbarkedTag { VehicleEntity = tank, Seat = 1 });
        _w.AddComponent(passenger, new IsEmbarkedTag { VehicleEntity = tank, Seat = 2 });
        return (tank, commander, passenger);
    }

    private OpeningRequestEvent[] Asked() { _w.Bus.SwapBuffers(); return _w.ReadEvents<OpeningRequestEvent>().ToArray(); }

    [Fact]
    public void H_AsksHisHeadOut_ThenBackIn_AndTheHudSaysWhichFromTheReplicatedState()
    {
        var (tank, commander, _) = Tank();
        Assert.True(CrewControls.HatchOf(_w, _tkb, commander, out var vehicle, out var man, out var id));
        Assert.Equal(tank, vehicle); Assert.Equal(commander, man);
        Assert.Equal("HATCH CLOSED  (H: head out)", CrewControls.HatchLine(_w, _tkb, commander, "H"));

        Assert.True(CrewControls.ToggleHeadOut(_w, _tkb, commander));
        var ask = Assert.Single(Asked());
        Assert.Equal(tank, ask.Vehicle); Assert.Equal(commander, ask.Man); Assert.Equal(OpeningVerb.HeadOut, ask.Verb);

        // the owner opened it and marked him exposed — replicated back
        var openings = VehicleOpeningsCatalog.Of(_tkb.GetByType(TkbEntityTypes.Tank_T72)!);
        var state = new VehicleOpenings { Count = (byte)openings.Length };
        for (int i = 0; i < openings.Length; i++)
            if (openings[i].Id == id) state.Slots[i] = new OpeningSlot { State = OpeningState.Open, CrewExposed = true };
        _w.AddComponent(tank, state);
        Assert.Equal("HEAD OUT  (H: head in)", CrewControls.HatchLine(_w, _tkb, commander, "H"));
        Assert.True(CrewControls.ToggleHeadOut(_w, _tkb, commander));
        Assert.Equal(OpeningVerb.HeadIn, Assert.Single(Asked()).Verb);
    }

    [Fact]
    public void APassenger_HasNoHatch_AndTheVehicleItself_PutsItsDriversHeadOut()
    {
        var (tank, _, passenger) = Tank();
        Assert.Null(CrewControls.HatchLine(_w, _tkb, passenger, "H"));
        Assert.False(CrewControls.ToggleHeadOut(_w, _tkb, passenger));
        Assert.Empty(Asked());
        // held as the vehicle itself (its driver seat): the man in the Driver role
        _w.AddComponent(tank, new CarrierStations());
        var driver = _w.CreateEntity();
        var list = _w.GetComponentRO<PassengerBuffer>(tank);
        list.Passengers[0] = driver;
        _w.SetComponent(tank, list);
        Assert.Equal(driver, CrewControls.ManOf(_w, tank, out var v));
        Assert.Equal(tank, v);
    }

    [Fact]
    public void U_CyclesTheDiscipline_AndTheHudSaysIt()
    {
        var (tank, commander, _) = Tank();
        Assert.Equal("LIGHTS NORMAL  (U: switch)", CrewControls.LightsLine(_w, _tkb, commander, "U"));
        Assert.True(CrewControls.CycleLights(_w, _tkb, commander));
        _w.Bus.SwapBuffers();
        var ask = Assert.Single(_w.ReadEvents<LightsRequestEvent>().ToArray());
        Assert.Equal(tank, ask.Vehicle); Assert.Equal(LightDiscipline.Convoy, ask.Discipline); Assert.Equal(commander, ask.Actor);
        _w.AddComponent(tank, new VehicleLights { Discipline = LightDiscipline.Blackout });
        Assert.Equal("LIGHTS BLACKOUT  (U: switch)", CrewControls.LightsLine(_w, _tkb, commander, "U"));
        Assert.Equal(LightDiscipline.Normal, CrewControls.Next(LightDiscipline.Off));
    }
}
