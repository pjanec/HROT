using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Look.Vehicles;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-4670</c> (📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §7d D-6; mockup docs/UX/vehicle-openings-lights.svg C) — what a possessed
/// CREWMAN may do with his vehicle from the station: put his head out of his hatch or back in (<b>H</b>) and switch the vehicle's light discipline (<b>U</b>), each a
/// REQUEST carried to the vehicle's owner (<see cref="VehicleRequests"/>); the HUD lines say the replicated state. The held unit is the man in a seat, or the
/// vehicle itself (its driver seat ⇒ the man in the Driver role). Pure over the world — the rail runs it without a station.
/// </summary>
public static class CrewControls
{
    /// <summary>The man whose head goes out: <paramref name="unit"/> himself when he sits in a vehicle; the man in the Driver role when the held unit IS the
    /// vehicle. Null when there is none.</summary>
    public static Entity ManOf(EntityRepository w, Entity unit, out Entity vehicle)
    {
        vehicle = SeatSwitch.VehicleOf(w, unit);
        if (vehicle.IsNull) return Entity.Null;
        if (vehicle != unit) return unit;
        if (!Has<PassengerBuffer>(w, vehicle) || !Has<CarrierSeats>(w, vehicle)) return Entity.Null;
        ref readonly var list = ref w.GetComponentRO<PassengerBuffer>(vehicle);
        ref readonly var seats = ref w.GetComponentRO<CarrierSeats>(vehicle);
        for (int i = 0; i < PassengerBuffer.Capacity; i++)
            if (seats.Roles[i] == SeatRole.Driver && !list.Passengers[i].IsNull) return list.Passengers[i];
        return Entity.Null;
    }

    /// <summary>The hatch his seat role is served by (the first, by the type's order): its TKB id; false when he has none (a passenger in the back, a car).</summary>
    public static bool HatchOf(EntityRepository w, ITkbDatabase? tkb, Entity unit, out Entity vehicle, out Entity man, out byte id)
    {
        id = 0;
        man = ManOf(w, unit, out vehicle);
        if (man.IsNull || TypeOf(w, vehicle, tkb) is not { } type || !Has<PassengerBuffer>(w, vehicle)) return false;
        var openings = VehicleOpeningsCatalog.Of(type);
        if (openings.Length == 0) return false;
        ref readonly var list = ref w.GetComponentRO<PassengerBuffer>(vehicle);
        int seat = -1;
        for (int i = 0; i < PassengerBuffer.Capacity; i++) if (list.Passengers[i] == man) { seat = i; break; }
        if (seat < 0) return false;
        var role = Has<CarrierSeats>(w, vehicle) ? w.GetComponentRO<CarrierSeats>(vehicle).Roles[seat] : SeatRole.Passenger;
        int mask = VehicleOpeningsCatalog.ServingMask(openings, role);
        for (int i = 0; i < openings.Length; i++)
            if ((mask & (1 << i)) != 0 && openings[i].Kind == OpeningKind.Hatch) { id = openings[i].Id; return true; }
        return false;
    }

    /// <summary>H: his head out when it is in, back in when it is out. False when he has no hatch (nothing asked).</summary>
    public static bool ToggleHeadOut(EntityRepository w, ITkbDatabase? tkb, Entity unit)
    {
        if (!HatchOf(w, tkb, unit, out var vehicle, out var man, out var id)) return false;
        return VehicleRequests.HeadOut(w, vehicle, man, !VehicleLook.CrewExposed(w, vehicle, id));
    }

    /// <summary>The vehicle's light discipline now (Normal when it carries none yet); null when <paramref name="unit"/> is in no vehicle whose type has lights.</summary>
    public static LightDiscipline? DisciplineOf(EntityRepository w, ITkbDatabase? tkb, Entity unit, out Entity vehicle)
    {
        vehicle = SeatSwitch.VehicleOf(w, unit);
        if (vehicle.IsNull || TypeOf(w, vehicle, tkb) is not { } type || VehicleLightsCatalog.Of(type).Length == 0) return null;
        return Has<VehicleLights>(w, vehicle) ? w.GetComponentRO<VehicleLights>(vehicle).Discipline : LightDiscipline.Normal;
    }

    /// <summary>The discipline U switches to: Normal → Convoy → Blackout → Off → Normal.</summary>
    public static LightDiscipline Next(LightDiscipline d) => d switch
    {
        LightDiscipline.Normal => LightDiscipline.Convoy,
        LightDiscipline.Convoy => LightDiscipline.Blackout,
        LightDiscipline.Blackout => LightDiscipline.Off,
        _ => LightDiscipline.Normal,
    };

    /// <summary>U: ask for the next discipline. False when the vehicle has no lights.</summary>
    public static bool CycleLights(EntityRepository w, ITkbDatabase? tkb, Entity unit)
    {
        if (DisciplineOf(w, tkb, unit, out var vehicle) is not { } d) return false;
        var man = ManOf(w, unit, out _);
        return VehicleRequests.SwitchLights(w, vehicle, Next(d), man);
    }

    /// <summary>The HUD's hatch line (<c>HATCH CLOSED  (H: head out)</c> / <c>HEAD OUT  (H: head in)</c>); null without a hatch for him.</summary>
    public static string? HatchLine(EntityRepository w, ITkbDatabase? tkb, Entity unit, string key)
        => !HatchOf(w, tkb, unit, out var vehicle, out _, out var id) ? null
         : VehicleLook.CrewExposed(w, vehicle, id) ? $"HEAD OUT  ({key}: head in)" : $"HATCH CLOSED  ({key}: head out)";

    /// <summary>The HUD's lights line (<c>LIGHTS NORMAL  (U: switch)</c>); null without light groups.</summary>
    public static string? LightsLine(EntityRepository w, ITkbDatabase? tkb, Entity unit, string key)
        => DisciplineOf(w, tkb, unit, out _) is { } d ? $"LIGHTS {d.ToString().ToUpperInvariant()}  ({key}: switch)" : null;

    /// <summary>The start of the two lines (their HUD colours).</summary>
    public const string HatchPrefix = "HATCH ", HeadOutPrefix = "HEAD OUT", LightsPrefix = "LIGHTS ";

    private static TkbTemplate? TypeOf(EntityRepository w, Entity vehicle, ITkbDatabase? tkb)
        => tkb != null && Has<TkbIdentity>(w, vehicle) && tkb.TryGetByType(w.GetComponentRO<TkbIdentity>(vehicle).TkbType, out var t) ? t : null;

    private static bool Has<T>(EntityRepository w, Entity e) where T : unmanaged => w.IsComponentTypeRegistered<T>() && w.HasComponent<T>(e);
}
