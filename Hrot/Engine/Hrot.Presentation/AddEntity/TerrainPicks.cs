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
    /// <summary>
    /// ⭐ Terrain authoring follow-up 2 (mockup 3, the Clutter and single-feature tiles) — the shared icon key (<see cref="NodeEditor.Core.Widgets.Icons"/>) of a single-feature pick's tile: a clutter type its own coloured glyph (a car, a barrel, a crate…), a wall / fence / hedge the walls
    /// glyph, a wood the trees… ; null for a block or a house, whose tile is a picture of the thing itself.
    /// </summary>
    public string? GlyphKey => Kind != TerrainPickKind.Feature ? null : FeatureKind switch
    {
        Fdp.Toolkit.Terrain.TerrainFeature.Clutter => TerrainPickCatalog.ClutterIconKey(Template),
        Fdp.Toolkit.Terrain.TerrainFeature.Wall or Fdp.Toolkit.Terrain.TerrainFeature.Fence or Fdp.Toolkit.Terrain.TerrainFeature.Hedge => "terrain/walls",
        Fdp.Toolkit.Terrain.TerrainFeature.Wood => "terrain/trees",
        Fdp.Toolkit.Terrain.TerrainFeature.Scrub => "terrain/orchard",
        Fdp.Toolkit.Terrain.TerrainFeature.Tree => "terrain/vegetation",
        Fdp.Toolkit.Terrain.TerrainFeature.Water => "terrain/water",
        Fdp.Toolkit.Terrain.TerrainFeature.Bridge => "terrain/bridge",
        Fdp.Toolkit.Terrain.TerrainFeature.Scatter => "terrain/clutter",
        Fdp.Toolkit.Terrain.TerrainFeature.Ground => "terrain/source",   // ⭐ CE-4592 (P-5) — the earthworks: the mountain glyph
        _ => null,
    };

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

    /// <summary>⭐ CE-4592 (P-5; mockup <c>docs/UX/ground-shaping-and-ditches.svg</c> panel A) — the 7th group: ground shapes and ditches.</summary>
    public const string Earthworks = "Earthworks";

    /// <summary>The seven terrain groups, in sidebar order (B-5; ⭐ P-5 adds Earthworks last).</summary>
    public static readonly IReadOnlyList<string> Groups = new[] { Blocks, Buildings, Vegetation, Walls, Clutter, Water, Earthworks };

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
            // ⭐ CE-4592 (P-5, G-14; mockup panel A) — ground shapes: an area levelled, raised or lowered; the newest drawn wins where they overlap
            F(Earthworks, TerrainFeature.Ground, "Level", "level", TerrainShape.Area, "flatten an area at one height (cut and fill)"),
            F(Earthworks, TerrainFeature.Ground, "Raise", "raise", TerrainShape.Area, "raise an area — a hill, a bank"),
            F(Earthworks, TerrainFeature.Ground, "Lower", "lower", TerrainShape.Area, "lower an area — a hollow"),
            F(Earthworks, TerrainFeature.Ground, "Crater", "crater", TerrainShape.Point, "a crater left by a blast", "A pre-shelled crater is a saved crater record (V-14) — not yet."),
            F(Earthworks, TerrainFeature.Ground, "Ditch", "ditch", TerrainShape.Line, "a lowered line 2 × 1 m: cover, and no vehicle crosses it"),
            F(Earthworks, TerrainFeature.Ground, "Anti-tank ditch", "at-ditch", TerrainShape.Line, "a ditch 4 m wide, 2 m deep: stops every vehicle"),
            F(Earthworks, TerrainFeature.Ground, "Foxhole", "foxhole", TerrainShape.Point, "a fighting hole 2 × 1.5 × 1 m along its heading"),
        };
        foreach (var t in Fdp.Toolkit.Terrain.ClutterTypes.All)
            l.Add(F(Clutter, TerrainFeature.Clutter, t.Name, t.Id, TerrainShape.Point, $"{t.Length:0.#} × {t.Width:0.#} m, {t.Material}"));
        return l;
    }

    /// <summary>⭐ The recipes that can be SCATTERED (they carry a <c>scatter</c> section) — (id, name, items per 100 m²): what the drawing panel's <i>What</i> lists. Empty until backend ships one (T-3).</summary>
    public static IReadOnlyList<(string Id, string Name, float Density)> ScatterRecipes(IEnumerable<string?> recipeRoots)
    {
        var l = new List<(string, string, float)>();
        foreach (var root in recipeRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*" + TerrainRecipe.Suffix))
                try { var r = TerrainRecipe.Parse(File.ReadAllText(file), file); if (r.Scatter.Count > 0 && !l.Exists(x => x.Item1 == r.Id)) l.Add((r.Id, Title(r.Id), r.ScatterDensity)); }
                catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
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
        var scatter = new List<(string Id, string Name, float Density)>();
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
                    if (recipe.Scatter.Count > 0) scatter.Add((recipe.Id, Title(recipe.Id), recipe.ScatterDensity));
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
        // ⭐ the area scatter is placeable as soon as ONE shipped recipe carries a `scatter` section (backend's T-3): its pick is then lit and starts on that recipe; until then it stays greyed with its reason
        if (scatter.Count > 0)
            for (int i = 0; i < list.Count; i++)
                if (list[i].FeatureKind == TerrainFeature.Scatter) list[i] = list[i] with { Disabled = null, Template = scatter[0].Id };
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

    /// <summary>
    /// ⭐ Terrain authoring follow-up 2 — how many houses a recipe makes and from which templates: its classic <c>houses</c> section, else the sum of its <c>elements</c> that place buildings (a <c>row</c>, a <c>cluster</c>, a <c>grid</c>: their <c>count</c>, or <c>cols × rows</c>)
    /// — backend's T-3 recipes carry no <c>houses</c> section any more, and the tile line, the pane and the ghost's "does it fit" all asked <see cref="TerrainRecipe.HousesMax"/>.
    /// </summary>
    public static (int Min, int Max, IReadOnlyList<string> Templates) HouseRange(TerrainRecipe r)
    {
        if (r.HousesMax > 0) return (r.HousesMin, r.HousesMax, r.Templates);
        int min = 0, max = 0; var templates = new List<string>();
        static (int, int) Range(JsonNode? n) => n switch { JsonArray { Count: 2 } a => (a[0]!.GetValue<int>(), a[1]!.GetValue<int>()), JsonValue v when v.TryGetValue<int>(out var i) => (i, i), _ => (0, 0) };
        foreach (var el in r.Elements)
        {
            if (el["templates"] is not JsonArray ts || ts.Count == 0) continue;
            var (a, b) = el["type"]?.GetValue<string>() == "grid" ? (Range(el["cols"]).Item1 * Range(el["rows"]).Item1, Range(el["cols"]).Item2 * Range(el["rows"]).Item2) : Range(el["count"]);
            min += a; max += b;
            foreach (var t in ts) if (t?.GetValue<string>() is { } name && !templates.Contains(name)) templates.Add(name);
        }
        return (min, max, templates);
    }

    private static TerrainPick OfRecipe(TerrainRecipe r, string json)
    {
        var ui = (JsonNode.Parse(json) as JsonObject)?["ui"] as JsonObject;
        string group = (string?)ui?["group"] is { Length: > 0 } g && IsTerrainGroup(g) ? g : Blocks;
        string name = (string?)ui?["name"] is { Length: > 0 } n ? n : Title(r.Id);
        var (hMin, hMax, hTemplates) = HouseRange(r);
        string? chip = (string?)ui?["chip"] is { Length: > 0 } c ? c : hMax > 0 ? "Rural" : "Nature";
        string houses = hMax == 0 ? "" : hMin == hMax ? $"{hMax} houses" : $"{hMin}-{hMax} houses";
        string woods = r.WoodsMax == 0 ? "" : r.WoodsMin == r.WoodsMax ? $"{r.WoodsMax} woods" : $"{r.WoodsMin}-{r.WoodsMax} woods";
        string line = (string?)ui?["line"] is { Length: > 0 } l ? l : string.Join(", ", new[] { houses, woods }.Where(s => s.Length > 0));
        var facts = new List<(string, string)>();
        if (houses.Length > 0) facts.Add(("Houses", houses + " from " + string.Join(", ", hTemplates)));
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

    /// <summary>
    /// ⭐ Terrain authoring follow-up 2 — the shared icon key (<see cref="NodeEditor.Core.Widgets.Icons"/>) of a recipe, the one table every surface reads (the wizard's rows, a chip, a tile): a town block the city, a hamlet and a village the house, a farmstead the tractor, a copse the trees…
    /// A recipe the table does not know gets the Blocks group's own icon, never a blank.
    /// </summary>
    public static string IconKey(string recipeId) => recipeId switch
    {
        "town-block" => "terrain/block",
        "village-street" or "hamlet" => "terrain/buildings",
        "farmstead" => "terrain/farm",
        "copse" => "terrain/trees",
        "orchard" => "terrain/orchard",
        "hedgerow-fields" => "terrain/walls",
        "checkpoint" => "terrain/checkpoint",
        "roadblock" => "terrain/roadblock",
        "defensive-position" => "terrain/defence",
        _ => "terrain/block",
    };

    /// <summary>The icon key of a clutter type (<c>ClutterTypes</c> id); any type the table does not list gets the Clutter group's icon.</summary>
    public static string ClutterIconKey(string typeId) => typeId switch
    {
        "car" => "clutter/car", "barrel" => "clutter/barrel", "crate" => "clutter/crate", "concrete-block" => "clutter/block",
        "jersey-barrier" => "clutter/barrier", "sandbag-wall" => "clutter/sandbags", "guard-booth" => "clutter/booth",
        _ => "terrain/clutter",
    };

    /// <summary>The plural a count reads as (<c>Town blocks</c>, <c>Hamlets</c>, <c>Woods and copses</c>): the shipped recipes by name, any other <see cref="Title"/> + <c>s</c>.</summary>
    public static string Plural(string recipeId) => recipeId switch
    {
        "village-street" => "Village streets", "copse" => "Woods and copses", "hedgerow-fields" => "Hedgerow fields", "defensive-position" => "Defensive positions",
        _ => Title(recipeId) + "s",
    };

    /// <summary><c>village-street</c> → <c>Village street</c>.</summary>
    public static string Title(string id)
    {
        string s = id.Replace('-', ' ').Replace('_', ' ');
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
