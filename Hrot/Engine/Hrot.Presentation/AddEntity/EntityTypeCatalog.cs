using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;
using NodeEditor.UI.Picker;
using Fdp.Core;

namespace Hrot.UI.Common.AddEntity;

/// <summary>One placeable type, as the Add Entity picker lists it.</summary>
/// <param name="TkbType">The TKB type the tool spawns.</param>
/// <param name="Name">Display name — the TKB master's <c>CustomName</c>, else the template name.</param>
/// <param name="Category">The picker's folder path, <c>"/"</c>-separated (D2).</param>
/// <param name="DisText">The DIS type in text form (<c>1.1.225.1.1.1.0</c>), or null for none.</param>
/// <param name="IconName">The TKB visual's <c>IconName</c>; else a fallback glyph name chosen from the DIS type
/// (<c>_person</c>, <c>_tank</c>, <c>_afv</c>, <c>_wheeled</c>, <c>_unit</c>); null ⇒ the tool's glyph.</param>
/// <param name="Tool">The tool a pick arms (D4).</param>
/// <param name="HasSide">True for a type placed as Friendly/Hostile/Neutral (D6).</param>
/// <param name="DisabledReason">Why it cannot be picked, or null when it can.</param>
/// <param name="Thumbnail">⭐ CE-4142 (G4) — the full path of its gallery thumbnail (explicit name, else the most specific <c>map.json</c> mask), or null ⇒ the glyph on a dashed tile.</param>
/// <param name="RoleLine">⭐ CE-4142 (G3) — the one line under its tile: the TKB master's short description, else <i>class · main weapon</i>; null for none.</param>
/// <param name="Group">⭐ CE-4142 (G1) — its gallery group (<see cref="GalleryGroups"/>); empty when the catalogue was built without a gallery.</param>
/// <param name="Chip">⭐ CE-4142 (G2) — its chip inside the group (the next DIS level), or null.</param>
public sealed record EntityTypeEntry(
    TkbTypeId TkbType, string Name, string Category, string? DisText, string? IconName,
    PlacementToolKind Tool, bool HasSide, string? DisabledReason = null,
    string? Thumbnail = null, string? RoleLine = null, string Group = "", string? Chip = null, string? MacroPath = null)
{
    /// <summary>True when the entry can be picked.</summary>
    public bool IsEnabled => DisabledReason is null;

    /// <summary>⭐ CE-4142 — the picker category of the GALLERY: <c>Group</c> or <c>Group/Chip</c>; the DIS <see cref="Category"/> when there is no group.</summary>
    public string GalleryCategory => Group.Length == 0 ? Category : Chip is null ? Group : Group + "/" + Chip;
}

/// <summary>
/// ⭐ <c>CE-1017</c> S1 — the list of types the Add Entity picker offers, BUILT FROM THE TKB.
/// 📄 docs/DESIGN_Add_Entity_Picker.md D2 (grouping), D3c (preview), D7 (which types).
///
/// <para>⭐ D7: every template carrying a <see cref="TkbMasterDto"/>, minus <see cref="TkbMasterDto.HideFromPalette"/>,
/// plus <see cref="PlacementToolRegistry.Graphics"/> (they carry no master). It replaces the hand-written
/// <c>ScenarioSpawnerCatalog</c> and ExCon's list (S4).</para>
///
/// <para>⭐ D2 grouping: units (DIS Kind 11 since CE-2139, formerly any <see cref="TkbCompositionDef"/>) under <c>Units</c>; map graphics under
/// <c>Map Graphics</c>; everything else by its named DIS path (<see cref="DisNameTable.Path"/> — ⭐ CE-2138: the level names of
/// the type's class chain in <see cref="EntityClassTable"/>, 0 levels skipped);
/// no DIS ⇒ the template's own <c>CategoryPath</c> ⇒ <c>Other</c>. Single-child chains are folded by the picker
/// (<see cref="PickerRequest.FoldSingleChildFolders"/>), not here.</para>
/// </summary>
public static class EntityTypeCatalog
{
    /// <summary>Folder for composite units — ⭐ CE-2139: the display name of the unit DIS Kind in the class table (railed).</summary>
    public const string UnitsFolder = "Units";
    /// <summary>Folder for the side-less map graphics.</summary>
    public const string MapGraphicsFolder = "Map Graphics";
    /// <summary>Folder for a type with neither a DIS type nor a category path.</summary>
    public const string OtherFolder = "Other";
    /// <summary>Icon key prefix the entity icon library serves (S5).</summary>
    public const string IconKeyPrefix = "entity/";

    /// <summary>Builds the catalog from <paramref name="tkb"/>, ordered by folder then name.</summary>
    /// <param name="gallery">⭐ CE-4142 — the gallery's groups and thumbnail map; null ⇒ entries carry no group and no thumbnail (the plain DIS tree).</param>
    /// <summary>⭐ CE-4210 MA-7 — the folder name macro entries are listed under when no gallery group names them.</summary>
    public const string MacrosFolder = "Macros";

    public static IReadOnlyList<EntityTypeEntry> Build(ITkbDatabase tkb, DisNameTable? names = null, GalleryData? gallery = null, IReadOnlyList<string>? macros = null)
    {
        ArgumentNullException.ThrowIfNull(tkb);
        names ??= DisNameTable.Default;

        var list = new List<EntityTypeEntry>();
        foreach (var t in tkb.GetAll())
        {
            if (!PlacementToolRegistry.HasSide(t.TkbType)) continue;   // a graphic: listed from the registry below
            // ⭐ CE-4210 MA-7 — the bare macro-instance type (8813, DIS 5.0.0.90) is not a placeable prop: a macro is placed from its FILE (the Macros group below), which gives the instance its MacroInstance.
            if (t.TkbType == (Fdp.Core.TkbTypeId)Hrot.Map.Common.TkbEntityTypes.ScenarioMacro) continue;
            var master = t.GetDescriptor<TkbMasterDto>();
            if (master is null || master.HideFromPalette) continue;

            string name = string.IsNullOrWhiteSpace(master.CustomName) ? t.Name : master.CustomName;
            string? dis = t.DisType.Value != 0 ? t.DisType.ToString() : null;
            string? icon = t.GetDescriptor<VisualDefinitionDto>()?.IconName;
            icon = string.IsNullOrWhiteSpace(icon) ? FallbackIconName(t) : icon;

            DISEntityType? type = t.DisType.Value != 0 ? t.DisType : null;
            var (group, chip) = gallery?.Groups.Classify(type, PlacementToolKind.Point, hasSide: true, names) ?? ("", null);
            string? thumb = gallery?.Thumbnails.Resolve(t.GetDescriptor<VisualDefinitionDto>()?.ThumbnailName, type);

            list.Add(new EntityTypeEntry(t.TkbType, name, CategoryOf(t, names), dis, icon,
                                         PlacementToolKind.Point, HasSide: true,
                                         Thumbnail: thumb, RoleLine: EntityRoleLine.Compose(t, master), Group: group, Chip: chip));
        }

        // ⭐ CE-4210 MA-7 — the macro FILES, beside the type catalogue (not TKB types): each places the instance type with its MacroInstance (PlacementToolRegistry.ArmMacro).
        if (macros is { Count: > 0 })
        {
            string? template = tkb.TryGetByType((Fdp.Core.TkbTypeId)Hrot.Map.Common.TkbEntityTypes.ScenarioMacro, out var inst) ? FallbackIconName(inst) : null;
            foreach (var path in macros)
            {
                int slash = path.LastIndexOf('/');
                string folder = slash > 0 ? path.Substring(0, slash) : "";
                string group = gallery?.Groups.MacrosName ?? "";
                list.Add(new EntityTypeEntry((Fdp.Core.TkbTypeId)Hrot.Map.Common.TkbEntityTypes.ScenarioMacro, slash >= 0 ? path.Substring(slash + 1) : path,
                                             MacrosFolder + (folder.Length == 0 ? "" : "/" + folder), null, template, PlacementToolKind.Point, HasSide: true,
                                             RoleLine: "Macro · " + path, Group: group, Chip: group.Length == 0 || folder.Length == 0 ? null : folder, MacroPath: path));
            }
        }

        foreach (var g in PlacementToolRegistry.Graphics)
        {
            var (group, chip) = gallery?.Groups.Classify(null, g.Tool, hasSide: false, names) ?? ("", null);
            list.Add(new EntityTypeEntry(g.TkbType, g.Name, MapGraphicsFolder, null, g.IconName, g.Tool,
                                         HasSide: false, g.DisabledReason, Group: group, Chip: chip));
        }

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
        // ⭐ CE-2139 (C3) — a composite is no longer special-cased: a unit's DIS Kind (11) is named "Units", so its path is
        //   Units/Land/<country>/<arm> like any other type's.
        var path = names.Path(t.DisType);
        if (path.Count > 0) return string.Join("/", path);

        var own = (t.CategoryPath ?? "").Replace('\\', '/').Trim('/');
        return own.Length > 0 ? own : OtherFolder;
    }

    /// <summary>The picker rows for the side-bearing types (<paramref name="sideBearing"/> true — the Friendly/Hostile/
    /// Neutral items) or for the map graphics (false — <c>Map Graphics…</c>). <see cref="PickerEntry.Tag"/> carries
    /// the <see cref="EntityTypeEntry"/>.</summary>
    /// <param name="gallery">⭐ CE-4142 — true ⇒ the GALLERY's rows: <see cref="PickerEntry.Category"/> is <c>Group/Chip</c>, the description is the tile's
    /// one line, <see cref="PickerEntry.ImageKey"/> its thumbnail path, and the keywords add the group, chip and line so a search over ALL groups finds it (G7).</param>
    public static IEnumerable<PickerEntry> ToPickerEntries(IEnumerable<EntityTypeEntry> catalog, bool sideBearing, bool gallery = false)
    {
        foreach (var e in catalog)
        {
            if (e.HasSide != sideBearing) continue;
            yield return new PickerEntry(
                Id: e.TkbType.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name: e.Name,
                Description: e.DisabledReason ?? (gallery ? e.RoleLine : null) ?? (e.DisText is null ? null : "DIS " + e.DisText),
                Category: gallery ? e.GalleryCategory : e.Category,
                Keywords: gallery ? GalleryKeywords(e) : Keywords(e),
                IconTextureId: null,
                Tag: e,
                IconKey: IconKeyOf(e),
                IsEnabled: e.IsEnabled,
                ImageKey: gallery ? e.Thumbnail : null);
        }
    }

    /// <summary>⭐ S4 — the side-bearing, pickable types as the spawner panel's fallback list (used only when the host
    /// has no picker). Replaces the hand-written <c>ScenarioSpawnerCatalog</c>.</summary>
    public static IReadOnlyList<Hrot.UI.Common.Panels.TkbCatalogEntry> SpawnerEntries(ITkbDatabase? tkb)
        => tkb is null
            ? Array.Empty<Hrot.UI.Common.Panels.TkbCatalogEntry>()
            : Build(tkb).Where(e => e.HasSide && e.IsEnabled)
                        .Select(e => new Hrot.UI.Common.Panels.TkbCatalogEntry(e.TkbType, e.Name)).ToArray();

    /// <summary>⭐ S5 — the glyph for a type whose TKB names no icon, from what the type IS (its DIS type, or being a
    /// composite), so a TKB without art still shows a meaningful picture. Null ⇒ the tool's glyph.</summary>
    /// <remarks>⭐ CE-1033 S1 — the classification is <see cref="Map3D.VisualFamilies"/>, shared with the 3-D shape kits.</remarks>
    public static string? FallbackIconName(TkbTemplate t)
        => Map3D.VisualFamilies.IconName(Map3D.VisualFamilies.Of(t));

    /// <summary>The icon key of an entry: <c>entity/&lt;IconName&gt;</c>, else a per-tool fallback
    /// (<c>entity/_point</c>, <c>entity/_area</c> …) the icon library draws as a glyph (S5).</summary>
    public static string IconKeyOf(EntityTypeEntry e)
        => IconKeyPrefix + (e.IconName ?? "_" + e.Tool.ToString().ToLowerInvariant());

    private static IReadOnlyList<string> GalleryKeywords(EntityTypeEntry e)
    {
        var k = new List<string>(Keywords(e));
        if (e.Group.Length > 0) k.Add(e.Group);
        if (e.Chip is not null) k.Add(e.Chip);
        if (e.RoleLine is not null) k.Add(e.RoleLine);
        return k;
    }

    private static IReadOnlyList<string> Keywords(EntityTypeEntry e)
    {
        var k = new List<string>(e.Category.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (e.DisText is not null) k.Add(e.DisText);
        return k;
    }
}
