using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1210</c> (docs/DESIGN_Entity_Control.md §8n MS-1…MS-6, R-1018) — SEAT SWITCHING in a vehicle with crewed weapons: its seats are
/// the vehicle itself (the driver / pilot seat — possessing the vehicle, as today) and, for each STATION whose operating seat holds a man,
/// that man (possessed, the vehicle is released and flies its order, MS-1). An empty gunner seat is skipped (MS-4). Read off the replicated
/// passenger list and the type's stations, so it answers on the possessing node.
/// </summary>
public static class SeatSwitch
{
    /// <summary>A seated gunner's eye above the seat's place (the seat's Z is the hips, Carriage.InSeat) (m).</summary>
    public const float EyeAboveHips = 0.72f;

    /// <summary>The vehicle <paramref name="unit"/> plays a seat of: itself when it has stations, else the vehicle he sits in; null otherwise.</summary>
    public static Entity VehicleOf(EntityRepository w, Entity unit)
    {
        if (!w.IsAlive(unit)) return Entity.Null;
        if (Has<CarrierStations>(w, unit) && Has<PassengerBuffer>(w, unit) && !Has<PersonCarrier>(w, unit)) return unit;
        return Has<IsEmbarkedTag>(w, unit) ? w.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity : Entity.Null;
    }

    /// <summary>The seats of <paramref name="vehicle"/> in order: the vehicle (its driver / pilot seat), then each operating seat's man.</summary>
    public static List<Entity> Seats(EntityRepository w, Entity vehicle)
    {
        var seats = new List<Entity>();
        if (vehicle.IsNull || !w.IsAlive(vehicle) || !Has<CarrierStations>(w, vehicle) || !Has<PassengerBuffer>(w, vehicle)) return seats;
        seats.Add(vehicle);
        ref readonly var list = ref w.GetComponentRO<PassengerBuffer>(vehicle);
        ref readonly var stations = ref w.GetComponentRO<CarrierStations>(vehicle);
        for (int i = 0; i < PassengerBuffer.Capacity; i++)
        {
            if (!stations.IsStation(i) || list.Passengers[i].IsNull) continue;
            int g = stations.Seats[i].OperatedBy;
            if (g < 0 || g >= PassengerBuffer.Capacity) continue;
            var man = list.Passengers[g];
            if (!man.IsNull && w.IsAlive(man) && Fdp.Toolkit.Combat.Injury.IsInTheFight(w, man) && !seats.Contains(man)) seats.Add(man);
        }
        return seats;
    }

    /// <summary>
    /// ⭐ <c>R-4096</c> (C-3′, MW-1 seat switching) — the seats <paramref name="unit"/>'s player may go to: <see cref="Seats"/> without the
    /// ones ANOTHER live station holds (the vehicle itself when another player drives it, a gunner another player plays). He never pushes
    /// anyone from a seat; a seat nobody holds — an AI gunner's, an empty driver's — still switches. <paramref name="unit"/> itself
    /// stays in the list (the place the next seat is counted from).
    /// </summary>
    public static List<Entity> SeatsFor(EntityRepository w, Entity unit)
    {
        long? me = Fdp.Toolkit.Behavior.Possession.PossessionSystem.HolderOf(w, unit);
        var seats = Seats(w, VehicleOf(w, unit));
        seats.RemoveAll(seat => seat != unit && Fdp.Toolkit.Behavior.Possession.StationClaims.TryHeldByOther(w, seat, me, out _));
        return seats;
    }

    /// <summary>The seat after <paramref name="unit"/>'s (wrapping), or null when the vehicle has no other.</summary>
    public static Entity Next(EntityRepository w, Entity unit)
    {
        var seats = SeatsFor(w, unit);
        if (seats.Count < 2) return Entity.Null;
        int at = Math.Max(0, seats.IndexOf(unit));
        return seats[(at + 1) % seats.Count];
    }

    /// <summary>The station <paramref name="man"/>'s seat operates (its arc), when he sits in one; false otherwise.</summary>
    public static bool StationOf(EntityRepository w, Entity man, out Entity vehicle, out StationSeat station, out Entity weapon)
    {
        vehicle = weapon = Entity.Null;
        station = default;
        if (!Has<IsEmbarkedTag>(w, man)) return false;
        var tag = w.GetComponentRO<IsEmbarkedTag>(man);
        vehicle = tag.VehicleEntity;
        if (!w.IsAlive(vehicle) || !Has<CarrierStations>(w, vehicle) || !Has<PassengerBuffer>(w, vehicle)) return false;
        ref readonly var stations = ref w.GetComponentRO<CarrierStations>(vehicle);
        ref readonly var list = ref w.GetComponentRO<PassengerBuffer>(vehicle);
        for (int i = 0; i < PassengerBuffer.Capacity; i++)
            if (stations.IsStation(i) && stations.Seats[i].OperatedBy == tag.Seat && !list.Passengers[i].IsNull)
            {
                station = stations.Seats[i];
                weapon = list.Passengers[i];
                return true;
            }
        return false;
    }

    /// <summary>⭐ MS-5 — a seated man's eye in the world: his seat's place on the vehicle's pose (the type's <c>SeatDto</c>), up to the eyes;
    /// false when the type gives the seat no place.</summary>
    public static bool TryEye(EntityRepository w, Entity man, ITkbDatabase? tkb, out Vector3 eye)
    {
        eye = default;
        if (!Has<IsEmbarkedTag>(w, man)) return false;
        var tag = w.GetComponentRO<IsEmbarkedTag>(man);
        if (!Has<SimTransform>(w, tag.VehicleEntity) || Carriage.SeatOf(w, tag.VehicleEntity, tag.Seat, tkb) is not { HasPlace: true } seat) return false;
        var tf = w.GetComponentRO<SimTransform>(tag.VehicleEntity);
        var body = Matrix4x4.CreateFromQuaternion(tf.Rotation) * Matrix4x4.CreateTranslation(tf.Position);
        eye = Vector3.Transform(new Vector3(0f, 0f, EyeAboveHips), Carriage.InSeat(body, seat));
        return true;
    }

    /// <summary>⭐ MS-5 (LK-6) — <paramref name="yaw"/> / <paramref name="pitch"/> held inside <paramref name="station"/>'s arc, measured off the
    /// hull's heading <paramref name="hullYaw"/>; unchanged for a station that states no arc.</summary>
    public static (float Yaw, float Pitch) Clamp(in StationSeat station, float hullYaw, float yaw, float pitch)
    {
        if (station.HasArc == 0) return (yaw, pitch);
        float rel = Fdp.Toolkit.Perception.Look.LookOf.Wrap(yaw - hullYaw);
        rel = Math.Clamp(rel, station.TraverseMin, station.TraverseMax);
        return (hullYaw + rel, Math.Clamp(pitch, station.ElevationMin, station.ElevationMax));
    }

    private static bool Has<T>(EntityRepository w, Entity e) where T : unmanaged
        => w.IsComponentTypeRegistered<T>() && w.HasComponent<T>(e);
}
