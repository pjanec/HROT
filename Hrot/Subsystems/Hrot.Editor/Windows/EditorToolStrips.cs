using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Tools;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.Windows;

/// <summary>
/// ⭐ CE-5006 W2 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-1 / TE-19, R-5038) — the editor's SCENARIO tools: a dockable tool strip (<see cref="ToolStripWindow"/>; a column or a row by its own size) holding the
/// <i>Add Entity</i> family (Friendly · Hostile · Neutral · Map graphic — the EXISTING map-menu handlers, run through the host's <see cref="GlobalActionRegistry"/>) and <i>Measure</i>. Replaces the old Editor Toolbar.
/// 🔒 The armed-tool rule (Tool Model step 5): an entry is lit while <c>ToolController.ActiveModal</c> is the tool it armed.
/// </summary>
internal sealed class ScenarioToolsWindow : ToolStripWindow
{
    internal const string WindowId = "scenario_tools";

    private readonly Func<GlobalActionRegistry?> _registry;
    private readonly Func<ISimulationView?> _world;
    private readonly Func<string?> _activeTool;
    private readonly List<ToolStripEntry> _entries = new();

    /// <summary>The Add Entity family's members, in order: the action each runs.</summary>
    internal static readonly (string Label, int ActionId, string Glyph, System.Numerics.Vector4 Colour)[] AddMembers =
    {
        ("Friendly", GlobalActionIds.AddEntityFriendly, "", IconPalette.Blue),
        ("Hostile", GlobalActionIds.AddEntityHostile, "", IconPalette.Red),
        ("Neutral", GlobalActionIds.AddEntityNeutral, "", IconPalette.Green),
        ("Map graphic", GlobalActionIds.AddMapGraphic, "", IconPalette.Teal),
    };

    public ScenarioToolsWindow(Func<GlobalActionRegistry?> registry, Func<ISimulationView?> world, Func<string?> activeTool)
        : base(WindowId, "Scenario tools", "Scenario", WindowScope.PerspectiveBound)
    {
        _registry = registry; _world = world; _activeTool = activeTool;
        IsOpen = true;
        TitleBarColor = EditorWindowColor.TitleBar;
        var add = new List<ToolStripMember>();
        foreach (var m in AddMembers)
        {
            int id = m.ActionId;
            add.Add(new ToolStripMember(new IconGlyph(m.Glyph, m.Colour), m.Label, "Add a " + m.Label.ToLowerInvariant() + " — pick the type, then click the map", () => Run(id)));
        }
        _entries.Add(new ToolStripEntry("##ts_add_entity", add[0].Glyph, "Add Entity") { Family = add, IsLit = () => _activeTool() == ScenarioToolIds.Spawn });
        _entries.Add(new ToolStripEntry("##ts_measure", new IconGlyph("", IconPalette.Amber), "Measure — click two points")
        { OnClick = () => Run(GlobalActionIds.Measure), IsLit = () => _activeTool() == ScenarioToolIds.Measure });
    }

    protected override IReadOnlyList<ToolStripEntry> Entries => _entries;

    /// <summary>The entries (for tests).</summary>
    internal IReadOnlyList<ToolStripEntry> StripEntries => _entries;

    /// <summary>Runs the existing handler of <paramref name="actionId"/> from the host's registry (no target: the map's own empty-space click).</summary>
    internal void Run(int actionId)
    {
        if (_registry() is { } r && _world() is { } w && r.TryGetHandler(actionId, out var h)) h(w, Entity.Null);
    }
}

/// <summary>
/// ⭐ CE-5006 W2 (TE-1, TE-17, TE-18, TE-19) — the TERRAIN tool strip: closed by default; one family per group of picks (Wood · Trees · Walls · Obstacles · Earthworks · Water · Scatter). 🔑 While it is OPEN every drawn frame sets
/// <see cref="TerrainLock.SetOpen"/> on the editor's CURRENT world (so placed terrain is selectable), and a closed frame clears it. Lit = the family this strip armed last while a tool is still armed.
/// </summary>
internal sealed class TerrainToolsWindow : ToolStripWindow
{
    internal const string WindowId = "terrain_tools";

    private readonly Func<EntityRepository?> _world;
    private readonly Func<ISpawnController?> _spawn;
    private readonly Func<string?> _activeTool;
    private readonly List<ToolStripEntry> _entries = new();
    private string? _lastArmed;

    /// <summary>The placed-obstacle types the strip offers (<see cref="TkbEntityTypes.Obstacles"/>): label and the shared icon key of each.</summary>
    internal static readonly (long Type, string Label, string IconKey)[] ObstacleMembers =
    {
        (TkbEntityTypes.Obstacle_Car, "Car", "clutter/car"),
        (TkbEntityTypes.Obstacle_SandbagWall, "Sandbag wall", "clutter/sandbags"),
        (TkbEntityTypes.Obstacle_ConcreteBlock, "Concrete block", "clutter/block"),
        (TkbEntityTypes.Obstacle_Crate, "Crate", "clutter/crate"),
    };

    public TerrainToolsWindow(Func<EntityRepository?> world, Func<ISpawnController?> spawn, Func<string?> activeTool,
                              IReadOnlyList<TerrainPick> picks, IReadOnlyList<(string Id, string Name, float Density)> scatterRecipes)
        : base(WindowId, "Terrain", "Scenario", WindowScope.PerspectiveBound)
    {
        _world = world; _spawn = spawn; _activeTool = activeTool;
        IsOpen = false;   // ⭐ closed by default (R-5038): the Terrain toolbar is what makes placed terrain selectable
        TitleBarColor = EditorWindowColor.TitleBar;
        var feature = picks.Where(p => p.Kind == TerrainPickKind.Feature && p.Disabled is null).ToList();
        void Family(string id, string name, string iconKey, IEnumerable<TerrainPick> members)
        {
            var list = members.Select(p => Member(id, p, p.Name, p.GlyphKey ?? iconKey)).ToList();
            if (list.Count > 0) _entries.Add(Entry(id, name, list));
        }
        Family("wood", "Wood", "terrain/trees", feature.Where(p => p.FeatureKind is TerrainFeature.Wood or TerrainFeature.Scrub));
        Family("trees", "Trees", "terrain/vegetation", feature.Where(p => p.FeatureKind is TerrainFeature.Tree or TerrainFeature.Row or TerrainFeature.Group));   // ⭐ CE-5008 (TE-3): One · Row · Group
        Family("walls", "Walls", "terrain/walls", feature.Where(p => p.FeatureKind is TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge));
        var obstacles = ObstacleMembers.Select(o => new ToolStripMember(Icons.Get(o.IconKey), o.Label, "Place a " + o.Label.ToLowerInvariant() + " (an obstacle entity)", () => { _lastArmed = "obstacles"; _spawn()?.StartPlacementMode(o.Type); })).ToList();
        // ⭐ CE-5008 (TE-5, R-4264 / R-5041) — the row: a LINE tool whose commit makes N obstacle entities in one undo step; its panel picks the type, the gap and the facing
        obstacles.Add(new ToolStripMember(Icons.Get("clutter/block"), "Row of obstacles…", "Drag a line: a row of obstacle entities, one undo step, selected as one group",
            () => { _lastArmed = "obstacles"; _spawn()?.StartAreaAuthoringMode(TkbEntityTypes.TerrainFeature, TerrainBirth.PlacementJson(ObstacleRowOptions.Spec)); }));
        _entries.Add(Entry("obstacles", "Obstacles", obstacles));
        Family("earthworks", "Earthworks", "terrain/source", feature.Where(p => p.Group == TerrainPickCatalog.Earthworks));
        Family("water", "Water", "terrain/water", feature.Where(p => p.FeatureKind is TerrainFeature.Water or TerrainFeature.Bridge));
        var scatterPick = feature.FirstOrDefault(p => p.FeatureKind == TerrainFeature.Scatter);
        if (scatterPick is not null || picks.Any(p => p.FeatureKind == TerrainFeature.Scatter))
        {
            var basePick = scatterPick ?? picks.First(p => p.FeatureKind == TerrainFeature.Scatter) with { Disabled = null };
            var list = scatterRecipes.Select(r => Member("scatter", basePick with { Template = r.Id, Name = r.Name, Disabled = null }, r.Name, r.Id == "undergrowth" ? "terrain/orchard" : "terrain/clutter")).ToList();
            if (list.Count > 0) _entries.Add(Entry("scatter", "Scatter", list));
        }
    }

    private ToolStripEntry Entry(string family, string name, List<ToolStripMember> members)
        => new("##ts_t_" + family, members[0].Glyph, name) { Family = members, IsLit = () => _lastArmed == family && ArmedNow() };

    private ToolStripMember Member(string family, TerrainPick pick, string label, string iconKey)
        => new(Icons.Get(iconKey), label, pick.Line, () => { _lastArmed = family; if (_spawn() is { } s) PlacementToolRegistry.ArmTerrain(s, pick); });

    private bool ArmedNow() => _activeTool() is { } t && t != ScenarioToolIds.Select;

    protected override IReadOnlyList<ToolStripEntry> Entries => _entries;

    /// <summary>The entries (for tests).</summary>
    internal IReadOnlyList<ToolStripEntry> StripEntries => _entries;

    /// <summary>
    /// 🔑 THE ONE per-frame lock rule (the editor calls it every frame — one writer): placed terrain is selectable on the editor's CURRENT world only while this strip is open AND its perspective is the active one.
    /// (The world is read each frame: it changes on a reload.)
    /// </summary>
    internal void UpdateLock(string currentPerspective)
    {
        if (_world() is { } w) TerrainLock.SetOpen(w, IsOpen && BelongsTo(currentPerspective));
    }
}
