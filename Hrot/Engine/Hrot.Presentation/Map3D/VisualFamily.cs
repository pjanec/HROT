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
