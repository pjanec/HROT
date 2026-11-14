using System.Numerics;
using Fdp.Toolkit.Combat.Components;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1236 (docs/DESIGN_People_Display.md §5 U7a, R-1022) — the soldier's weapon drawn as what it is (<see cref="HeldLook"/>): the figure
/// keeps its parts and their order; the weapon part is reshaped along the rifle's line (as <see cref="Shouldered"/> already did for the
/// aimed launcher, which it still is for <see cref="HeldLook.MissileTube"/>), a pistol and a mortar move his hands, and the small things a
/// look has (a scope, a magazine box, a warhead, a launcher slung on his back) are part DETAILS — boxes in a part's own frame, like the
/// outfit's (§6q).
/// </summary>
public static partial class BlockFigure
{
    /// <summary>Lengths (m): a machine gun, a scoped rifle, a pistol, an RPG tube, a MANPADS tube.</summary>
    public const float MachineGunLength = 1.05f, ScopedRifleLength = 1.12f, PistolLength = 0.22f, RpgLength = 1.0f, ManpadsLength = 1.5f;

    /// <summary>Diameters (m): an RPG tube, a MANPADS tube.</summary>
    public const float RpgDiameter = 0.08f, ManpadsDiameter = 0.09f;

    /// <summary>
    /// <paramref name="parts"/> with its weapon drawn as <paramref name="look"/> in <paramref name="posture"/>. <paramref name="handsAt"/>
    /// (body metres) is where a <see cref="HeldLook.Mortar"/>'s hands go while it is fired (over its tube's mouth); null at ease (hanging).
    /// A figure with no weapon part, and the rifle, are returned as they are.
    /// </summary>
    public static KitPart[] Held(KitPart[] parts, HeldLook look, WeaponPostureKind posture, Vector3? handsAt = null)
    {
        if (parts.Length <= WeaponIndex || look == HeldLook.Rifle) return parts;
        if (look == HeldLook.MissileTube && posture == WeaponPostureKind.Aimed) return Shouldered(parts);   // CE-1090 N-4, unchanged
        var held = (KitPart[])parts.Clone();
        var rifle = parts[WeaponIndex];
        var (stock, muzzle) = Ends(rifle);
        var dir = Vector3.Normalize(muzzle - stock);
        switch (look)
        {
            case HeldLook.MachineGun:
                held[WeaponIndex] = Reshaped(rifle, stock, stock + dir * MachineGunLength, 0.07f, 0.11f);
                break;
            case HeldLook.ScopedRifle:
                held[WeaponIndex] = Reshaped(rifle, stock - dir * 0.04f, stock + dir * (ScopedRifleLength - 0.04f), 0.05f, 0.08f);
                break;
            case HeldLook.Pistol:
            {
                // both hands on it, the arms posed from his shoulders by the posture. 🔒 User, 2026-10-30: "a pistol is usually held with both
                //   hands forward from the body if aiming and both hands lowered (but still holding the pistol) if weapon at ease" — ⛔ was: the
                //   pistol where the rifle's grip was, his hands on his chest in every posture
                var shoulderL = Ends(parts[6]).A;
                var shoulderR = Ends(parts[8]).A;
                var chest = (shoulderL + shoulderR) / 2f;
                bool prone = chest.Z < 0.5f;   // on his front there is no room to lower the arms: the hands stay forward
                Vector3 grip, aim;
                switch (prone ? WeaponPostureKind.Aimed : posture)
                {
                    case WeaponPostureKind.Aimed:                                        // arms out, the hands at shoulder height, level
                        grip = chest + new Vector3(0.52f, 0f, -0.02f);
                        aim = Vector3.UnitX;
                        break;
                    case WeaponPostureKind.Ready:                                        // drawn in, in front of the chest, pointing forward
                        grip = chest + new Vector3(0.36f, 0f, -0.14f);
                        aim = Vector3.UnitX;
                        break;
                    default:                                                             // at ease: both hands lowered in front, muzzle down
                        grip = chest + new Vector3(0.24f, 0f, -0.42f);
                        aim = Vector3.Normalize(new Vector3(1f, 0f, -1f));
                        break;
                }
                held[WeaponIndex] = Reshaped(rifle, grip - aim * 0.04f, grip + aim * (PistolLength - 0.04f), 0.04f, 0.12f);
                for (int side = 0; side < 2; side++)
                {
                    int upper = side == 0 ? 6 : 8;
                    float y = side == 0 ? 1f : -1f;
                    var shoulder = side == 0 ? shoulderL : shoulderR;
                    var hand = grip + new Vector3(-0.01f, 0.025f * y, 0f);   // the two hands clasped round the grip
                    var reach = hand - shoulder;
                    if (reach.Length() > ArmReach) hand = shoulder + Vector3.Normalize(reach) * ArmReach;
                    var elbow = (shoulder + hand) / 2f + new Vector3(0f, 0.07f * y, -0.06f);   // a little out and down
                    held[upper] = Reshaped(parts[upper], shoulder, elbow, ArmWidth, ArmWidth);
                    held[upper + 1] = Reshaped(parts[upper + 1], elbow, hand, ArmWidth * 0.9f, ArmWidth * 0.9f);
                }
                break;
            }
            case HeldLook.Rpg or HeldLook.Manpads or HeldLook.MissileTube:
            {
                float length = look switch { HeldLook.Rpg => RpgLength, HeldLook.Manpads => ManpadsLength, _ => TubeLength };
                float diameter = look switch { HeldLook.Rpg => RpgDiameter, HeldLook.Manpads => ManpadsDiameter, _ => TubeDiameter };
                // aimed: on the shoulder, a third behind it (N-4's placing); otherwise along the rifle's line, held at its middle
                var centre = posture == WeaponPostureKind.Aimed
                    ? (stock + muzzle) / 2f - dir * 0.18f + new Vector3(0f, 0f, 0.06f)
                    : stock + dir * (length * 0.40f);
                held[WeaponIndex] = rifle with
                {
                    Shape = PartShape.Cylinder, Axis = PartAxis.X, Centre = centre / S,
                    Extent = new Vector3(length, 0f, diameter) / S, Colour = ColourRole.Body,
                };
                break;
            }
            case HeldLook.Mortar:
            {
                // no weapon in his hands (the tube stands on the ground, or rides his back): fired, his hands over the tube's mouth; at
                // ease they hang
                held[WeaponIndex] = rifle with { Extent = Vector3.Zero };
                for (int side = 0; side < 2; side++)
                {
                    int upper = side == 0 ? 6 : 8;
                    float y = side == 0 ? 1f : -1f;
                    var shoulder = Ends(parts[upper]).A;
                    Vector3 elbow, hand;
                    if (handsAt is { } at && posture != WeaponPostureKind.AtEase)
                    {
                        hand = at + new Vector3(0f, 0.05f * y, 0f);
                        var reach = hand - shoulder;
                        if (reach.Length() > ArmReach) hand = shoulder + Vector3.Normalize(reach) * ArmReach;
                        elbow = (shoulder + hand) / 2f + new Vector3(0f, 0.10f * y, -0.06f);
                    }
                    else
                    {
                        elbow = shoulder + new Vector3(0f, 0.02f * y, -0.30f);
                        hand = shoulder + new Vector3(0.02f, 0.02f * y, -0.60f);
                    }
                    held[upper] = Reshaped(parts[upper], shoulder, elbow, ArmWidth, ArmWidth);
                    held[upper + 1] = Reshaped(parts[upper + 1], elbow, hand, ArmWidth * 0.9f, ArmWidth * 0.9f);
                }
                break;
            }
        }
        return held;
    }

    /// <summary>How far a hand reaches from its shoulder (m).</summary>
    public const float ArmReach = 0.62f;

    /// <summary>
    /// The details of <paramref name="inHands"/> on the weapon part and of <paramref name="slung"/> on the torso, appended to
    /// <paramref name="into"/>: a machine gun's box magazine and folded bipod, a scope, an RPG's warhead, a MANPADS's grip box and seeker
    /// cap, a missile tube's sight; a launcher or a mortar he is not using rides upright on his back.
    /// </summary>
    public static void WeaponDetails(HeldLook inHands, HeldLook slung, WeaponPostureKind posture, List<PartDetail> into)
    {
        var metal = new Color(40, 42, 36, 255);
        var olive = new Color(78, 88, 60, 255);
        switch (inHands)
        {
            // a box part runs along its x (-0.5 stock … 0.5 muzzle); y across, z up — in fractions of the part
            case HeldLook.MachineGun:
                into.Add(new PartDetail(WeaponIndex, new(-0.06f, 0f, -0.95f), new(0.13f, 1.5f, 1.1f), olive));    // the box magazine
                into.Add(new PartDetail(WeaponIndex, new(0.36f, 0f, -0.70f), new(0.24f, 0.45f, 0.35f), metal));   // the bipod, folded
                break;
            case HeldLook.ScopedRifle:
                into.Add(new PartDetail(WeaponIndex, new(-0.02f, 0f, 1.05f), new(0.30f, 1.0f, 0.75f), metal));    // the scope
                break;
            // a tube runs along its unit z (0 rear … 1 muzzle); its x is DOWN (−x up), y across, in diameters
            case HeldLook.Rpg:
                into.Add(new PartDetail(WeaponIndex, new(0f, 0f, 1.10f), new(1.7f, 1.7f, 0.24f), olive));          // the warhead
                into.Add(new PartDetail(WeaponIndex, new(0.85f, 0f, 0.48f), new(1.1f, 0.6f, 0.10f), metal));      // the grip
                break;
            case HeldLook.Manpads:
                into.Add(new PartDetail(WeaponIndex, new(0.95f, 0f, 0.62f), new(1.1f, 0.8f, 0.10f), metal));      // the grip stock / battery
                into.Add(new PartDetail(WeaponIndex, new(0f, 0f, 1.01f), new(1.15f, 1.15f, 0.03f), metal));       // the seeker cap
                break;
            case HeldLook.MissileTube when posture != WeaponPostureKind.Aimed:
                into.Add(new PartDetail(WeaponIndex, new(-0.15f, 0.70f, 0.55f), new(0.8f, 0.55f, 0.20f), metal)); // the sight
                break;
        }
        if (!HeldWeapons.IsSlungShown(slung) && !(inHands == HeldLook.Mortar && posture == WeaponPostureKind.AtEase)) return;
        // on his back, upright, sticking out above his shoulder (the torso box: −x is his back, z up)
        bool mortar = slung == HeldLook.Mortar || inHands == HeldLook.Mortar;
        into.Add(new PartDetail(TorsoIndex, new(-0.66f, -0.18f, 0.30f), new(mortar ? 0.36f : 0.30f, mortar ? 0.24f : 0.20f, 1.65f), olive));
        if (mortar)   // its baseplate, at the bottom of the pack
            into.Add(new PartDetail(TorsoIndex, new(-0.62f, 0f, -0.30f), new(0.12f, 0.95f, 0.55f), metal));
    }

    /// <summary>⭐ CE-1173 (U12) — a toy's orange; ⭐ CE-1174 (U13) — a less-lethal weapon's yellow band.</summary>
    public static readonly Color ToyOrange = new(255, 120, 20, 255), LessLethalYellow = new(245, 205, 30, 255);

    /// <summary>
    /// ⭐ CE-1173 / CE-1174 — the held weapon's marking as a detail on the weapon part: a toy covered in orange (a shell a hair larger than
    /// the part), a less-lethal weapon a yellow band a third of the way from its muzzle. None adds nothing.
    /// </summary>
    public static void MarkingDetail(HeldMarking marking, List<PartDetail> into)
    {
        if (marking == HeldMarking.Toy) into.Add(new PartDetail(WeaponIndex, Vector3.Zero, new Vector3(1.04f, 1.12f, 1.12f), ToyOrange));
        else if (marking == HeldMarking.LessLethal) into.Add(new PartDetail(WeaponIndex, new(0.18f, 0f, 0f), new(0.12f, 1.25f, 1.25f), LessLethalYellow));
    }

    /// <summary>The two ends (body metres) of a part drawn by <see cref="Limb"/>: A its start, B its end.</summary>
    public static (Vector3 A, Vector3 B) Ends(in KitPart part)
    {
        float p = part.PitchDegrees * MathF.PI / 180f, y = part.YawDegrees * MathF.PI / 180f;
        var dir = new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
        var centre = part.Centre * S;
        float half = part.Extent.X * S.X / 2f;
        return (centre - dir * half, centre + dir * half);
    }

    /// <summary><paramref name="part"/> redrawn as a box from <paramref name="a"/> to <paramref name="b"/> — its role, pivots and colour kept.</summary>
    private static KitPart Reshaped(in KitPart part, Vector3 a, Vector3 b, float w, float h)
    {
        var limb = Limb(a, b, w, h, part.Colour);
        return part with
        {
            Shape = PartShape.Box, Centre = limb.Centre, Extent = limb.Extent, PitchDegrees = limb.PitchDegrees, YawDegrees = limb.YawDegrees,
        };
    }
}
