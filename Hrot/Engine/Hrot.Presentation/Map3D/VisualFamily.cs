using Fdp.Core;
using Fdp.Interfaces;
using Hrot.Core.Tkb;
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
/// <para>⭐ CE-2138 (C2, rule K1, R-279 / R-280 (behaviors)) — the DIS half is now <see cref="EntityClassTable"/>: the family is
/// that of the MOST SPECIFIC class in the type's chain that has one (<see cref="FamilyOfClass"/>), so the 3-D model, the
/// picker and the behaviours read one table. ⭐ CE-2139 (C3) — units have their own DIS Kind, so the composite check is gone.</para>
/// </summary>
public static class VisualFamilies
{
    /// <summary>⭐ CE-2138 — the classes that decide a family; a type takes the first one found walking its chain from the
    /// most specific class up. Every name here is a class of <see cref="EntityClassTable.Default"/> (rail).</summary>
    public static IReadOnlyDictionary<string, VisualFamily> FamilyOfClass { get; } = new Dictionary<string, VisualFamily>
    {
        ["Unit"] = VisualFamily.Unit,
        ["Person"] = VisualFamily.Person,
        ["Tank"] = VisualFamily.Tank,
        // ⭐ Indirect fire IF-1 (I8) — the M109 draws as a tracked, turreted body (the tank kit) until the ui lane gives artillery its own
        ["SelfPropelledArtillery"] = VisualFamily.Tank,
        ["Afv"] = VisualFamily.Afv,
        ["Car"] = VisualFamily.WheeledCar,
        ["WheeledUtility"] = VisualFamily.WheeledUtility,
        ["Helicopter"] = VisualFamily.Helicopter,
        ["Jet"] = VisualFamily.Jet,
        ["CargoPlane"] = VisualFamily.CargoPlane,
    };

    /// <summary>The family of <paramref name="t"/> — its DIS type's. ⭐ CE-2139 (C3): a unit is <see cref="VisualFamily.Unit"/>
    /// because its DIS Kind is the unit Kind (11), no longer because it carries a <see cref="TkbCompositionDef"/>.</summary>
    public static VisualFamily Of(TkbTemplate t) => Of(t.DisType);

    /// <summary>The family of a DIS type, from its class chain (<see cref="VisualFamily.Unknown"/> when no class decides one).</summary>
    public static VisualFamily Of(DISEntityType dis)
    {
        var chain = EntityClassTable.Default.Chain(dis);
        for (int i = chain.Count - 1; i >= 0; i--)
            if (FamilyOfClass.TryGetValue(chain[i].Name, out var family)) return family;
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
