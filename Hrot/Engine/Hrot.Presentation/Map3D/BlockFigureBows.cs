using System.Numerics;
using Fdp.Toolkit.Combat.Components;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1976 (docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §3 BW-10, mockup <c>docs/designs/weapons/bows-visuals.svg</c>, R-4143) — the BOW and the CROSSBOW in a man's hands.
/// A bow is a stave of two bent limbs and a grip, a string, and (aimed) the arrow nocked and DRAWN to the cheek: the bow arm straight out, the string arm's elbow raised and out.
/// Carried, it hangs in the left hand with its string toward him. A crossbow is a wooden stock along the rifle's line, a prod of two swept limbs across its front, the string spanned
/// back to a catch and a bolt on the rail. All of it is figure PARTS (the weapon part, then appended boxes), in body metres — the numbers the mockup draws.
/// </summary>
public static partial class BlockFigure
{
    /// <summary>The bow's limb half-length (m, along the stave) and the limb mid-point's and tip's setback from the grip, relaxed and at full draw.</summary>
    public const float BowHalf = 0.58f, BowMidBack = 0.05f, BowTipBack = 0.14f, BowTipBackDrawn = 0.20f;

    /// <summary>The arrow's drawn length (m): its nock at the string hand, its point just past the grip.</summary>
    public const float NockedArrow = 0.66f;

    private static readonly Color QuiverLeather = new(96, 66, 40, 255);

    /// <summary>
    /// <paramref name="parts"/> with a BOW (<see cref="HeldLook.Bow"/>) or a CROSSBOW (<see cref="HeldLook.Crossbow"/>) in his hands in <paramref name="posture"/>. The weapon part becomes the
    /// arrow (aimed bow) / the bow's grip (carried) / the crossbow's stock; the other boxes are appended. Aimed bow: the left arm straight to the grip, the right hand drawn to the cheek.
    /// </summary>
    public static KitPart[] BowHeld(KitPart[] parts, HeldLook look, WeaponPostureKind posture)
    {
        var held = (KitPart[])parts.Clone();
        var rifle = parts[WeaponIndex];
        if (look == HeldLook.Crossbow) return CrossbowHeld(held, rifle);
        var lsh = Ends(parts[6]).A;
        var rsh = Ends(parts[8]).A;
        var c = (lsh + rsh) / 2f;
        bool aimed = posture == WeaponPostureKind.Aimed && c.Z > 0.5f;   // on his front there is no room to draw: carried
        var extra = new List<KitPart>(12);
        Vector3 g, u;
        if (aimed)
        {
            g = c + new Vector3(0.57f, 0.12f, 0.12f);
            var n = c + new Vector3(0.06f, -0.12f, 0.12f);
            u = Vector3.Normalize(new Vector3(g.X - n.X, g.Y - n.Y, 0f));
            Bow(extra, g, u, drawn: true, n);
            // the arrow: shaft (the weapon part), a metal point, two crossed vanes
            held[WeaponIndex] = Reshaped(rifle with { Colour = ColourRole.Wood }, n, n + u * (NockedArrow - 0.06f), 0.012f, 0.012f);
            extra.Add(Limb(n + u * (NockedArrow - 0.06f), n + u * NockedArrow, 0.024f, 0.024f, ColourRole.Metal));
            extra.Add(Limb(n + u * 0.02f, n + u * 0.13f, 0.036f, 0.006f, ColourRole.Red));
            extra.Add(Limb(n + u * 0.02f, n + u * 0.13f, 0.006f, 0.036f, ColourRole.Red));
            held[7] = Reshaped(parts[7], Knee(lsh, g, ArmReach / 2f, ArmReach / 2f, new Vector3(0f, 0.4f, -0.3f)), g, ArmWidth * 0.9f, ArmWidth * 0.9f);
            held[6] = Reshaped(parts[6], lsh, Knee(lsh, g, ArmReach / 2f, ArmReach / 2f, new Vector3(0f, 0.4f, -0.3f)), ArmWidth, ArmWidth);
            var elbowR = Knee(rsh, n, ArmReach / 2f, ArmReach / 2f, new Vector3(-0.35f, -1f, 0.2f));
            held[8] = Reshaped(parts[8], rsh, elbowR, ArmWidth, ArmWidth);
            held[9] = Reshaped(parts[9], elbowR, n, ArmWidth * 0.9f, ArmWidth * 0.9f);
        }
        else
        {
            // carried in the left hand, the stave upright beside him and its string toward his body (the bow's plane faces out: u = +y)
            g = posture == WeaponPostureKind.AtEase ? lsh + new Vector3(0.10f, 0.06f, -0.55f) : lsh + new Vector3(0.34f, 0.04f, -0.34f);
            u = Vector3.UnitY;
            Bow(extra, g, u, drawn: false, default);
            held[WeaponIndex] = Reshaped(rifle with { Colour = ColourRole.Dark }, g - Vector3.UnitZ * 0.07f, g + Vector3.UnitZ * 0.07f, 0.045f, 0.045f);   // the grip
            var elbow = Knee(lsh, g, ArmReach / 2f, ArmReach / 2f, new Vector3(0f, 0.3f, -0.1f));
            held[6] = Reshaped(parts[6], lsh, elbow, ArmWidth, ArmWidth);
            held[7] = Reshaped(parts[7], elbow, g, ArmWidth * 0.9f, ArmWidth * 0.9f);
        }
        return [.. held, .. extra];
    }

    /// <summary>The stave (four limb boxes) and the string of a bow whose grip is at <paramref name="g"/>, its belly toward <paramref name="u"/>; a drawn string runs tip → <paramref name="nock"/> → tip, a relaxed one tip → tip.</summary>
    private static void Bow(List<KitPart> into, Vector3 g, Vector3 u, bool drawn, Vector3 nock)
    {
        float tip = drawn ? BowTipBackDrawn : BowTipBack, mid = drawn ? BowMidBack + 0.02f : BowMidBack;
        Vector3 W(float back, float up) => g - u * back + Vector3.UnitZ * up;
        foreach (float s in new[] { 1f, -1f })
        {
            into.Add(Limb(W(0f, 0.05f * s), W(mid, 0.30f * s), 0.03f, 0.03f, ColourRole.Wood));
            into.Add(Limb(W(mid, 0.30f * s), W(tip, BowHalf * s), 0.026f, 0.026f, ColourRole.Wood));
        }
        if (drawn) { into.Add(Limb(W(tip, BowHalf), nock, 0.008f, 0.008f, ColourRole.Bone)); into.Add(Limb(nock, W(tip, -BowHalf), 0.008f, 0.008f, ColourRole.Bone)); }
        else into.Add(Limb(W(tip, BowHalf), W(tip, -BowHalf), 0.008f, 0.008f, ColourRole.Bone));
    }

    /// <summary>A crossbow along the rifle's line: the stock (the weapon part), a prod of two swept limbs, the spanned string back to a catch, a bolt on the rail.</summary>
    private static KitPart[] CrossbowHeld(KitPart[] held, KitPart rifle)
    {
        var (stock, muzzle) = Ends(rifle);
        var dir = Vector3.Normalize(muzzle - stock);
        var side = Vector3.Cross(Vector3.UnitZ, dir);
        side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitY;
        held[WeaponIndex] = Reshaped(rifle with { Colour = ColourRole.Wood }, stock, stock + dir * 0.72f, 0.05f, 0.075f);
        var f = stock + dir * 0.60f;
        var catchAt = stock + dir * 0.34f;
        var extra = new List<KitPart>(8);
        foreach (float s in new[] { 1f, -1f })
        {
            var p1 = f + side * (0.15f * s) - dir * 0.01f;
            var p2 = f + side * (0.30f * s) - dir * 0.07f;
            extra.Add(Limb(f, p1, 0.03f, 0.03f, ColourRole.Wood));
            extra.Add(Limb(p1, p2, 0.026f, 0.026f, ColourRole.Wood));
            extra.Add(Limb(p2, catchAt, 0.008f, 0.008f, ColourRole.Bone));
        }
        var rail = Vector3.UnitZ * 0.05f;
        extra.Add(Limb(catchAt + rail, stock + dir * 0.80f + rail, 0.016f, 0.016f, ColourRole.Wood));            // the bolt: shorter and thicker than an arrow
        extra.Add(Limb(stock + dir * 0.80f + rail, stock + dir * 0.85f + rail, 0.024f, 0.024f, ColourRole.Metal));
        return [.. held, .. extra];
    }

    /// <summary>The QUIVER on the back of a man who carries a bow: a leather case slanting up from his right hip to over his left shoulder, three fletchings showing at its top — details on the torso (they follow every stance).
    /// A crossbowman wears a short bolt case at the right hip instead.</summary>
    public static void QuiverDetails(HeldLook look, List<PartDetail> into)
    {
        const float tx = 0.24f, ty = 0.40f, tz = 0.56f, zc = 1.18f;
        PartDetail D(float x, float y, float z, float dx, float dy, float dz, Color c) => new(TorsoIndex, new(x / tx, y / ty, (z - zc) / tz), new(dx / tx, dy / ty, dz / tz), c);
        if (look == HeldLook.Bow)
        {
            into.Add(D(-0.17f, -0.11f, 1.02f, 0.08f, 0.10f, 0.30f, QuiverLeather));
            into.Add(D(-0.17f, -0.06f, 1.27f, 0.08f, 0.10f, 0.30f, QuiverLeather));
            into.Add(D(-0.17f, -0.01f, 1.50f, 0.08f, 0.10f, 0.26f, QuiverLeather));
            into.Add(D(-0.17f, -0.04f, 1.70f, 0.03f, 0.03f, 0.10f, new Color(190, 40, 36, 255)));
            into.Add(D(-0.17f, 0.01f, 1.72f, 0.03f, 0.03f, 0.12f, new Color(226, 218, 196, 255)));
            into.Add(D(-0.17f, 0.05f, 1.69f, 0.03f, 0.03f, 0.09f, new Color(190, 40, 36, 255)));
        }
        else if (look == HeldLook.Crossbow)
        {
            into.Add(D(-0.02f, -0.215f, 0.88f, 0.09f, 0.07f, 0.20f, QuiverLeather));
            into.Add(D(-0.02f, -0.215f, 1.01f, 0.03f, 0.03f, 0.06f, new Color(226, 218, 196, 255)));
        }
    }
}
