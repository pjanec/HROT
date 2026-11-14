using System.Numerics;
using Fdp.Toolkit.Combat.Components;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1169</c> (docs/DESIGN_People_Display.md §5 U7) — the POCKET ITEMS and the THROWABLE in a man's hand, from the replicated
/// <see cref="WeaponPosture.HeldItem"/> (written by behaviors: the phone in a civilian's routine and the IED trigger, a stone in his hand,
/// the operator's binoculars): the phone to his ear while he stands, in his hand in front of him while he walks; the binoculars raised to
/// his eyes in both hands; a stone or grenade held low in the right hand, ready. A soldier's rifle is slung meanwhile. Only the four arm
/// parts change (the legs keep their gait), the shape of <see cref="GestureArms"/>.
/// </summary>
public static partial class BlockFigure
{
    /// <summary>A phone (m): long, wide, thick.</summary>
    public const float PhoneLength = 0.14f, PhoneWidth = 0.07f, PhoneThickness = 0.02f;

    /// <summary>Binoculars (m): across, deep (toward his eyes), high.</summary>
    public const float BinocularsWidth = 0.17f, BinocularsDepth = 0.14f, BinocularsHeight = 0.07f;

    /// <summary>The items <see cref="HeldItemArms"/> draws (a held WEAPON is <see cref="Held"/>'s, None is nothing).</summary>
    public static bool IsPocketOrThrowable(HeldItemKind item) => item is HeldItemKind.Phone or HeldItemKind.Binoculars or HeldItemKind.Throwable;

    /// <summary>
    /// <paramref name="parts"/> with the arms holding <paramref name="item"/>: the phone (a dark slab) to the ear, or in front while
    /// <paramref name="walking"/>; the binoculars (a dark box) at the eyes; a throwable's hand low and ready (its round is a detail,
    /// <see cref="ThrowableDetail"/>). Anything else is returned as it is.
    /// </summary>
    public static KitPart[] HeldItemArms(KitPart[] parts, HeldItemKind item, bool crouched, bool soldier, bool walking)
    {
        if (!IsPocketOrThrowable(item) || parts.Length < TorsoIndex + 2 + 4) return parts;
        var drop = crouched ? CrouchDrop : Vector3.Zero;
        var l = StandShoulderL + drop; var r = StandShoulderR + drop;
        var hangL = Arm(l, l + new Vector3(0f, 0.02f, -0.30f), l + new Vector3(0.02f, 0.02f, -0.60f), PartRole.Hull);
        Vector3 handR, handL = default;
        KitPart[] arms;
        switch (item)
        {
            case HeldItemKind.Phone when !walking:
                handR = new Vector3(0.06f, -0.14f, 1.58f) + drop;                            // at his ear, the elbow out and down
                arms = [.. hangL, .. Arm(r, r + new Vector3(0.16f, -0.08f, -0.16f), handR, PartRole.Hull)];
                break;
            case HeldItemKind.Phone:
                handR = new Vector3(0.30f, -0.10f, 1.12f) + drop;                            // in front of him, looked at
                arms = [.. hangL, .. Arm(r, r + new Vector3(0.06f, -0.02f, -0.26f), handR, PartRole.Hull)];
                break;
            case HeldItemKind.Binoculars:
                handL = new Vector3(0.18f, 0.06f, 1.62f) + drop;                             // both hands at his eyes, the elbows out
                handR = new Vector3(0.18f, -0.06f, 1.62f) + drop;
                arms = [.. Arm(l, l + new Vector3(0.12f, 0.06f, -0.14f), handL, PartRole.Hull),
                        .. Arm(r, r + new Vector3(0.12f, -0.06f, -0.14f), handR, PartRole.Hull)];
                break;
            default:                                                                          // a stone / grenade, low and ready
                handR = new Vector3(0.22f, -0.26f, 1.02f) + drop;
                arms = [.. hangL, .. Arm(r, r + new Vector3(0.04f, -0.02f, -0.28f), handR, PartRole.Hull)];
                break;
        }
        var shaped = (KitPart[])parts.Clone();
        for (int i = 0; i < 4; i++) shaped[TorsoIndex + 2 + i] = arms[i];
        if (soldier && shaped.Length > WeaponIndex)
            shaped[WeaponIndex] = Limb(new Vector3(-0.16f, 0.22f, 1.55f) + drop, new Vector3(-0.14f, -0.24f, 0.85f) + drop, 0.05f, 0.08f, ColourRole.Metal);   // slung
        return item switch
        {
            HeldItemKind.Phone when !walking => [.. shaped, Limb(handR + new Vector3(0f, 0f, -0.08f), handR + new Vector3(0.02f, 0f, 0.06f), PhoneWidth, PhoneThickness, ColourRole.Dark)],
            HeldItemKind.Phone => [.. shaped, Limb(handR, handR + new Vector3(0.04f, 0f, 0.13f), PhoneWidth, PhoneThickness, ColourRole.Dark)],
            HeldItemKind.Binoculars => [.. shaped, Limb((handL + handR) / 2f + new Vector3(-0.07f, 0f, 0.01f), (handL + handR) / 2f + new Vector3(0.07f, 0f, 0.01f),
                                                        BinocularsWidth, BinocularsHeight, ColourRole.Dark)],
            _ => shaped,
        };
    }

    /// <summary>⭐ <c>CE-1179</c> (People Display §5 U18) — <paramref name="parts"/> with the weapon part LAID on the ground at his feet, a
    /// step in front of him and across his path, its length kept; his arms are left to whoever shapes them (a surrender's hands up).</summary>
    public static KitPart[] LaidAtFeet(KitPart[] parts)
    {
        if (parts.Length <= WeaponIndex) return parts;
        var weapon = parts[WeaponIndex];
        var (a, b) = Ends(weapon);
        float half = MathF.Max(0.1f, Vector3.Distance(a, b) / 2f);
        var laid = (KitPart[])parts.Clone();
        laid[WeaponIndex] = Limb(new Vector3(0.45f, half, 0.04f), new Vector3(0.45f, -half, 0.04f), 0.05f, 0.08f, weapon.Colour);
        return laid;
    }

    /// <summary>⭐ CE-1172 (People Display U11) — a police baton (m): long and thick.</summary>
    public const float BatonLength = 0.60f, BatonThickness = 0.035f;

    /// <summary>⭐ CE-1172 — <paramref name="parts"/> with a baton in the right hand, hanging down along his thigh, a little forward.</summary>
    public static KitPart[] BatonInHand(KitPart[] parts)
    {
        int fore = TorsoIndex + 5;
        if (parts.Length <= fore) return parts;
        var (_, hand) = Ends(parts[fore]);
        return [.. parts, Limb(hand + new Vector3(0.02f, 0f, 0.04f), hand + new Vector3(0.14f, -0.02f, 0.04f - BatonLength), BatonThickness, BatonThickness, ColourRole.Dark)];
    }

    /// <summary>⭐ CE-1172 (round-out of U11) — a riot shield (m): wide, tall, thick.</summary>
    public const float ShieldWidth = 0.55f, ShieldHeight = 0.95f, ShieldThickness = 0.03f;

    /// <summary>⭐ CE-1172 — <paramref name="parts"/> with a riot shield held upright in front of his left side, from knee to chin.</summary>
    public static KitPart[] ShieldOnArm(KitPart[] parts, bool crouched)
    {
        var drop = crouched ? CrouchDrop : Vector3.Zero;
        var bottom = new Vector3(0.34f, 0.10f, 0.52f) + drop;
        return [.. parts, Limb(bottom, bottom + new Vector3(0f, 0f, ShieldHeight), ShieldWidth, ShieldThickness, ColourRole.Glass)];
    }

    /// <summary>
    /// The round in a throwable's hand: a box of <paramref name="length"/> × <paramref name="diameter"/> metres in
    /// <paramref name="colour"/> (its munition's own look — a grey stone, a dark green grenade, a canister), just beyond the end of the
    /// right forearm of <paramref name="parts"/>, in that part's unit frame. Null when there is no forearm.
    /// </summary>
    public static PartDetail? ThrowableDetail(KitPart[] parts, float length, float diameter, Color colour)
    {
        int fore = TorsoIndex + 5;
        if (parts.Length <= fore) return null;
        var size = parts[fore].Extent * S;   // a limb's extent is (length, width, height) / S
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f) return null;
        float d = MathF.Max(0.05f, diameter), len = MathF.Max(d, length);
        return new PartDetail(fore, new Vector3(0.5f + len * 0.4f / size.X, 0f, 0f), new Vector3(len / size.X, d / size.Y, d / size.Z), colour);
    }
}
