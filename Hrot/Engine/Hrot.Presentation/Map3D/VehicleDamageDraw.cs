using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.BodyParts;
using Hrot.Look.Vehicles;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-4670</c> (vehicle appearance VA-S1; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6c) — how OUR 3D map draws a vehicle's partial
/// damage, from the ONE reader (<see cref="VehicleLook"/>): a kit part whose simulation part is Destroyed is drawn wreck-dark, and a Catastrophic kill throws the
/// turret and its gun off the hull. Pure; 0 B (a mask computed once per vehicle per frame). ⚠ Kit parts carry no simulation part id: a turret / gun part maps
/// by its role, a running-gear part (tracks, wheels — Hull role) by being low on one side; everything else is the hull.
/// </summary>
public static class VehicleDamageDraw
{
    public const byte HullBit = 1, TurretBit = 2, GunBit = 4, GearLeftBit = 8, GearRightBit = 16, EngineBit = 32;

    /// <summary>The simulation parts of <paramref name="vehicle"/> that look Destroyed, as a bit mask (<see cref="HullBit"/> …); 0 when undamaged.</summary>
    public static byte DestroyedMask(EntityRepository world, Entity vehicle)
    {
        if (!world.IsComponentTypeRegistered<PartDamage>() || !world.HasComponent<PartDamage>(vehicle)) return 0;
        byte mask = 0;
        if (VehicleLook.PartState(world, vehicle, VehiclePart.Hull) == PartLook.Destroyed) mask |= HullBit;
        if (VehicleLook.PartState(world, vehicle, VehiclePart.Turret) == PartLook.Destroyed) mask |= TurretBit;
        if (VehicleLook.PartState(world, vehicle, VehiclePart.Gun) == PartLook.Destroyed) mask |= GunBit;
        if (VehicleLook.PartState(world, vehicle, VehiclePart.RunningGearL) == PartLook.Destroyed) mask |= GearLeftBit;
        if (VehicleLook.PartState(world, vehicle, VehiclePart.RunningGearR) == PartLook.Destroyed) mask |= GearRightBit;
        if (VehicleLook.PartState(world, vehicle, VehiclePart.Engine) == PartLook.Destroyed) mask |= EngineBit;
        return mask;
    }

    /// <summary>The simulation part a kit part draws, as its bit.</summary>
    public static byte BitOf(in KitPart part) => part.Role switch
    {
        PartRole.Turret => TurretBit,
        PartRole.Gun => GunBit,
        PartRole.Hull when part.Centre.Z < 0.3f && part.Centre.Y >= 0.3f => GearLeftBit,    // a track / wheels, low on the left (+Y)
        PartRole.Hull when part.Centre.Z < 0.3f && part.Centre.Y <= -0.3f => GearRightBit,
        PartRole.Gear => (byte)(GearLeftBit | GearRightBit),
        _ => HullBit,
    };

    /// <summary>Is <paramref name="part"/> drawn destroyed under <paramref name="mask"/>?</summary>
    public static bool Destroyed(byte mask, in KitPart part) => (mask & BitOf(part)) != 0;

    /// <summary>Does a Catastrophic kill throw <paramref name="part"/> (the turret and its gun)?</summary>
    public static bool Thrown(in KitPart part) => part.Role is PartRole.Turret or PartRole.Gun;

    /// <summary>The body-space move of a thrown turret: tipped over onto the left side, beside the hull, lower (applied after the part's own transform).</summary>
    public static Matrix4x4 ThrownTransform(Vector3 size)
    {
        float ring = 0.62f * size.Z;
        return Matrix4x4.CreateTranslation(0f, 0f, -ring)
             * Matrix4x4.CreateRotationX(1.25f)
             * Matrix4x4.CreateTranslation(0f, 0.95f * size.Y, 0.35f * ring);
    }

    /// <summary>Is <paramref name="vehicle"/> a Catastrophic wreck (its turret drawn thrown)?</summary>
    public static bool TurretThrown(EntityRepository world, Entity vehicle) => VehicleLook.TurretThrown(world, vehicle);
}
