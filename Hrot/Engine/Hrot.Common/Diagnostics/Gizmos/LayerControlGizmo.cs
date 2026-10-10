using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.UI;
using Hrot.Common.Constants;
using StructEdit.Core;

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

    // DTO that matches the StructEdit schema used by the StructInspector panel.
    // Must be JSON-serializable; property names match the schema produced by the terminal.
    public class LayerControlDto
    {
        public bool Entities { get; set; } = true;
        public bool Perception { get; set; } = true;
        public bool AiHelpers { get; set; } = true;
        /// <summary>⭐ Tuning T-5 — the fire traces (FireTraceGizmo, layer 3).</summary>
        public bool FireTraces { get; set; } = true;
        /// <summary>⭐ <c>CE-3117</c> — door leaves by state (DoorLeafGizmo, layer 4).</summary>
        public bool Doors { get; set; } = true;
        /// <summary>⭐ <c>CE-3117</c> — the selected mover's planned path and look-ahead (PlannedPathGizmo, layer 5).</summary>
        public bool Paths { get; set; } = true;
        /// <summary>⭐ <c>CE-3117</c> — bursts: radii and fragment rays (DetonationGizmo, layer 6).</summary>
        public bool Blast { get; set; } = true;
        /// <summary>⭐ <c>CE-3117</c> — sound rings and heard estimates (HearingGizmo, layer 7).</summary>
        public bool Hearing { get; set; } = true;
        /// <summary>⭐ <c>CE-3124</c> — the loaded terrain's road network (RoadNetworkGizmo, layer 8).</summary>
        public bool Roads { get; set; } = true;
        /// <summary>⭐ Stage 7a (<c>CE-3134</c>) — cover points and window firing positions (CoverPointsGizmo, layer 9). Off by
        /// default: a town holds hundreds of points.</summary>
        public bool Cover { get; set; }
        /// <summary>⭐ <c>CE-3133</c> — the baked navmesh (NavmeshGizmo, layer 10). Off by default: it draws the whole layer.</summary>
        public bool Navmesh { get; set; }
        /// <summary>⭐ <c>CE-3133</c> — which navmesh the Navmesh layer draws (R-233: Infantry by default). Kept in the settings registry
        /// (<see cref="NavmeshLayerSetting"/>), read and written with the family scopes.</summary>
        public NavmeshDrawLayers NavmeshLayers { get; set; } = NavmeshLayerSetting.Default;

        /// <summary>The first layer bit no toggle owns; every bit from here up is always on.</summary>
        public const int FirstUntoggledLayer = 11;

        // ⭐ CE-3120 (R-227) — per gizmo FAMILY: true = draw only for the selected and the pinned units, false = for every unit.
        //   Written into the GizmoSettingsRegistry on Apply (map.scope.<Family>), where GizmoFamilyVisibilityPolicy reads it.
        //   Defaults are GizmoFamilies.DefaultScope's. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
        public bool PathSelectedOnly { get; set; } = true;
        public bool PerceptionSelectedOnly { get; set; }
        public bool ContactsSelectedOnly { get; set; }
        public bool EqsSelectedOnly { get; set; } = true;   // ⭐ CE-3143 — EQS draws every candidate's verdict: selected by default
        public bool UtilitySelectedOnly { get; set; } = true;
        public bool SquadSelectedOnly { get; set; } = true;
        /// <summary>⭐ <c>CE-3136</c> — the action status (why a unit is not firing): selected and pinned units by default.</summary>
        public bool ActionsSelectedOnly { get; set; } = true;

        /// <summary>⭐ <c>CE-3120</c> — reads the families' scopes from <paramref name="settings"/> (and, <c>CE-3133</c>, the navmesh layer choice).</summary>
        public void ReadScopes(GizmoSettingsRegistry settings)
        {
            GizmoFamilies.Register(settings);
            PathSelectedOnly       = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path);
            PerceptionSelectedOnly = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception);
            ContactsSelectedOnly   = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory);
            EqsSelectedOnly        = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Eqs);
            UtilitySelectedOnly    = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.UtilityDecision);
            SquadSelectedOnly      = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.SquadAssignment);
            ActionsSelectedOnly    = Selected(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Channels);
            NavmeshLayerSetting.Register(settings);
            NavmeshLayers          = NavmeshLayerSetting.Of(settings);   // ⭐ CE-3133
        }

        /// <summary>⭐ <c>CE-3120</c> — writes the families' scopes into <paramref name="settings"/> (and, <c>CE-3133</c>, the navmesh layer choice).</summary>
        public void WriteScopes(GizmoSettingsRegistry settings)
        {
            GizmoFamilies.Register(settings);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path,            PathSelectedOnly);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Perception,      PerceptionSelectedOnly);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.TargetMemory,    ContactsSelectedOnly);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Eqs,             EqsSelectedOnly);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.UtilityDecision, UtilitySelectedOnly);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.SquadAssignment, SquadSelectedOnly);
            Set(settings, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Channels,        ActionsSelectedOnly);
            NavmeshLayerSetting.Register(settings);
            if (NavmeshLayerSetting.Of(settings) != NavmeshLayers) NavmeshLayerSetting.Set(settings, NavmeshLayers);   // ⭐ CE-3133
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
            if (Entities) mask.SetBit(0);
            if (Perception) mask.SetBit(1);
            if (AiHelpers) mask.SetBit(2);
            if (FireTraces) mask.SetBit(3);
            if (Doors) mask.SetBit(DebugTraceLayers.Doors);
            if (Paths) mask.SetBit(DebugTraceLayers.Paths);
            if (Blast) mask.SetBit(DebugTraceLayers.Blast);
            if (Hearing) mask.SetBit(DebugTraceLayers.Hearing);
            if (Roads) mask.SetBit(DebugTraceLayers.Roads);
            if (Cover) mask.SetBit(DebugTraceLayers.Cover);
            if (Navmesh) mask.SetBit(DebugTraceLayers.Navmesh);
            for (int i = FirstUntoggledLayer; i < 256; i++) mask.SetBit(i);
            return mask;
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
    //   Operator edits and clicks Apply ->
    //   GizmoStructUpdateEvent with PayloadJson routed here via OnStructUpdate ->
    //   _dto updated, _activeLayers recomputed.
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
            + ",\"label\":\"2-D / 3-D Map\"}]}]";

        private readonly long _anchorId;
        private readonly FdpEventBus _interactionBus;
        private readonly StructInspectorProjector<LayerControlDto> _projector;

        private LayerControlDto _dto = new();
        private LayerMask256 _activeLayers;
        private bool _isEditing;
        private readonly GizmoSettingsRegistry? _settings;   // ⭐ CE-3120 — where the family scopes live

        // IGizmoInteractionHandler
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public LayerControlGizmo(
            long anchorId,
            FdpEventBus interactionBus,
            IComponentEditService editService,
            IGizmoUiStatePublisher? uiPublisher = null,
            GizmoSettingsRegistry? settings = null)
        {
            _anchorId = anchorId;
            _settings = settings;
            if (settings != null) _dto.ReadScopes(settings);
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
            _projector = new StructInspectorProjector<LayerControlDto>(
                editService ?? throw new ArgumentNullException(nameof(editService)),
                uiPublisher);
            _activeLayers = _dto.ToMask();
        }

        // Called once per frame by GlobalGizmoManager regardless of focus state.
        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            // Drain OpenLayerEditorEvent to toggle the inspector panel.
            foreach (ref readonly var _ in _interactionBus.Read<OpenLayerEditorEvent>())
                _isEditing = !_isEditing;

            // Emit authoritative layer control mask (consumed by DebugPrimitiveRenderer2D).
            var maskPrim = DebugPrimitive.MakeLayerControlMask(_activeLayers);
            draw.EmitRaw(in maskPrim);

            // Inject "View > Tactical Map Layers..." into the host main menu bar.
            draw.DrawMainMenuBinding(MainMenuJson);

            // Emit StructInspector panel when editing is active.
            if (_isEditing)
                _projector.EmitAndSync(draw, _anchorId, SchemaHash, _dto, ScreenAnchor.Center, SizeMode.ScreenPercent);
        }

        // Called by GlobalGizmoManager when a GizmoStructUpdateEvent arrives for _anchorId.
        public void OnStructUpdate(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return;
            _projector.ApplyUpdate(payloadJson, ref _dto);
            _activeLayers = _dto.ToMask();
            if (_settings != null) _dto.WriteScopes(_settings);
            _isEditing = false;
        }

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
