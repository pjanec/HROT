using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>What a terrain pick places: a <see cref="Fdp.Toolkit.Terrain.TerrainBlock"/> (a recipe) or one <see cref="Fdp.Toolkit.Terrain.TerrainFeature"/> (a house).</summary>
public enum TerrainPickKind { Block, House, Feature }

/// <summary>How a feature is drawn: one click (a tree, a clutter item), a line (a wall, a bridge: ≥ 2 points), or an area (a wood, a lake: ≥ 3).</summary>
public enum TerrainShape { Point, Line, Area }

/// <summary>
/// ⭐ Terrain authoring U-1 (docs/DESIGN_Terrain_Authoring.md §11 B-5, §15.U-1) — ONE entry of Add Entity's terrain groups: a RECIPE (a <see cref="TerrainRecipe"/>, not a DIS type) or a building TEMPLATE.
/// </summary>
/// <param name="Id">The recipe id / the building template name.</param>
/// <param name="Group">One of <see cref="TerrainPickCatalog.Groups"/>.</param>
/// <param name="Chip">The chip under the group (Rural, Urban, Military, Nature…), or null.</param>
/// <param name="Line">The one line under the tile ("house, barns, yard").</param>
/// <param name="Facts">The side pane's rows (label, value).</param>
/// <param name="FeatureKind">⭐ U-7 — the <see cref="Fdp.Toolkit.Terrain.TerrainFeature"/> kind a <see cref="TerrainPickKind.Feature"/> places; <paramref name="Template"/> its material / clutter type / recipe; <paramref name="Shape"/> how it is drawn; <paramref name="Disabled"/> why it cannot be placed yet (null = it can).</param>
/// <param name="Picture">The tile's PNG (the recipe at its default seed, <see cref="TerrainThumbnails"/>), or null when none was asked for.</param>
public sealed record TerrainPick(TerrainPickKind Kind, string Id, string Name, string Group, string? Chip, string Line, IReadOnlyList<(string Label, string Value)> Facts, string? Picture = null,
    byte FeatureKind = 0, string Template = "", TerrainShape Shape = TerrainShape.Point, string? Disabled = null)
{
    /// <summary>The placement spec the arm carries (<c>block:hamlet</c> / <c>house:house-2f</c> / <c>feature:2:brick:Line</c>) — what <see cref="TerrainBirth"/> reads.</summary>
    public string Spec => Kind switch
    {
        TerrainPickKind.Block => "block:" + Id,
        TerrainPickKind.House => "house:" + Id,
        _ => $"feature:{FeatureKind}:{Template}:{Shape}",
    };
}

/// <summary>
/// ⭐ Terrain authoring U-1 — the SOURCE of the terrain groups: the shipped recipes (<c>assets/terrain-recipes/*.recipe.json</c>) and building templates (<c>assets/buildings/*.building.json</c>), listed for the
/// gallery. A recipe is not a DIS type, so the groups cannot be DIS masks (G-15): <see cref="EntityTypeCatalog.Build"/> lists these entries beside the TKB types. ⭐ A recipe may carry an optional
/// <c>"ui": { "group", "chip", "line", "name" }</c> object (the engine ignores it) so a new recipe needs no code.
/// ⛔ The groups with no engine behind them yet (vegetation, walls, clutter, water — backend's T-2, CE-4429) are LISTED and empty, with their reason in <see cref="PendingReason"/>.
/// </summary>
public static class TerrainPickCatalog
{
    public const string Blocks = "Blocks", Buildings = "Buildings", Vegetation = "Vegetation", Walls = "Walls & fences", Clutter = "Clutter", Water = "Water & bridges";

    /// <summary>The six terrain groups, in sidebar order (B-5).</summary>
    public static readonly IReadOnlyList<string> Groups = new[] { Blocks, Buildings, Vegetation, Walls, Clutter, Water };

    /// <summary>The chips of the Blocks group, in the mockup's order.</summary>
    public static readonly IReadOnlyList<string> BlockChips = new[] { "Rural", "Urban", "Military", "Nature" };

    /// <summary>Why a group lists nothing yet, or null for one that lists entries (every group lists since backend's T-2).</summary>
    public static string? PendingReason(string group) => null;

    /// <summary>
    /// ⭐ Terrain authoring U-7 (backend's T-2, CE-4429; mockup 3) — the SINGLE-FEATURE picks, not files: trees, scrub and woods (Vegetation), walls · fences · hedges (a line), the clutter types of <c>ClutterTypes</c>, water and a bridge. A pick arms the point tool or the
    /// line / area drawing tool; the entity it makes is a <c>TerrainFeature</c> of that kind (+ an <c>EditablePolyline</c> for a line / area). ⛔ The area SCATTER is listed greyed with its reason: backend shipped no scatter recipe yet (T-3).
    /// </summary>
    public static IReadOnlyList<TerrainPick> FeaturePicks()
    {
        TerrainPick F(string group, byte kind, string name, string template, TerrainShape shape, string line, string? disabled = null)
            => new(TerrainPickKind.Feature, name.ToLowerInvariant().Replace(' ', '-'), name, group, null, line,
                   new[] { ("Drawn as", shape switch { TerrainShape.Point => "one click", TerrainShape.Line => "a line: click the points, right-click or Enter finishes", _ => "an area: click the corners, right-click or Enter finishes" }) },
                   null, kind, template, shape, disabled);
        var l = new List<TerrainPick>
        {
            F(Vegetation, TerrainFeature.Wood, "Wood", "", TerrainShape.Area, "an area of trees"),
            F(Vegetation, TerrainFeature.Scrub, "Scrub", "", TerrainShape.Area, "an area of bushes"),
            F(Vegetation, TerrainFeature.Tree, "Tree", "", TerrainShape.Point, "one tree"),
            F(Walls, TerrainFeature.Wall, "Wall", "", TerrainShape.Line, "a brick or concrete wall, 2 m"),
            F(Walls, TerrainFeature.Fence, "Fence", "", TerrainShape.Line, "a wooden fence, 1.6 m"),
            F(Walls, TerrainFeature.Hedge, "Hedge", "", TerrainShape.Line, "a hedge, 1.5 m"),
            F(Water, TerrainFeature.Water, "Water", "", TerrainShape.Area, "a lake or pond; impassable"),
            F(Water, TerrainFeature.Bridge, "Bridge", "", TerrainShape.Line, "a deck over the river between two points, joins the banks"),
            F(Clutter, TerrainFeature.Scatter, "Street clutter", "", TerrainShape.Area, "cars, barrels and crates scattered in an area", "No scatter recipe is shipped yet (backend's T-3)."),
        };
        foreach (var t in Fdp.Toolkit.Terrain.ClutterTypes.All)
            l.Add(F(Clutter, TerrainFeature.Clutter, t.Name, t.Id, TerrainShape.Point, $"{t.Length:0.#} × {t.Width:0.#} m, {t.Material}"));
        return l;
    }

    /// <summary>The terrain group a name belongs to; false for any other group.</summary>
    public static bool IsTerrainGroup(string group) { foreach (var g in Groups) if (g == group) return true; return false; }

    /// <summary>
    /// The picks of the recipes found in <paramref name="recipeRoots"/> and the templates found in <paramref name="buildingRoots"/> (the first root holding an id wins — the rule <see cref="TerrainRecipe.Find"/> has).
    /// A file that does not parse is skipped. Sorted by group, then name.
    /// </summary>
    public static IReadOnlyList<TerrainPick> Build(IEnumerable<string?> recipeRoots, IEnumerable<string?> buildingRoots, string? pictureDir = null)
    {
        var recipeRootList = recipeRoots.ToList(); var buildingRootList = buildingRoots.ToList();
        var list = new List<TerrainPick>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in recipeRootList)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*" + TerrainRecipe.Suffix))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var recipe = TerrainRecipe.Parse(json, file);
                    if (!seen.Add("r:" + recipe.Id)) continue;
                    list.Add(OfRecipe(recipe, json));
                }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            }
        }
        foreach (var root in buildingRootList)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*" + TerrainAssets.TemplateSuffix))
            {
                string id = Path.GetFileName(file); id = id.Substring(0, id.Length - TerrainAssets.TemplateSuffix.Length);
                if (!seen.Add("b:" + id)) continue;
                try { list.Add(OfTemplate(id, File.ReadAllText(file))); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            }
        }
        list.AddRange(FeaturePicks());
        if (pictureDir is not null)
            for (int i = 0; i < list.Count; i++)
                if (list[i].Kind != TerrainPickKind.Feature) list[i] = list[i] with { Picture = TerrainThumbnails.EnsureFor(list[i], recipeRootList, buildingRootList, pictureDir) };
        list.Sort((a, b) =>
        {
            int c = Rank(a.Group).CompareTo(Rank(b.Group));
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static int Rank(string group) { for (int i = 0; i < Groups.Count; i++) if (Groups[i] == group) return i; return Groups.Count; }

    /// <summary>The shipped catalogue: the NAS's recipes and building templates (empty when the NAS root cannot be resolved).</summary>
    public static IReadOnlyList<TerrainPick> Shipped()
    {
        try
        {
            string nas = ContentLayout.NasRoot;
            return Build(new[] { ContentLayout.TerrainRecipeAssets(nas) }, new[] { ContentLayout.BuildingAssets(nas) }, TerrainThumbnails.DefaultCacheDir);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException) { return Array.Empty<TerrainPick>(); }
    }

    private static TerrainPick OfRecipe(TerrainRecipe r, string json)
    {
        var ui = (JsonNode.Parse(json) as JsonObject)?["ui"] as JsonObject;
        string group = (string?)ui?["group"] is { Length: > 0 } g && IsTerrainGroup(g) ? g : Blocks;
        string name = (string?)ui?["name"] is { Length: > 0 } n ? n : Title(r.Id);
        string? chip = (string?)ui?["chip"] is { Length: > 0 } c ? c : r.HousesMax > 0 ? "Rural" : "Nature";
        string houses = r.HousesMax == 0 ? "" : r.HousesMin == r.HousesMax ? $"{r.HousesMax} houses" : $"{r.HousesMin}-{r.HousesMax} houses";
        string woods = r.WoodsMax == 0 ? "" : r.WoodsMin == r.WoodsMax ? $"{r.WoodsMax} woods" : $"{r.WoodsMin}-{r.WoodsMax} woods";
        string line = (string?)ui?["line"] is { Length: > 0 } l ? l : string.Join(", ", new[] { houses, woods }.Where(s => s.Length > 0));
        var facts = new List<(string, string)>();
        if (houses.Length > 0) facts.Add(("Houses", houses + " from " + string.Join(", ", r.Templates)));
        if (woods.Length > 0) facts.Add(("Woods", woods));
        facts.Add(("Reach", $"about {Math.Round(r.Radius * 2)} m across"));
        facts.Add(("Layout", "a new one each time — Re-roll changes it"));
        return new TerrainPick(TerrainPickKind.Block, r.Id, name, group, chip, line, facts);
    }

    private static TerrainPick OfTemplate(string id, string json)
    {
        var obj = JsonNode.Parse(json) as JsonObject;
        int storeys = (obj?["storeys"] as JsonArray)?.Count ?? 0;
        string size = "";
        if (obj?["footprint"] is JsonArray fp && fp.Count > 1)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in fp) { float x = p![0]!.GetValue<float>(), y = p[1]!.GetValue<float>(); x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            size = $"{Math.Round(x1 - x0)} × {Math.Round(y1 - y0)} m";
        }
        string material = (string?)obj?["material"] ?? "";
        string line = string.Join(", ", new[] { storeys > 0 ? (storeys == 1 ? "1 storey" : storeys + " storeys") : "", size }.Where(s => s.Length > 0));
        var facts = new List<(string, string)>();
        if (size.Length > 0) facts.Add(("Footprint", size));
        if (storeys > 0) facts.Add(("Storeys", storeys.ToString()));
        if (material.Length > 0) facts.Add(("Walls", material));
        facts.Add(("Placed as", "one house, fitted to the ground"));
        return new TerrainPick(TerrainPickKind.House, id, Title(id), Buildings, null, line, facts);
    }

    /// <summary><c>village-street</c> → <c>Village street</c>.</summary>
    public static string Title(string id)
    {
        string s = id.Replace('-', ' ').Replace('_', ' ');
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
