using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Constants;
using StructEdit.Core;
using StructEdit.Json;

namespace Hrot.Common.Diagnostics.Gizmos
{
    // Raised by the action registry when the operator selects
    // "View > Tactical Map Layers..." from the main menu bar.
    // Consumed by LayerControlGizmo.UpdateAndDraw to toggle the StructInspector panel.
    public sealed class OpenLayerEditorEvent { }

    // DTO that matches the StructEdit schema used by the StructInspector panel.
    // Must be JSON-serializable; property names match the schema produced by the terminal.
    public struct LayerControlDto
    {
        public bool BaseLayer { get; set; }
        public bool UnitsLayer { get; set; }
        public bool SensorsLayer { get; set; }

        // Returns the 256-bit layer visibility mask derived from the DTO flags.
        public LayerMask256 ToMask()
        {
            var mask = new LayerMask256();
            if (BaseLayer) mask.SetBit(0);
            if (UnitsLayer) mask.SetBit(1);
            if (SensorsLayer) mask.SetBit(2);
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
    //   interactionBus.PublishManaged(new OpenLayerEditorEvent()) ->
    //   UpdateAndDraw drains the event -> _isEditing toggled ->
    //   StructInspector panel appears on terminal ->
    //   Operator edits and clicks Apply ->
    //   GizmoStructUpdateEvent with PayloadJson routed here via OnStructUpdate ->
    //   _dto updated, _activeLayers recomputed.
    public sealed class LayerControlGizmo : IEntityStatefulGizmo
    {
        // Schema hash must match the StructEdit schema registered for LayerControlDto.
        // Value is shared with GizmoMap.Example.LayerControlGizmo for cross-app consistency.
        public const uint SchemaHash = 0x8899AABB;

        // JSON for the "View" top-level main menu entry (priority=30 places it after standard menus).
        private static readonly string MainMenuJson =
            "[{\"label\":\"View\",\"priority\":30,\"children\":[{\"id\":"
            + GlobalActionIds.OpenLayerControl
            + ",\"label\":\"Tactical Map Layers...\"}]}]";

        private readonly long _anchorId;
        private readonly FdpEventBus _interactionBus;
        private readonly IComponentEditService _editService;

        private LayerControlDto _dto = new() { BaseLayer = true, UnitsLayer = true, SensorsLayer = true };
        private LayerMask256 _activeLayers;
        private bool _isEditing;

        // IGizmoInteractionHandler
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public LayerControlGizmo(long anchorId, FdpEventBus interactionBus, IComponentEditService editService)
        {
            _anchorId = anchorId;
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
            _activeLayers = _dto.ToMask();
        }

        // Called once per frame by GlobalGizmoManager regardless of focus state.
        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
        {
            // Drain OpenLayerEditorEvent to toggle the inspector panel.
            foreach (var _ in _interactionBus.ReadManaged<OpenLayerEditorEvent>())
                _isEditing = !_isEditing;

            // Emit authoritative layer control mask (consumed by DebugPrimitiveRenderer2D).
            var maskPrim = default(DebugPrimitive);
            maskPrim.Shape = DebugPrimitiveShape.LayerControlMask;
            maskPrim.ActiveLayers = _activeLayers;
            draw.EmitRaw(in maskPrim);

            // Inject "View > Tactical Map Layers..." into the host main menu bar.
            draw.DrawMainMenuBinding(MainMenuJson);

            // Emit StructInspector panel when editing is active.
            if (_isEditing)
            {
                var inspPrim = default(DebugPrimitive);
                inspPrim.Shape = DebugPrimitiveShape.StructInspector;
                inspPrim.TargetView = PipelineTarget.All;
                inspPrim.StructNetworkId = _anchorId;
                inspPrim.StructSchemaHash = SchemaHash;
                inspPrim.StructAnchor = ScreenAnchor.Center;
                inspPrim.StructOffsetX = 0f;
                inspPrim.StructOffsetY = 0f;
                inspPrim.SizeMode = SizeMode.ScreenPercent;
                inspPrim.StructIsReadOnly = 0;
                draw.EmitRaw(in inspPrim);
            }
        }

        // Called by GlobalGizmoManager when a GizmoStructUpdateEvent arrives for _anchorId.
        public void OnStructUpdate(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return;
            try
            {
                using IEditSession session = _editService.Open(_dto, typeof(LayerControlDto));
                session.LoadJson(payloadJson);
                _dto = (LayerControlDto)session.Commit();
                _activeLayers = _dto.ToMask();
                _isEditing = false;
            }
            catch (Exception ex)
            {
                FdpLog<LayerControlGizmo>.Warn("Dropped invalid layer StructUpdate: {0}", ex.Message);
            }
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
