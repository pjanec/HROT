using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4038 — marks an entity as the local REPLICA of one SUMO car, keyed by SumoSharp's vehicle handle. The mirror
/// (<see cref="TrafficReplicas"/>) creates it when the car comes within reach of an HROT body, poses it every frame and destroys it
/// when the car leaves the band or SUMO retires it. Never saved, never replayed: the car is SUMO's, not the scenario's.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.TrafficCar)]
[DataPolicy(DataPolicy.Transient)]
public struct TrafficCar
{
    /// <summary>SumoSharp's <c>VehicleHandle.Index</c>.</summary>
    public uint VehicleIndex;

    /// <summary>SumoSharp's <c>VehicleHandle.Generation</c> (a recycled slot is a different car).</summary>
    public ushort Generation;

    /// <summary>⭐ AQ93 §15 K3 — <see cref="KindCar"/>, or <see cref="KindPedestrian"/> (then <see cref="VehicleIndex"/> is SumoSharp's ped
    /// id and <see cref="Generation"/> 0).</summary>
    public byte Kind;

    public const byte KindCar = 0;
    public const byte KindPedestrian = 1;
}
