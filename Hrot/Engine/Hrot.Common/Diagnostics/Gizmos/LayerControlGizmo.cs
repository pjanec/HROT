using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.UI;
using Hrot.Common.Constants;
using StructEdit.Core;
using StructEdit.Core.Attributes;

namespace Hrot.Common.Diagnostics.Gizmos
{
    // Raised by the action registry when the operator selects
    // "View > Tactical Map Layers..." from the main menu bar.
    // Consumed by LayerControlGizmo.UpdateAndDraw to toggle the StructInspector panel.
    [EventId(8061)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct OpenLayerEditorEvent { }

    /// <summary>⭐ CE-1033 — the operator asked for the other map mode (2-D ↔ 3-D); the map's view switch drains it.</summary>
    [EventId(8062)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ToggleMap3DEvent { }

    /// <summary>⭐ CE-1201 (docs/DESIGN_Entity_Control.md §8p OS-1) — View › First Person: Over the Shoulder; the map's view-mode layer
    /// drains it and toggles the station's view.</summary>
    [EventId(8065)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ToggleShoulderViewEvent { }

    /// <summary>⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-2) — a unit's menu "Follow with the camera": the map's view-mode layer drains it
    /// and puts the 3-D camera on the unit. ⚠ 8066 measured free on every lane 2026-10-23.</summary>
    [EventId(8066)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct FollowUnitEvent { public Entity Target; }

    // DTO that matches the StructEdit schema used by the StructInspector panel.
    // Must be JSON-serializable; property names match the schema produced by the terminal.
    // ⭐ CE-1065 (docs/DESIGN_Terrain_Combat_Tuning.md §5d) — 🔒 user, 2026-10-14: "overwhelmed with the amount of gizmos shown on the
    //   scene, especially in the 3d map. I would like to turn most of them off by default". OFF by default: AI helpers, Fire traces,
    //   Blast, Hearing, Unit details, Spatial grid. Each is still one checkbox away.
    // ⭐⭐ CE-1203 (§5e, R-1011) — one checkbox per KIND of drawing (FL-1), the new ones ON = what was drawn before (FL-2), in five
    //   nested groups the panel draws as tree nodes (FL-4), each row with a display name and a hover line (FL-5: [EditDisplayName],
    //   [EditDoc] — StructEdit's own field documentation). A family's "selected only" tick sits right under its row.
    //   ⛔ SUPERSEDED: one flat list of rows; "Perception" (bit 1) is now Ai.VisionCones, its contact lines Ai.Contacts (bit 25).
    public class LayerControlDto
    {
        /// <summary>⭐ CE-1203 FL-8 — which view this set belongs to ("2-D map" / "3-D map"); the panel prints it as its first line (a
        /// text row, not editable), and <see cref="LayerControlGizmo.OnStructUpdate"/> drops an edit made to the OTHER view's set (a click
        /// in the frame the view switched).</summary>
        [EditDisplayName("Showing"), EditDoc("Each view keeps its own set: switch the map to 2-D or 3-D to edit that view's layers.")]
        public string View { get; set; } = LayerControlSets.View2D;

        [EditDisplayName("Units"), EditDoc("The units themselves: symbols, cards, unit details, rounds in flight.")]
        public UnitLayerGroup Units { get; set; } = new();
        [EditDisplayName("Command"), EditDoc("Who commands whom, and where units are told to go.")]
        public CommandLayerGroup Command { get; set; } = new();
        [EditDisplayName("AI"), EditDoc("What the AI sees, thinks and is doing.")]
        public AiLayerGroup Ai { get; set; } = new();
        [EditDisplayName("Fire"), EditDoc("Shots, bursts and sounds.")]
        public FireLayerGroup Fire { get; set; } = new();
        [EditDisplayName("Map"), EditDoc("The terrain and what is drawn on it.")]
        public MapLayerGroup Map { get; set; } = new();

        /// <summary>The first layer bit no toggle owns; every bit from here up is always on.</summary>
        public const int FirstUntoggledLayer = 29;   // ⭐ CE-1288 — 27 → 28: the Grid row took bit 27 · ⭐ CE-1309 — 28 → 29: the Mind state row took bit 28

        /// <summary>⭐ <c>CE-3120</c> — reads the families' scopes from <paramref name="settings"/> (and, <c>CE-3133</c>, the navmesh layer choice).</summary>
        public void ReadScopes(GizmoSettingsRegistry settings)
        {
            GizmoFamilies.Register(settings);
            Command.PathsSelectedOnly        = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path);
            NavmeshLayerSetting.Register(settings);
            Map.NavmeshLayers                = NavmeshLayerSetting.Of(settings);   // ⭐ CE-3133
        }

        /// <summary>⭐ <c>CE-3120</c> — writes the families' scopes into <paramref name="settings"/> (and, <c>CE-3133</c>, the navmesh layer choice).</summary>
        public void WriteScopes(GizmoSettingsRegistry settings)
        {
            GizmoFamilies.Register(settings);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path,            Command.PathsSelectedOnly);
            NavmeshLayerSetting.Register(settings);
            if (NavmeshLayerSetting.Of(settings) != Map.NavmeshLayers) NavmeshLayerSetting.Set(settings, Map.NavmeshLayers);   // ⭐ CE-3133
        }

        private static bool Selected(GizmoSettingsRegistry s, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags f) =>
            GizmoFamilies.ScopeOf(s, f) == GizmoScope.SelectedOrPinned;

        private static void Set(GizmoSettingsRegistry s, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags f, bool selectedOnly)
        {
            var scope = selectedOnly ? GizmoScope.SelectedOrPinned : GizmoScope.All;
            if (GizmoFamilies.ScopeOf(s, f) != scope) GizmoFamilies.SetScope(s, f, scope);
        }

        // Returns the 256-bit layer visibility mask derived from the DTO flags.
        public LayerMask256 ToMask()
        {
            var mask = new LayerMask256();
            if (Units.Entities) mask.SetBit(0);
            if (Units.Labels) mask.SetBit(DebugTraceLayers.Labels);
            if (Units.UnitDetails) mask.SetBit(DebugTraceLayers.UnitDetails);
            if (Units.Rounds) mask.SetBit(DebugTraceLayers.Rounds);
            if (Units.Concealed) mask.SetBit(DebugTraceLayers.Concealed);
            if (Units.MindState) mask.SetBit(DebugTraceLayers.MindState);
            if (Command.SquadCommand) mask.SetBit(DebugTraceLayers.SquadCommand);
            if (Command.Sectors) mask.SetBit(DebugTraceLayers.Sectors);
            if (Command.Paths) mask.SetBit(DebugTraceLayers.Paths);
            if (Command.Routes) mask.SetBit(DebugTraceLayers.Routes);
            if (Ai.VisionCones) mask.SetBit(1);
            if (Ai.Contacts) mask.SetBit(DebugTraceLayers.Contacts);
            if (Ai.Actions) mask.SetBit(DebugTraceLayers.Actions);
            if (Ai.Utility) mask.SetBit(DebugTraceLayers.Utility);
            if (Ai.Eqs) mask.SetBit(DebugTraceLayers.Eqs);
            if (Ai.AiHelpers) mask.SetBit(2);
            if (Fire.FireTraces) mask.SetBit(3);
            if (Fire.Blast) mask.SetBit(DebugTraceLayers.Blast);
            if (Fire.Hearing) mask.SetBit(DebugTraceLayers.Hearing);
            if (Map.Terrain2D) mask.SetBit(DebugTraceLayers.Terrain2D);
            if (Map.Grid) mask.SetBit(DebugTraceLayers.Grid);
            if (Map.TerrainZones) mask.SetBit(DebugTraceLayers.TerrainZones);
            if (Map.Roads) mask.SetBit(DebugTraceLayers.Roads);
            if (Map.Traffic) mask.SetBit(DebugTraceLayers.Traffic);
            if (Map.TacticalGraphics) mask.SetBit(DebugTraceLayers.TacticalGraphics);
            if (Map.Doors) mask.SetBit(DebugTraceLayers.Doors);
            if (Map.Cover) mask.SetBit(DebugTraceLayers.Cover);
            if (Map.Navmesh) mask.SetBit(DebugTraceLayers.Navmesh);
            if (Map.SpatialGrid) mask.SetBit(DebugTraceLayers.SpatialGrid);
            for (int i = FirstUntoggledLayer; i < 256; i++) mask.SetBit(i);
            return mask;
        }
    }

    // ⭐⭐ CE-1329 (docs/DESIGN_Entity_Brain_View.md §12r BV-67) — the panel holds GLOBAL switches only: a brain family's row (Vision cones,
    //   Contacts, Actions, Utility, EQS, Squad & command) turns it on / off for every unit, and it draws for the units ticked on their Brain
    //   tab or by the right-click Pin gizmos. ⛔ Their six "selected only" rows are gone (the per-unit switch is the tick).
    // ⭐ CE-1203 FL-4 — the five groups. A family's scope ("selected only": draw for the selected and pinned units only, unticked = for
    //   every unit; CE-3120, written to the registry as map.scope.<Family>) sits under its row. Defaults are GizmoFamilies.DefaultScope's.

    /// <summary>⭐ CE-1203 — the Units group.</summary>
    public class UnitLayerGroup
    {
        [EditDisplayName("Entities"), EditDoc("Unit symbols and silhouettes (and what you click to select them).")]
        public bool Entities { get; set; } = true;
        [EditDisplayName("Labels"), EditDoc("The unit cards: frame, name, health bar (CE-1033).")]
        public bool Labels { get; set; } = true;
        [EditDisplayName("Unit details"), EditDoc("Heading arrows with their degrees and stance labels on every unit (CE-1065).")]
        public bool UnitDetails { get; set; }
        [EditDisplayName("Rounds"), EditDoc("Rounds and grenades in flight or lying on the ground.")]
        public bool Rounds { get; set; } = true;
        [EditDisplayName("Concealed (ExCon)"), EditDoc("Exercise control: outline what each person carries HIDDEN (a worn charge, a concealed pistol) and name it on his card. Off by default (CE-1169).")]
        public bool Concealed { get; set; }
        [EditDisplayName("Mind state"), EditDoc("A bar under each stressed soldier's health bar: his stress, a white tick at his breaking point (his nerve), red and BROKEN when he breaks; a frightened civilian's fear. Where the brains run (the editor). Off by default (CE-1309).")]
        public bool MindState { get; set; }
    }

    /// <summary>⭐ CE-1203 — the Command group.</summary>
    public class CommandLayerGroup
    {
        [EditDisplayName("Squad & command"), EditDoc("Leader-to-member lines, the squad's phase and shared contacts; a commander's reported picture.")]
        public bool SquadCommand { get; set; } = true;
        [EditDisplayName("Sectors"), EditDoc("Units' sector areas, NoFire areas and the commanders' arcs (CE-1225).")]
        public bool Sectors { get; set; }
        [EditDisplayName("Paths"), EditDoc("The planned path and look-ahead, and the lines to a unit's mission tasks.")]
        public bool Paths { get; set; } = true;
        [EditDisplayName("Paths: selected only"), EditDoc("Paths for the selected and pinned units only; unticked = every unit.")]
        public bool PathsSelectedOnly { get; set; } = true;
        [EditDisplayName("Routes"), EditDoc("Route entities: waypoints joined by lines.")]
        public bool Routes { get; set; } = true;
    }

    /// <summary>⭐ CE-1203 — the AI group.</summary>
    public class AiLayerGroup
    {
        [EditDisplayName("Vision cones"), EditDoc("What each unit can see: its view cone.")]
        public bool VisionCones { get; set; } = true;
        [EditDisplayName("Contacts"), EditDoc("Lines to the enemies a unit remembers; a heard one is dashed, with its uncertainty ring.")]
        public bool Contacts { get; set; } = true;
        [EditDisplayName("Actions"), EditDoc("What a unit is busy doing (e.g. 'W aiming 0.4/0.8s'), and its peek-and-fire slots and phase.")]
        public bool Actions { get; set; } = true;
        [EditDisplayName("Utility"), EditDoc("Each utility decision's winner and its margin over the runner-up.")]
        public bool Utility { get; set; } = true;
        [EditDisplayName("EQS"), EditDoc("Position queries: the search radius, the scored candidates, green kept / red rejected.")]
        public bool Eqs { get; set; } = true;
        [EditDisplayName("AI helpers"), EditDoc("The navigation arrow of each moving unit and the danger areas.")]
        public bool AiHelpers { get; set; }
    }

    /// <summary>⭐ CE-1203 — the Fire group.</summary>
    public class FireLayerGroup
    {
        [EditDisplayName("Fire traces"), EditDoc("Each shot from muzzle to impact, coloured by its outcome (Tuning T-5).")]
        public bool FireTraces { get; set; }
        [EditDisplayName("Blast"), EditDoc("Bursts: their radii and fragment rays.")]
        public bool Blast { get; set; }
        [EditDisplayName("Hearing"), EditDoc("Sound rings and where a unit estimates a heard sound came from.")]
        public bool Hearing { get; set; }
    }

    /// <summary>⭐ CE-1203 — the Map group.</summary>
    public class MapLayerGroup
    {
        [EditDisplayName("Terrain (2-D)"), EditDoc("The 2-D map's ground cover, buildings, walls and their labels (CE-1227).")]
        public bool Terrain2D { get; set; } = true;
        [EditDisplayName("Grid (2-D)"), EditDoc("The 2-D map's helper grid: thin grey lines for the fine step, thicker for the coarse one, the step chosen by the zoom (CE-1288).")]
        public bool Grid { get; set; } = true;
        [EditDisplayName("Terrain zones"), EditDoc("The terrain zones and whether each is loaded.")]
        public bool TerrainZones { get; set; } = true;
        [EditDisplayName("Roads"), EditDoc("The loaded terrain's road network.")]
        public bool Roads { get; set; } = true;
        [EditDisplayName("Traffic"), EditDoc("SUMO traffic: lanes, lights, pedestrians and cars that are not entities.")]
        public bool Traffic { get; set; } = true;
        [EditDisplayName("Tactical graphics"), EditDoc("Tactical-graphic areas and drawn overlay lines.")]
        public bool TacticalGraphics { get; set; } = true;
        [EditDisplayName("Doors"), EditDoc("Door leaves, coloured by state.")]
        public bool Doors { get; set; } = true;
        [EditDisplayName("Cover"), EditDoc("Cover points and window firing positions (a town holds hundreds).")]
        public bool Cover { get; set; }
        [EditDisplayName("Navmesh"), EditDoc("The baked navmesh (the whole layer).")]
        public bool Navmesh { get; set; }
        /// <summary>⭐ <c>CE-3133</c> — which navmesh the Navmesh layer draws (R-233: Infantry by default). Kept in the settings registry
        /// (<see cref="NavmeshLayerSetting"/>), read and written with the family scopes.</summary>
        [EditDisplayName("Navmesh: which layer"), EditDoc("Which navmesh the Navmesh row draws (Infantry by default).")]
        public NavmeshDrawLayers NavmeshLayers { get; set; } = NavmeshLayerSetting.Default;
        [EditDisplayName("Spatial grid"), EditDoc("The spatial hash grid.")]
        public bool SpatialGrid { get; set; }
    }

    /// <summary>
    /// ⭐ CE-1203 FL-8/FL-9 (docs/DESIGN_Terrain_Combat_Tuning.md §5e) — the two sets' rows as settings in the shared
    /// <see cref="GizmoSettingsRegistry"/> (<c>map.layers.2d.&lt;Group&gt;.&lt;Row&gt;</c>, <c>map.layers.3d.…</c>, bool), so the registry's own
    /// SaveToDisk / LoadFromDisk carry them (with the family scopes <c>map.scope.*</c> and the navmesh choice <c>map.navmesh.layers</c>,
    /// already settings) to the user's file. The rows are found by reflection over the groups — a new row is remembered with no list to
    /// update. The "selected only" ticks are NOT rows: they are the family scopes, one per family for both views.
    /// </summary>
    public static class LayerControlSets
    {
        public const string View2D = "2-D map", View3D = "3-D map";

        private static readonly (System.Reflection.PropertyInfo Group, System.Reflection.PropertyInfo Row)[] Rows =
            typeof(LayerControlDto).GetProperties()
                .Where(g => g.PropertyType.IsClass && g.PropertyType != typeof(string))
                .SelectMany(g => g.PropertyType.GetProperties()
                    .Where(r => r.PropertyType == typeof(bool) && !r.Name.EndsWith("SelectedOnly", StringComparison.Ordinal))
                    .Select(r => (g, r)))
                .ToArray();

        /// <summary>The setting key of one row in one view's set.</summary>
        public static string Key(bool is3D, string group, string row) => $"map.layers.{(is3D ? "3d" : "2d")}.{group}.{row}";

        /// <summary>The number of rows a set remembers (the rail reads it).</summary>
        public static int RowCount => Rows.Length;

        /// <summary>⭐ CE-4038 B10 (docs/DESIGN_Film_Director.md §4b) — every row as <c>Group.Row</c> (the DTO's own names) with the name and the
        /// hover text the Layers panel shows, so a take author or an agent never guesses.</summary>
        public static System.Collections.Generic.IReadOnlyList<(string Name, string Display, string Doc)> RowNames { get; } =
            Rows.Select(x => ($"{x.Group.Name}.{x.Row.Name}",
                              x.Row.GetCustomAttributes(typeof(EditDisplayNameAttribute), false).OfType<EditDisplayNameAttribute>().FirstOrDefault()?.Name ?? x.Row.Name,
                              x.Row.GetCustomAttributes(typeof(EditDocAttribute), false).OfType<EditDocAttribute>().FirstOrDefault()?.Summary ?? ""))
                .ToArray();

        /// <summary>⭐ CE-4038 B10 — the rows a film keeps as the user has them when a take "starts clean": the scene itself (units, rounds, the
        /// ground, roads, traffic, doors), not the overlays and helpers (🔒 "Overlays like labels optional, Default is off").</summary>
        /// <remarks>⭐ <c>CE-1379</c> (docs/DESIGN_Director_Timeline_Editor.md §11t "as built") — and the SCENARIO's own drawing: its tactical graphics
        /// (areas, overlay lines) and its route entities — what the scenario author put on the map, not a helper.</remarks>
        public static readonly System.Collections.Generic.IReadOnlyList<string> SceneRows = new[]
            { "Units.Entities", "Units.Rounds", "Map.Terrain2D", "Map.Roads", "Map.Traffic", "Map.Doors", "Map.TacticalGraphics", "Command.Routes" };

        /// <summary>
        /// ⭐ <c>CE-1379</c> / <c>CE-1380</c> (§11t "as built") — the gizmo layer bits a FILM frame may draw: the bits the Layers panel's rows toggle
        /// (<see cref="LayerControlDto.FirstUntoggledLayer"/> and up are never filmed: the selection ring, the always-on tool and helper bits). Which of
        /// them a frame shows is the layer set in force — on screen and in a film alike (a film's set starts clean: <see cref="SceneRows"/>).
        /// </summary>
        public static LayerMask256 FilmLayers
        {
            get
            {
                var mask = new LayerMask256();
                for (int i = 0; i < LayerControlDto.FirstUntoggledLayer; i++) mask.SetBit(i);
                return mask;
            }
        }

        /// <summary>One row of <paramref name="dto"/> by its <c>Group.Row</c> name; false when no such row.</summary>
        public static bool TryGet(LayerControlDto dto, string name, out bool value)
        {
            value = false;
            foreach (var (g, r) in Rows)
                if (string.Equals($"{g.Name}.{r.Name}", name, StringComparison.OrdinalIgnoreCase)) { value = (bool)r.GetValue(g.GetValue(dto))!; return true; }
            return false;
        }

        /// <summary>Sets one row of <paramref name="dto"/> by its <c>Group.Row</c> name; false when no such row.</summary>
        public static bool TrySet(LayerControlDto dto, string name, bool value)
        {
            foreach (var (g, r) in Rows)
                if (string.Equals($"{g.Name}.{r.Name}", name, StringComparison.OrdinalIgnoreCase)) { r.SetValue(g.GetValue(dto), value); return true; }
            return false;
        }

        /// <summary>Registers both sets' rows with the DTO's defaults (idempotent; the 3-D set starts as a copy of the 2-D defaults).</summary>
        public static void Register(GizmoSettingsRegistry settings)
        {
            var defaults = new LayerControlDto();
            foreach (var (g, r) in Rows)
                for (int v = 0; v < 2; v++)
                    settings.RegisterSetting(Key(v == 1, g.Name, r.Name), GizmoSettingValue.From((bool)r.GetValue(g.GetValue(defaults))!));
        }

        /// <summary>One view's rows from the registry into <paramref name="dto"/>.</summary>
        public static void Read(GizmoSettingsRegistry settings, LayerControlDto dto, bool is3D)
        {
            foreach (var (g, r) in Rows)
                r.SetValue(g.GetValue(dto), settings.Read(GizmoSettingsRegistry.ComputeHash(Key(is3D, g.Name, r.Name))).BoolValue);
        }

        /// <summary><paramref name="dto"/>'s rows into the registry as one view's set (only the rows that changed are written).</summary>
        public static void Write(GizmoSettingsRegistry settings, LayerControlDto dto, bool is3D)
        {
            foreach (var (g, r) in Rows)
            {
                uint hash = GizmoSettingsRegistry.ComputeHash(Key(is3D, g.Name, r.Name));
                bool value = (bool)r.GetValue(g.GetValue(dto))!;
                if (settings.Read(hash).BoolValue != value) settings.Write(hash, GizmoSettingValue.From(value));
            }
        }
    }

    // Stateful backend gizmo that owns layer visibility state for the tactical map.
    //
    // Each frame it emits:
    //   - A LayerControlMask primitive (authoritative 256-bit mask consumed by dumb terminal).
    //   - A MainMenuBinding primitive (injects "View > Tactical Map Layers..." into the menu bar).
    //   - Optionally a StructInspector primitive (ImGui property panel, when _isEditing = true).
    //
    // Interaction flow:
    //   Operator clicks menu item -> GlobalActionIds.OpenLayerControl action ->
    //   interactionBus.Publish(new OpenLayerEditorEvent()) ->
    //   UpdateAndDraw drains the event -> _isEditing toggled ->
    //   StructInspector panel appears on terminal ->
    //   Operator clicks a checkbox (⭐ CE-1065: each click commits; no Apply) ->
    //   GizmoStructUpdateEvent with PayloadJson routed here via OnStructUpdate ->
    //   _dto updated, _activeLayers recomputed; the panel stays open until the menu entry toggles it shut.
    public sealed class LayerControlGizmo : IEntityStatefulGizmo
    {
        // Schema hash computed from the DTO's full type name — matches what the terminal
        // derives via reflection, with no magic numbers.
        public static readonly uint SchemaHash =
            GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!);

        // JSON for the "View" top-level main menu entry (priority=30 places it after standard menus).
        private static readonly string MainMenuJson =
            "[{\"label\":\"View\",\"priority\":30,\"children\":[{\"id\":"
            + GlobalActionIds.OpenLayerControl
            + ",\"label\":\"Tactical Map Layers...\"},{\"id\":"
            + GlobalActionIds.ToggleMap3D            // ⭐ CE-1033 — the map's 2-D / 3-D switch, on every map host
            + ",\"label\":\"2-D / 3-D Map\"},{\"id\":"
            + GlobalActionIds.ToggleShoulderView     // ⭐ CE-1201 (§8p OS-1)
            + ",\"label\":\"First Person: Over the Shoulder\"}]}]";

        /// <summary>⭐ CE-3171 — the same View menu plus "RTS command mode", where the map can order units
        /// (<see cref="Hrot.Common.Orders.OrdersOffered"/>; never in a replay).</summary>
        private static readonly string MainMenuJsonWithOrders =
            "[{\"label\":\"View\",\"priority\":30,\"children\":[{\"id\":"
            + GlobalActionIds.OpenLayerControl
            + ",\"label\":\"Tactical Map Layers...\"},{\"id\":"
            + GlobalActionIds.ToggleMap3D
            + ",\"label\":\"2-D / 3-D Map\"},{\"id\":"
            + GlobalActionIds.ToggleRtsCommand
            + ",\"label\":\"RTS Command Mode\"},{\"id\":"
            + GlobalActionIds.ToggleShoulderView
            + ",\"label\":\"First Person: Over the Shoulder\"}]}]";

        private readonly long _anchorId;
        private readonly FdpEventBus _interactionBus;
        private readonly StructInspectorProjector<LayerControlDto> _projector;

        // ⭐⭐ CE-1203 FL-8 (§5e) — TWO sets, one per view; each frame the gizmo asks the host's view switch (Is3D, wired by
        //   MapInteractionPack.AttachMapLayers on every map host) and emits THAT set's mask and THAT set's panel.
        //   ⛔ SUPERSEDED: one set for both views (private LayerControlDto _dto).
        private readonly LayerControlDto[] _sets = { new() { View = LayerControlSets.View2D }, new() { View = LayerControlSets.View3D } };
        private int _view;   // 0 = 2-D, 1 = 3-D
        private LayerControlDto Current => _sets[_view];
        private LayerMask256 _activeLayers;
        private readonly Action? _saveSettings;   // ⭐ CE-1203 FL-9 — writes the user's file; called on every change

        /// <summary>⭐ CE-1203 FL-8 — the map shows 3-D. Wired by <c>MapInteractionPack.AttachMapLayers</c> (the view switch); unset = 2-D.</summary>
        public Func<bool>? Is3D { get; set; }

        /// <summary>⭐ CE-1203 FL-8 — the set of one view (the rails read it).</summary>
        public LayerControlDto SetOf(bool is3D) => _sets[is3D ? 1 : 0];

        /// <summary>⭐ CE-1227 — the mask this gizmo asserts (what <see cref="UpdateAndDraw"/> hands the buffer each frame). The map pack
        /// seeds the buffer with it at build, so the FIRST frame refuses hidden layers too.</summary>
        public LayerMask256 ActiveLayers => _activeLayers;
        private bool _isEditing;
        private readonly GizmoSettingsRegistry? _settings;   // ⭐ CE-3120 — where the family scopes live

        private LayerControlDto? _override;

        /// <summary>
        /// ⭐⭐ CE-4038 B10 (docs/DESIGN_Film_Director.md §4b, R-4060) — a film take's layers: a TRANSIENT set laid over the user's (both
        /// views), dropped by <see cref="ClearOverride"/>. ⛔ Never written to the settings registry — that registry is saved to the user's
        /// file, and a take must not change the user's map. Unknown names are returned, not dropped.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> SetOverride(System.Collections.Generic.IEnumerable<string> on, System.Collections.Generic.IEnumerable<string> off)
        {
            var unknown = new System.Collections.Generic.List<string>();
            var dto = new LayerControlDto { View = Current.View };
            foreach (var (name, _, _) in LayerControlSets.RowNames) LayerControlSets.TrySet(dto, name, false);
            foreach (var name in LayerControlSets.SceneRows) LayerControlSets.TrySet(dto, name, LayerControlSets.TryGet(Current, name, out var v) && v);
            foreach (var name in on) if (!LayerControlSets.TrySet(dto, name, true)) unknown.Add(name);
            foreach (var name in off) if (!LayerControlSets.TrySet(dto, name, false)) unknown.Add(name);
            _override = dto;
            _activeLayers = dto.ToMask();
            return unknown;
        }

        /// <summary>⭐ CE-4038 B10 — the user's own set again.</summary>
        public void ClearOverride()
        {
            _override = null;
            _activeLayers = Current.ToMask();
        }

        /// <summary>The override in force (null: the user's set).</summary>
        public LayerControlDto? Override => _override;

        /// <summary>
        /// ⭐ CE-4038 B10 (§4b, D-13) — <c>POST /map/layers</c>: rows turned on and off in one view's set EXACTLY as the user's click does —
        /// written to the registry and saved to the user's file. Returns the unknown names.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> SetRows(bool is3D, System.Collections.Generic.IEnumerable<string> on, System.Collections.Generic.IEnumerable<string> off)
        {
            var unknown = new System.Collections.Generic.List<string>();
            int v = is3D ? 1 : 0;
            var edited = System.Text.Json.JsonSerializer.Deserialize<LayerControlDto>(System.Text.Json.JsonSerializer.Serialize(_sets[v], Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;
            foreach (var name in on) if (!LayerControlSets.TrySet(edited, name, true)) unknown.Add(name);
            foreach (var name in off) if (!LayerControlSets.TrySet(edited, name, false)) unknown.Add(name);
            _sets[v] = edited;
            if (v == _view && _override == null) _activeLayers = edited.ToMask();
            if (_settings != null)
            {
                LayerControlSets.Write(_settings, edited, is3D);
                _saveSettings?.Invoke();
            }
            return unknown;
        }

        /// <summary>The view on screen is the 3-D map.</summary>
        public bool Shows3D => _view == 1;

        // IGizmoInteractionHandler
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public LayerControlGizmo(
            long anchorId,
            FdpEventBus interactionBus,
            IComponentEditService editService,
            IGizmoUiStatePublisher? uiPublisher = null,
            GizmoSettingsRegistry? settings = null,
            Action? saveSettings = null)
        {
            _anchorId = anchorId;
            _settings = settings;
            _saveSettings = saveSettings;
            if (settings != null)
            {
                // ⭐ CE-1203 FL-9 — both sets from the registry (the host loaded the user's file into it before building this gizmo)
                LayerControlSets.Register(settings);
                for (int v = 0; v < 2; v++)
                {
                    LayerControlSets.Read(settings, _sets[v], is3D: v == 1);
                    _sets[v].ReadScopes(settings);
                }
            }
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
            _projector = new StructInspectorProjector<LayerControlDto>(
                editService ?? throw new ArgumentNullException(nameof(editService)),
                uiPublisher);
            _activeLayers = Current.ToMask();
        }

        // Called once per frame by GlobalGizmoManager regardless of focus state.
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            // Drain OpenLayerEditorEvent to toggle the inspector panel.
            foreach (ref readonly var _ in _interactionBus.Read<OpenLayerEditorEvent>())
                _isEditing = !_isEditing;

            // ⭐ CE-1203 FL-8 — the view decides the set. The family scopes are one per family (not per view), so a switched-to set
            //   re-reads them.
            int shown = Is3D?.Invoke() == true ? 1 : 0;
            if (shown != _view)
            {
                _view = shown;
                if (_settings != null) Current.ReadScopes(_settings);
                _activeLayers = (_override ?? Current).ToMask();   // ⭐ CE-4038 B10 — a take's override holds across the view switch
            }

            // Emit authoritative layer control mask (consumed by DebugPrimitiveRenderer2D).
            // ⭐ CE-1071 — and hand the same mask to the buffer, which then refuses hidden-layer primitives at emit (one authority,
            //   applied earlier: hidden layers no longer fill the frame and push the mask, menus and tool bindings out of it).
            draw.SetActiveLayers(in _activeLayers);
            var maskPrim = DebugPrimitive.MakeLayerControlMask(_activeLayers);
            draw.EmitRaw(in maskPrim);

            // Inject "View > Tactical Map Layers..." into the host main menu bar.
            draw.DrawMainMenuBinding(Hrot.Common.Orders.OrdersOffered.IsOfferedIn(view) ? MainMenuJsonWithOrders : MainMenuJson);   // ⭐ CE-3171

            // Emit StructInspector panel when editing is active.
            if (_isEditing)
                _projector.EmitAndSync(draw, _anchorId, SchemaHash, Current, ScreenAnchor.Center, SizeMode.ScreenPercent);
        }

        // Called by GlobalGizmoManager when a GizmoStructUpdateEvent arrives for _anchorId.
        public void OnStructUpdate(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return;
            var edited = System.Text.Json.JsonSerializer.Deserialize<LayerControlDto>(System.Text.Json.JsonSerializer.Serialize(Current, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed)!;   // a copy: the edit applies on top of the shown set
            _projector.ApplyUpdate(payloadJson, ref edited);
            if (edited.View != Current.View) return;   // ⭐ CE-1203 FL-8 — an edit of the set the panel showed before the view switched
            _sets[_view] = edited;
            _activeLayers = (_override ?? edited).ToMask();
            if (_settings != null)
            {
                edited.WriteScopes(_settings);
                LayerControlSets.Write(_settings, edited, is3D: _view == 1);   // ⭐ CE-1203 FL-9
                _saveSettings?.Invoke();
            }
            // ⭐ CE-1065 — the panel STAYS OPEN: each checkbox now commits as it is clicked (ImGuiPropertyTreeAdapter), so closing
            //   here made every second toggle a trip back through View ▸ Tactical Map Layers…. The menu entry toggles it shut.
        }

        /// <summary>⭐ CE-1066 — the panel's [X]: stop emitting it (the View menu entry opens it again).</summary>
        public void OnInspectorClosed() => _isEditing = false;

        // No-op stubs for IGizmoInteractionHandler methods not used by this gizmo.
        // Menu actions arrive as OpenLayerEditorEvent via the action registry, not OnMenuAction.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMenuAction(int actionId) { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
        public void Dispose() { }
    }
}
