using Fdp.Interfaces;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.UI.Common.Map3D;

/// <summary>What kind of thing a TKB type is, for drawing it — an icon glyph in 2-D, a shape kit in 3-D.</summary>
public enum VisualFamily : byte
{
    Unknown = 0,
    Person,
    Tank,
    Afv,
    WheeledCar,
    WheeledUtility,
    /// <summary>Rotary wing — DIS platform/air categories 20–25.</summary>
    Helicopter,
    /// <summary>Fast fixed wing — DIS platform/air 1, 2, 6, 7, 40, 47, 50.</summary>
    Jet,
    /// <summary>Large fixed wing — DIS platform/air 3, 4, 5, 8, 57 (bomber, cargo/tanker, patrol, AEW, commercial).</summary>
    CargoPlane,
    /// <summary>A composite unit: no body of its own, its members draw themselves.</summary>
    Unit,
}

/// <summary>
/// ⭐ CE-1033 S1 — THE one answer to "what kind of thing is this type" (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.5, M7), extracted
/// from <c>EntityTypeCatalog.FallbackIconName</c> so the Add Entity icons and the 3-D shape kits cannot disagree. Keyed on
/// what the type IS — its DIS type, or being a composite — so a TKB without art still gets a meaningful picture.
/// </summary>
public static class VisualFamilies
{
    /// <summary>The family of <paramref name="t"/>.</summary>
    public static VisualFamily Of(TkbTemplate t)
    {
        if (t.GetDescriptor<TkbCompositionDef>() is not null) return VisualFamily.Unit;
        var d = t.DisType;
        if (d.Kind == 3) return VisualFamily.Person;                 // Life Form
        if (d.Kind == 1 && d.Domain == 1)                            // Platform / Land
            return d.Category switch
            {
                1 => VisualFamily.Tank,
                2 => VisualFamily.Afv,
                81 => VisualFamily.WheeledCar,                         // car
                3 or 6 or 7 => VisualFamily.WheeledUtility,            // utility vehicles
                _ => VisualFamily.Unknown,
            };
        if (d.Kind == 1 && d.Domain == 2)                            // Platform / Air — SISO-REF-010 categories (§3.5)
            return d.Category switch
            {
                >= 20 and <= 25 => VisualFamily.Helicopter,            // attack, utility, ASW, cargo, observation, special ops
                1 or 2 or 6 or 7 or 40 or 47 or 50 => VisualFamily.Jet, // fighter, attack, EW, recce, trainer, light, UAV
                3 or 4 or 5 or 8 or 57 => VisualFamily.CargoPlane,      // bomber, cargo/tanker, patrol, AEW/C2, commercial
                _ => VisualFamily.Unknown,
            };
        return VisualFamily.Unknown;
    }

    /// <summary>The fallback icon glyph of a family (null ⇒ the tool's glyph) — the Add Entity picker's S5 names.</summary>
    public static string? IconName(VisualFamily family) => family switch
    {
        VisualFamily.Unit => "_unit",
        VisualFamily.Person => "_person",
        VisualFamily.Tank => "_tank",
        VisualFamily.Afv => "_afv",
        VisualFamily.WheeledCar or VisualFamily.WheeledUtility => "_wheeled",
        _ => null,
    };

    /// <summary>True for a soldier (DIS life form, category 1) — the person kit then carries a rifle.</summary>
    public static bool IsSoldier(TkbTemplate t) => t.DisType.Kind == 3 && t.DisType.Category == 1;
}
