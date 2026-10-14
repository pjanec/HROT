using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-1081 … CE-1084 (docs/DESIGN_Map_3D_Mode.md §6m) — how a person is held, as the 3-D map draws him.</summary>
public enum CarriageKind : byte
{
    /// <summary>On his own feet (or in an enclosed seat, where nothing is drawn).</summary>
    Free = 0,
    /// <summary>On a carrier's shoulders (C-1).</summary>
    Carried,
    /// <summary>Dragged on his back behind a carrier (C-1).</summary>
    Dragged,
    /// <summary>Carrying a casualty on his shoulders (C-1).</summary>
    Carrying,
    /// <summary>Dragging a casualty behind him (C-1).</summary>
    Dragging,
    /// <summary>In an exposed seat that has a place (C-3).</summary>
    Seated,
    /// <summary>Down but alive: lying on his back (C-2).</summary>
    Downed,
}

/// <summary>⭐ CE-1081 … CE-1083 — the answer for one person: how he is held, by what (the carrier or the vehicle), the seat (when
/// seated), and whether he is WOUNDED (he limps — C-2; it combines with walking free or carrying).</summary>
public readonly record struct FigureCarriage(CarriageKind Kind, Entity Holder = default, SeatDto? Seat = null, bool Wounded = false)
{
    public static readonly FigureCarriage Free = new(CarriageKind.Free);
}

/// <summary>
/// ⭐⭐ CE-1081 … CE-1083 (docs/DESIGN_Map_3D_Mode.md §6m) — the ONE classifier of how a person is held, from facts every map host
/// already has: the embarked tag every node derives from the carrier's or vehicle's passenger list (<see cref="IsEmbarkedTag"/> —
/// carried / dragged / seated, exposed or not), the carrier's own list (he carries / drags), the seat's place in the TKB
/// (<see cref="SeatDto"/>) and the injury state derived from the replicated health (<see cref="Injury.StateOf(Fdp.ModuleHost.Abstractions.ISimulationView, Entity)"/>).
/// Pure — the rails call it without a GL context.
/// </summary>
public static class Carriage
{
    public static FigureCarriage Of(EntityRepository world, Entity e, ITkbDatabase? tkb)
    {
        bool wounded = world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && Injury.StateOf(world, e) == InjuryState.Wounded;
        if (world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(e))
        {
            var tag = world.GetComponentRO<IsEmbarkedTag>(e);
            var holder = tag.VehicleEntity;
            if (world.IsAlive(holder))
            {
                if (world.IsComponentTypeRegistered<PersonCarrier>() && Embarkation.IsPersonCarrier(world, holder))
                    return tag.Mode switch
                    {
                        CarryMode.Carried => new FigureCarriage(CarriageKind.Carried, holder),
                        CarryMode.Dragged => new FigureCarriage(CarriageKind.Dragged, holder),
                        _ => new FigureCarriage(CarriageKind.Free, Wounded: wounded),
                    };
                if (tag.Exposed != 0 && SeatOf(world, holder, tag.Seat, tkb) is { HasPlace: true } seat)
                    return new FigureCarriage(CarriageKind.Seated, holder, seat, wounded);
            }
        }
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && Injury.StateOf(world, e) == InjuryState.Incapacitated)
            return new FigureCarriage(CarriageKind.Downed);
        if (world.IsComponentTypeRegistered<PassengerBuffer>() && world.HasComponent<PassengerBuffer>(e)
            && world.IsComponentTypeRegistered<PersonCarrier>() && Embarkation.IsPersonCarrier(world, e))
        {
            var list = world.GetComponentRO<PassengerBuffer>(e);
            if (list.Count > 0)
                return list.ModeOf(0) == CarryMode.Dragged
                    ? new FigureCarriage(CarriageKind.Dragging, Wounded: wounded)
                    : new FigureCarriage(CarriageKind.Carrying, Wounded: wounded);
        }
        return wounded ? new FigureCarriage(CarriageKind.Free, Wounded: true) : FigureCarriage.Free;
    }

    /// <summary>Seat <paramref name="seat"/> of <paramref name="vehicle"/>'s type (its <c>Gen.Carrier</c>), or null.</summary>
    public static SeatDto? SeatOf(EntityRepository world, Entity vehicle, int seat, ITkbDatabase? tkb)
    {
        if (tkb == null || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.HasComponent<TkbIdentity>(vehicle)) return null;
        if (!tkb.TryGetByType(world.GetComponentRO<TkbIdentity>(vehicle).TkbType, out var template) || template == null) return null;
        var seats = template.GetDescriptor<CarrierDefDto>()?.Seats;
        return seats != null && seat >= 0 && seat < seats.Count ? seats[seat] : null;
    }

    /// <summary>⭐ CE-1081 (C-1) — where a carried man's middle rests on his carrier: on the shoulders (metres up for a 1.8 m carrier),
    /// a little behind the neck.</summary>
    public const float ShoulderHeight = 1.50f, ShoulderBack = 0.05f;

    /// <summary>
    /// ⭐ CE-1081 (C-1) — the carried man's figure frame in the WORLD: crosswise on <paramref name="carrierBody"/>'s shoulders (his own
    /// +X — toward his head — along the carrier's +Y, so his head hangs at the carrier's left), at the carrier's shoulder height scaled
    /// to the carrier's size.
    /// </summary>
    public static Matrix4x4 OnShoulders(Matrix4x4 carrierBody, float carrierHeight)
        => Matrix4x4.CreateRotationZ(System.MathF.PI / 2f)
           * Matrix4x4.CreateTranslation(-ShoulderBack, 0f, ShoulderHeight / 1.8f * carrierHeight)
           * carrierBody;

    /// <summary>⭐ CE-1083 (C-3) — a rider's figure frame in the WORLD: at the seat's place in <paramref name="vehicleBody"/>, facing its
    /// way. The seated figure's hips are its origin.</summary>
    public static Matrix4x4 InSeat(Matrix4x4 vehicleBody, SeatDto seat)
        => Matrix4x4.CreateRotationZ(seat.FacingDegrees * System.MathF.PI / 180f)
           * Matrix4x4.CreateTranslation(seat.X, seat.Y, seat.Z)
           * vehicleBody;
}

/// <summary>
/// ⭐ CE-1084 (docs/DESIGN_Map_3D_Mode.md §6m C-4) — the JUMP DOWN, a drawing: while a man rides an exposed seat the layer tells it where
/// his hips are in the world; the first frame he is free again, if they were more than <see cref="MinDrop"/> above where his standing
/// hips now are, he is drawn that much higher, falling (in free fall, at least <see cref="MinSeconds"/>), and lands crouched for
/// <see cref="LandSeconds"/>. A man who was not seen riding just before (a jump in time) simply stands there.
/// </summary>
public sealed class DismountDrop
{
    /// <summary>Drops shorter than this are a step down, not a jump (m).</summary>
    public const float MinDrop = 0.5f;
    /// <summary>The shortest fall drawn, and how long he stays crouched after landing (s, sim time).</summary>
    public const float MinSeconds = 0.3f, LandSeconds = 0.3f;
    /// <summary>A ride further back than this was not seen ending (s).</summary>
    public const double SeenWithin = 0.5;

    private readonly Dictionary<Entity, (double At, float HipsZ)> _riding = new();
    private readonly Dictionary<Entity, (double At, float Height)> _drops = new();
    private readonly List<Entity> _gone = new();

    /// <summary>He rides this frame, his hips at <paramref name="hipsZ"/> (world).</summary>
    public void Riding(Entity e, float hipsZ, double now)
    {
        _riding[e] = (now, hipsZ);
        _drops.Remove(e);
    }

    /// <summary>He is free this frame, his standing hips at <paramref name="standingHipsZ"/>: how much higher to draw him (m) and whether
    /// he is in the jump (falling or just landed — drawn crouched).</summary>
    public (float Lift, bool Jumping) Free(Entity e, float standingHipsZ, double now)
    {
        if (_riding.Remove(e, out var ride) && now >= ride.At && now - ride.At <= SeenWithin)
        {
            float height = ride.HipsZ - standingHipsZ;
            if (height > MinDrop) _drops[e] = (now, height);
        }
        if (!_drops.TryGetValue(e, out var drop)) return (0f, false);
        float fall = FallSeconds(drop.Height);
        float t = (float)((now - drop.At) / fall);
        if (t < 1f) return (drop.Height * (1f - t * t), true);   // free fall: the height left goes as 1 − t²
        if (now - drop.At <= fall + LandSeconds) return (0f, true);
        _drops.Remove(e);
        return (0f, false);
    }

    /// <summary>How long a fall of <paramref name="height"/> metres takes (free fall, at least <see cref="MinSeconds"/>).</summary>
    public static float FallSeconds(float height) => System.MathF.Max(MinSeconds, System.MathF.Sqrt(2f * System.MathF.Max(0f, height) / 9.81f));

    /// <summary>Forgets everyone not drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        _gone.Clear();
        foreach (var e in _riding.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _drops.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) { _riding.Remove(e); _drops.Remove(e); }
    }
}
