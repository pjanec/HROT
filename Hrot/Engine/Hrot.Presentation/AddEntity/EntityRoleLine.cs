using System.Text;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ <c>CE-4142</c> GL3 (G3) — the ONE line under a gallery tile and the facts in its side pane, composed from the TKB.
/// The master's <c>ShortDescription</c> wins; else <i>class · main weapon</i> — the class from <see cref="EntityClassTable"/>
/// (its most specific class that is not a mere level name), the weapon from the FIRST mount of the <see cref="WeaponSuiteDto"/>
/// (a countermeasure dispenser is not a weapon). 📄 docs/DESIGN_Add_Entity_Picker.md §2f G3/G6.
/// </summary>
public static class EntityRoleLine
{
    /// <summary>The tile line of <paramref name="t"/>; null when the TKB says nothing to compose.</summary>
    public static string? Compose(TkbTemplate t, TkbMasterDto master, EntityClassTable? classes = null)
    {
        if (!string.IsNullOrWhiteSpace(master.ShortDescription)) return master.ShortDescription.Trim();

        var parts = new List<string>(2);
        if (ClassName(t, classes ?? EntityClassTable.Default) is { } cls) parts.Add(cls);
        if (MainWeapon(t) is { } weapon) parts.Add(weapon);
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The display name of the type's most specific class, without the table's " (HROT)" tag; null when none matches.</summary>
    public static string? ClassName(TkbTemplate t, EntityClassTable classes)
    {
        if (t.DisType.Value == 0) return null;
        var c = classes.MostSpecific(t.DisType);
        if (c is null) return null;
        string n = c.DisplayName;
        const string tag = " (HROT)";
        return n.EndsWith(tag, StringComparison.Ordinal) ? n[..^tag.Length] : n;
    }

    /// <summary>The first mount's weapon category in words (<c>Tank gun</c>), or null when it has none.</summary>
    public static string? MainWeapon(TkbTemplate t)
    {
        var suite = t.GetDescriptor<WeaponSuiteDto>();
        if (suite is null) return null;
        foreach (var m in suite.Mounts)
            if (m.Category is not (WeaponCategory.Unknown or WeaponCategory.Countermeasure)) return Words(m.Category.ToString());
        return null;
    }

    /// <summary>All the weapon categories of the mounts, in mount order, distinct.</summary>
    public static IReadOnlyList<string> Weapons(TkbTemplate t)
        => t.GetDescriptor<WeaponSuiteDto>()?.Mounts
            .Where(m => m.Category is not (WeaponCategory.Unknown or WeaponCategory.Countermeasure))
            .Select(m => Words(m.Category.ToString())).Distinct().ToArray()
           ?? Array.Empty<string>();

    /// <summary>The facts the gallery's side pane lists (label, value) for a type: DIS type, speed, seats, weapons. Empty rows are left out.</summary>
    public static IReadOnlyList<(string Label, string Value)> Facts(TkbTemplate t, string? disText)
    {
        var facts = new List<(string, string)>();
        var v = t.GetDescriptor<VehicleParametersDto>();
        if (v is not null && v.MaxSpeedFwd > 0f)
            facts.Add(("Speed", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v.MaxSpeedFwd * 3.6f:0} km/h")));
        var carrier = t.GetDescriptor<CarrierDefDto>();
        if (carrier is not null && carrier.Seats.Count > 0)
            facts.Add(("Seats", carrier.Seats.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var weapons = Weapons(t);
        if (weapons.Count > 0) facts.Add(("Weapons", string.Join(", ", weapons)));
        if (disText is not null) facts.Add(("DIS", disText));
        return facts;
    }

    /// <summary><c>TankGun</c> → <c>Tank gun</c>.</summary>
    internal static string Words(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 2);
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (i == 0) sb.Append(char.ToUpperInvariant(c));
            else if (char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
