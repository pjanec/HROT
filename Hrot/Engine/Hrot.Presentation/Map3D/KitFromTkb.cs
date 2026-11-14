using System.Numerics;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ E4b (docs/DESIGN_Visual_Effects.md VE-L, §6b) — the kit's turret and gun placed FROM the TKB: the turret axis at the
/// <see cref="TurretDto"/> pivot, the gun's trunnion at pivot + trunnion, the barrel from the trunnion to the primary turret mount's
/// muzzle offset. ⭐ So the drawn barrel tip IS <see cref="Fdp.Toolkit.Combat.Muzzle"/>'s point — the TKB is the truth, the kit draws
/// it (the <c>CE-1041</c> pattern). A type without turret data keeps its family kit unchanged.
/// </summary>
public static class KitFromTkb
{
    /// <summary>The parts and pivots to draw <paramref name="kit"/> with for a type <paramref name="template"/> of <paramref name="size"/>.</summary>
    public static (KitPart[] Parts, KitPivots Pivots) For(ShapeKit kit, TkbTemplate? template, Vector3 size)
    {
        var turret = template?.GetDescriptor<TurretSuiteDto>() is { Turrets.Count: > 0 } suite ? suite.Turrets[0] : null;
        var mount = turret != null ? MainGun(template!) : null;
        if (turret == null || mount == null || !kit.IsArticulated || size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
            return (kit.Parts, kit.Pivots);

        // ⭐ CE-1103 (docs/DESIGN_Map_3D_Mode.md §6s) — the TKB gives the pivot from the body's REFERENCE point; the kit is drawn in its box,
        //   which a Body.Geometry shifts off that point (kitToBody: BodyCentre, the box bottom at BodyCentreZ − h/2). ⛔ Read raw, a
        //   CG-referenced turret (the AH-64's chin gun) landed 1.5 m aft and 1.3 m low; a ground-referenced tank's shift is zero.
        var shift = template!.GetDescriptor<BodyGeometryDto>() is { Length: > 0f, Width: > 0f, Height: > 0f } g
            ? new Vector3(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ - size.Z / 2f) : Vector3.Zero;
        var pivot = new Vector3(turret.PivotX, turret.PivotY, turret.PivotZ) - shift;
        var trunnion = pivot + new Vector3(turret.TrunnionX, 0f, turret.TrunnionZ);
        var pivots = new KitPivots(new Vector2(pivot.X / size.X, pivot.Y / size.Y),
                                   new Vector2(trunnion.X / size.X, trunnion.Z / size.Z));
        var parts = (KitPart[])kit.Parts.Clone();
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Role == PartRole.Turret)
                parts[i] = p with { Centre = new Vector3(pivot.X / size.X, pivot.Y / size.Y, p.Centre.Z) };
            else if (p.Role == PartRole.Gun && p.Shape == PartShape.Cylinder && p.Axis == PartAxis.X)
            {
                // a horizontal barrel from the trunnion to the muzzle: its tip is trunnion + offset exactly
                var centre = trunnion + new Vector3(mount.MuzzleOffsetX / 2f, mount.MuzzleOffsetY, mount.MuzzleOffsetZ);
                parts[i] = p with { Centre = centre / size, Extent = new Vector3(mount.MuzzleOffsetX / size.X, p.Extent.Y, p.Extent.Z) };
            }
        }
        return (parts, pivots);
    }

    /// <summary>The barrel the kit draws: the first mount on turret 0 with a forward muzzle offset (mount 0 on a tank).</summary>
    private static WeaponMountDto? MainGun(TkbTemplate template)
    {
        if (template.GetDescriptor<WeaponSuiteDto>() is not { } weapons) return null;
        foreach (var m in weapons.Mounts)
            if (m.TurretIndex == 0 && m.MuzzleOffsetX > 0f) return m;
        return null;
    }
}
