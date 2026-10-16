using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Physics.Components;
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
    /// <summary>⭐ CE-1088 (§6n N-2) — in a weapon system's own <c>Gunner</c> seat: at his weapon (the holder is the weapon).</summary>
    Manning,
    /// <summary>⭐ CE-1104 (§6t) — on a stretcher two men carry (the holder is the front man, the partner the rear one).</summary>
    OnStretcher,
    /// <summary>⭐ CE-1104 — at a stretcher's front handles (the partner is the rear man).</summary>
    StretcherFront,
    /// <summary>⭐ CE-1104 — at a stretcher's rear handles (the holder is the front man).</summary>
    StretcherRear,
    /// <summary>⭐ CE-1104 — carrying a two-man weapon at its front (the load; the partner is the rear man).</summary>
    TeamFront,
    /// <summary>⭐ CE-1104 — carrying a two-man weapon at its rear (the holder is the front man, the load the weapon).</summary>
    TeamRear,
}

/// <summary>⭐ CE-1081 … CE-1083 — the answer for one person: how he is held, by what (the carrier or the vehicle), the seat (when
/// seated), and whether he is WOUNDED (he limps — C-2; it combines with walking free or carrying). ⭐ CE-1085 (§6n N-3) — and the
/// weapon system he CARRIES in his carry station (<paramref name="Load"/>; null when none), which combines with any of his own kinds.</summary>
/// ⭐ CE-1104 (§6t) — and the other man of a pair carrying one load (<paramref name="Partner"/>).
public readonly record struct FigureCarriage(CarriageKind Kind, Entity Holder = default, SeatDto? Seat = null, bool Wounded = false,
                                             Entity Load = default, Entity Partner = default)
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
                        CarryMode.Stretcher => new FigureCarriage(CarriageKind.OnStretcher, holder, Partner: AssistantOf(world, holder)),   // ⭐ CE-1104
                        _ => new FigureCarriage(CarriageKind.Free, Wounded: wounded),
                    };
                // ⭐ CE-1088 (§6n N-2) — a man in a weapon system's own seat mans it (a tripod HMG, a mortar): drawn AT the weapon
                if (WeaponSystems.IsWeaponSystem(world, holder))
                    return new FigureCarriage(CarriageKind.Manning, holder, SeatOf(world, holder, tag.Seat, tkb), wounded);
                if (tag.Exposed != 0 && SeatOf(world, holder, tag.Seat, tkb) is { HasPlace: true } seat)
                    return new FigureCarriage(CarriageKind.Seated, holder, seat, wounded);
            }
        }
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && Injury.StateOf(world, e) == InjuryState.Incapacitated)
            return new FigureCarriage(CarriageKind.Downed);
        // ⭐ CE-1164 (civilian U-3, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U3-a) — KNOCKED DOWN (a baton round, a shove: the stored
        //   EffectState, CE-3219) lies on the ground like a man down, at full health; he gets up when the effect runs out
        if (Fdp.Toolkit.Combat.Effects.IsKnockedDown(world, e))
            return new FigureCarriage(CarriageKind.Downed);
        // ⭐ CE-1104 (§6t) — the SECOND man of a team carry: at the rear of what his carrier carries (a stretcher, or a two-man weapon)
        if (world.IsComponentTypeRegistered<IsAssistingTag>() && world.HasComponent<IsAssistingTag>(e))
        {
            var front = world.GetComponentRO<IsAssistingTag>(e).Carrier;
            if (world.IsAlive(front) && world.HasComponent<PassengerBuffer>(front))
            {
                var (stretcher, weapon) = TeamLoadOf(world, front);
                if (stretcher) return new FigureCarriage(CarriageKind.StretcherRear, front, Wounded: wounded);
                if (!weapon.IsNull) return new FigureCarriage(CarriageKind.TeamRear, front, Wounded: wounded, Load: weapon);
            }
        }
        if (world.IsComponentTypeRegistered<PassengerBuffer>() && world.HasComponent<PassengerBuffer>(e)
            && world.IsComponentTypeRegistered<PersonCarrier>() && Embarkation.IsPersonCarrier(world, e))
        {
            // ⭐ CE-1085 (§6n N-3) — his casualty seat and his carry station are read APART: a weapon system in his list is the LOAD he
            //   carries; only a man in it makes him a carrier. (CE-1081 read seat 0's mode whenever the list was not empty, so a soldier
            //   with only a Javelin in his carry station was drawn carrying a casualty.)
            var list = world.GetComponentRO<PassengerBuffer>(e);
            Entity load = default;
            int casualty = -1;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var p = list.Passengers[i];
                if (p.IsNull || !world.IsAlive(p)) continue;
                if (WeaponSystems.IsWeaponSystem(world, p)) { if (load.IsNull) load = p; }
                else if (casualty < 0) casualty = i;
            }
            bool paired = !list.Assistant.IsNull && world.IsAlive(list.Assistant);
            if (casualty >= 0)
                return list.ModeOf(casualty) switch
                {
                    CarryMode.Dragged => new FigureCarriage(CarriageKind.Dragging, Wounded: wounded, Load: load),
                    CarryMode.Stretcher => new FigureCarriage(CarriageKind.StretcherFront, Wounded: wounded, Load: load, Partner: list.Assistant),   // ⭐ CE-1104
                    _ => new FigureCarriage(CarriageKind.Carrying, Wounded: wounded, Load: load),
                };
            if (!load.IsNull && paired && Embarkation.IsTwoManWeapon(world, load))   // ⭐ CE-1104 — a two-man weapon, the pair at it
                return new FigureCarriage(CarriageKind.TeamFront, Wounded: wounded, Load: load, Partner: list.Assistant);
            if (!load.IsNull) return new FigureCarriage(CarriageKind.Free, Wounded: wounded, Load: load);
        }
        return wounded ? new FigureCarriage(CarriageKind.Free, Wounded: true) : FigureCarriage.Free;
    }

    /// <summary>⭐ CE-1104 — the second man <paramref name="carrier"/> names (null when none).</summary>
    public static Entity AssistantOf(EntityRepository world, Entity carrier)
        => world.IsComponentTypeRegistered<PassengerBuffer>() && world.HasComponent<PassengerBuffer>(carrier)
            ? world.GetComponentRO<PassengerBuffer>(carrier).Assistant : default;

    /// <summary>⭐ CE-1104 — what a team carrier carries with his assistant: a stretcher (a casualty in <see cref="CarryMode.Stretcher"/>), or
    /// the two-man weapon in his carry station (null when none).</summary>
    private static (bool Stretcher, Entity Weapon) TeamLoadOf(EntityRepository world, Entity carrier)
    {
        var list = world.GetComponentRO<PassengerBuffer>(carrier);
        Entity weapon = default;
        for (int i = 0; i < PassengerBuffer.Capacity; i++)
        {
            var p = list.Passengers[i];
            if (p.IsNull || !world.IsAlive(p)) continue;
            if (WeaponSystems.IsWeaponSystem(world, p)) { if (weapon.IsNull && Embarkation.IsTwoManWeapon(world, p)) weapon = p; }
            else if (list.ModeOf(i) == CarryMode.Stretcher) return (true, default);
        }
        return (false, weapon);
    }

    /// <summary>⭐ CE-1104 (§6t T-b1) — how high the litter (and a two-man weapon) is held: the carriers' hands, for 1.8 m men.</summary>
    public const float HandleHeight = 0.80f;

    /// <summary>
    /// ⭐ CE-1104 (§6t T-b1) — the frame a team load is drawn in: MIDWAY between the front man and his partner, at <see cref="HandleHeight"/>
    /// above the ground under them, facing the front man's way. With no partner placed, at the front man's place.
    /// </summary>
    public static Matrix4x4 TeamFrame(in SimTransform front, in SimTransform? rear)
    {
        var at = rear is { } r ? (front.Position + r.Position) / 2f : front.Position;
        return Matrix4x4.CreateFromQuaternion(front.Rotation) * Matrix4x4.CreateTranslation(at + new Vector3(0f, 0f, HandleHeight));
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
