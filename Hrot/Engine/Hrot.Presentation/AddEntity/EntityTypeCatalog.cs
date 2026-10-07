using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;
using NodeEditor.UI.Picker;

namespace Hrot.UI.Common.AddEntity;

/// <summary>One placeable type, as the Add Entity picker lists it.</summary>
/// <param name="TkbType">The TKB type the tool spawns.</param>
/// <param name="Name">Display name — the TKB master's <c>CustomName</c>, else the template name.</param>
/// <param name="Category">The picker's folder path, <c>"/"</c>-separated (D2).</param>
/// <param name="DisText">The DIS type in text form (<c>1.1.225.1.1.1.0</c>), or null for none.</param>
/// <param name="IconName">The TKB visual's <c>IconName</c>, or null (S5 draws a fallback glyph).</param>
/// <param name="Tool">The tool a pick arms (D4).</param>
/// <param name="HasSide">True for a type placed as Friendly/Hostile/Neutral (D6).</param>
/// <param name="DisabledReason">Why it cannot be picked, or null when it can.</param>
public sealed record EntityTypeEntry(
    long TkbType, string Name, string Category, string? DisText, string? IconName,
    PlacementToolKind Tool, bool HasSide, string? DisabledReason = null)
{
    /// <summary>True when the entry can be picked.</summary>
    public bool IsEnabled => DisabledReason is null;
}

/// <summary>
/// ⭐ <c>CE-1017</c> S1 — the list of types the Add Entity picker offers, BUILT FROM THE TKB.
/// 📄 docs/DESIGN_Add_Entity_Picker.md D2 (grouping), D3c (preview), D7 (which types).
///
/// <para>⭐ D7: every template carrying a <see cref="TkbMasterDto"/>, minus <see cref="TkbMasterDto.HideFromPalette"/>,
/// plus <see cref="PlacementToolRegistry.Graphics"/> (they carry no master). It replaces the hand-written
/// <c>ScenarioSpawnerCatalog</c> and ExCon's list (S4).</para>
///
/// <para>⭐ D2 grouping: composites (a <see cref="TkbCompositionDef"/>) under <c>Units</c>; map graphics under
/// <c>Map Graphics</c>; everything else by its named DIS path (<see cref="DisNameTable.Path"/>, 0 levels skipped);
/// no DIS ⇒ the template's own <c>CategoryPath</c> ⇒ <c>Other</c>. Single-child chains are folded by the picker
/// (<see cref="PickerRequest.FoldSingleChildFolders"/>), not here.</para>
/// </summary>
public static class EntityTypeCatalog
{
    /// <summary>Folder for composite units.</summary>
    public const string UnitsFolder = "Units";
    /// <summary>Folder for the side-less map graphics.</summary>
    public const string MapGraphicsFolder = "Map Graphics";
    /// <summary>Folder for a type with neither a DIS type nor a category path.</summary>
    public const string OtherFolder = "Other";
    /// <summary>Icon key prefix the entity icon library serves (S5).</summary>
    public const string IconKeyPrefix = "entity/";

    /// <summary>Builds the catalog from <paramref name="tkb"/>, ordered by folder then name.</summary>
    public static IReadOnlyList<EntityTypeEntry> Build(ITkbDatabase tkb, DisNameTable? names = null)
    {
        ArgumentNullException.ThrowIfNull(tkb);
        names ??= DisNameTable.Default;

        var list = new List<EntityTypeEntry>();
        foreach (var t in tkb.GetAll())
        {
            if (!PlacementToolRegistry.HasSide(t.TkbType)) continue;   // a graphic: listed from the registry below
            var master = t.GetDescriptor<TkbMasterDto>();
            if (master is null || master.HideFromPalette) continue;

            string name = string.IsNullOrWhiteSpace(master.CustomName) ? t.Name : master.CustomName;
            string? dis = t.DisType.Value != 0 ? t.DisType.ToString() : null;
            string? icon = t.GetDescriptor<VisualDefinitionDto>()?.IconName;
            icon = string.IsNullOrWhiteSpace(icon) ? null : icon;

            list.Add(new EntityTypeEntry(t.TkbType, name, CategoryOf(t, names), dis, icon,
                                         PlacementToolKind.Point, HasSide: true));
        }

        foreach (var g in PlacementToolRegistry.Graphics)
            list.Add(new EntityTypeEntry(g.TkbType, g.Name, MapGraphicsFolder, null, null, g.Tool,
                                         HasSide: false, g.DisabledReason));

        list.Sort((a, b) =>
        {
            int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>The D2 folder path of a template.</summary>
    public static string CategoryOf(TkbTemplate t, DisNameTable names)
    {
        if (t.GetDescriptor<TkbCompositionDef>() is not null)
            return t.DisType.Country != 0 ? UnitsFolder + "/" + names.CountryName(t.DisType.Country) : UnitsFolder;

        var path = names.Path(t.DisType);
        if (path.Count > 0) return string.Join("/", path);

        var own = (t.CategoryPath ?? "").Replace('\\', '/').Trim('/');
        return own.Length > 0 ? own : OtherFolder;
    }

    /// <summary>The picker rows for the side-bearing types (<paramref name="sideBearing"/> true — the Friendly/Hostile/
    /// Neutral items) or for the map graphics (false — <c>Map Graphics…</c>). <see cref="PickerEntry.Tag"/> carries
    /// the <see cref="EntityTypeEntry"/>.</summary>
    public static IEnumerable<PickerEntry> ToPickerEntries(IEnumerable<EntityTypeEntry> catalog, bool sideBearing)
    {
        foreach (var e in catalog)
        {
            if (e.HasSide != sideBearing) continue;
            yield return new PickerEntry(
                Id: e.TkbType.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name: e.Name,
                Description: e.DisabledReason ?? (e.DisText is null ? null : "DIS " + e.DisText),
                Category: e.Category,
                Keywords: Keywords(e),
                IconTextureId: null,
                Tag: e,
                IconKey: IconKeyOf(e),
                IsEnabled: e.IsEnabled);
        }
    }

    /// <summary>The icon key of an entry: <c>entity/&lt;IconName&gt;</c>, else a per-tool fallback
    /// (<c>entity/_point</c>, <c>entity/_area</c> …) the icon library draws as a glyph (S5).</summary>
    public static string IconKeyOf(EntityTypeEntry e)
        => IconKeyPrefix + (e.IconName ?? "_" + e.Tool.ToString().ToLowerInvariant());

    private static IReadOnlyList<string> Keywords(EntityTypeEntry e)
    {
        var k = new List<string>(e.Category.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (e.DisText is not null) k.Add(e.DisText);
        return k;
    }
}
