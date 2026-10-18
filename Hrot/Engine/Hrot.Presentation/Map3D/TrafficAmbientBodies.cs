using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Hrot.Traffic;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ AQ93 §15.8 (CE-4031) — SUMO's agents that are NOT entities (far from our units, R-4020), as bodies the map draws exactly as ours
/// (<see cref="EntityBodyLayer3D.AmbientBody"/>): a pedestrian is the civilian his id types him as (<see cref="TrafficReplicas.PedestrianTypeOf"/>
/// — the type his replica and the civilian a conversion makes also have), a car its TKB type (<see cref="TrafficReplicas.CarTypeOf"/>) in
/// its own paint; each keyed by its network id, the seed of his clothes and face or its paint. Read from <see cref="TrafficFrame"/>, which
/// every node with the SUMO mirror writes; a node without it has none.
/// </summary>
public static class TrafficAmbientBodies
{
    [System.ThreadStatic] private static List<EntityBodyLayer3D.AmbientBody>? t_bodies;

    /// <summary>The bodies of this frame. ⚠ The list is reused by the next call on this thread — read it before asking again.</summary>
    public static IReadOnlyList<EntityBodyLayer3D.AmbientBody> Of(EntityRepository world)
    {
        var bodies = t_bodies ??= new List<EntityBodyLayer3D.AmbientBody>();
        bodies.Clear();
        if (TrafficFrame.Of(world) is not { } frame) return bodies;
        foreach (var ped in frame.Pedestrians)
            if (!frame.IsPedEntity(ped))
                bodies.Add(new(KeyOf(ped.NetworkId, ped.Name), TrafficReplicas.PedestrianTypeOf(ped.NetworkId), new Vector3(ped.X, ped.Y, ped.Z), ped.YawRad));
        foreach (var car in frame.Cars)
            if (!frame.IsEntity(car))
                bodies.Add(new(KeyOf(car.NetworkId, car.Id), TrafficReplicas.CarTypeOf(car), new Vector3(car.X, car.Y, car.Z), car.YawRad, car.Color));
        return bodies;
    }

    /// <summary>The network id; for an agent its sender named otherwise (id 0), a stable hash of its name (FNV-1a — the same on every node,
    /// unlike <see cref="string.GetHashCode()"/>), negative so it never meets a real id.</summary>
    public static long KeyOf(long networkId, string name)
    {
        if (networkId != 0) return networkId;
        ulong h = 14695981039346656037UL;
        foreach (char c in name) { h ^= c; h *= 1099511628211UL; }
        return -(long)(h >> 1) - 1;
    }
}
