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
    public sealed class OpenLayerEditorEvent { }

    // DTO that matches the StructEdit schema used by the StructInspector panel.
    // Must be JSON-serializable; property names match the schema produced by the terminal.
    public class LayerControlDto
    {
        public bool Entities { get; set; } = true;
        public bool Perception { get; set; } = true;
        public bool AiHelpers { get; set; } = true;

        // Returns the 256-bit layer visibility mask derived from the DTO flags.
        public LayerMask256 ToMask()
        {
            var mask = new LayerMask256();
            if (Entities) mask.SetBit(0);
            if (Perception) mask.SetBit(1);
            if (AiHelpers) mask.SetBit(2);
            for (int i = 3; i < 256; i++) mask.SetBit(i);
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
        // Schema hash computed from the DTO's full type name — matches what the terminal
        // derives via reflection, with no magic numbers.
        public static readonly uint SchemaHash =
            GizmoSettingsRegistry.ComputeHash(typeof(LayerControlDto).FullName!);

        // JSON for the "View" top-level main menu entry (priority=30 places it after standard menus).
        private static readonly string MainMenuJson =
            "[{\"label\":\"View\",\"priority\":30,\"children\":[{\"id\":"
            + GlobalActionIds.OpenLayerControl
            + ",\"label\":\"Tactical Map Layers...\"}]}]";

        private readonly long _anchorId;
        private readonly FdpEventBus _interactionBus;
        private readonly StructInspectorProjector<LayerControlDto> _projector;

        private LayerControlDto _dto = new();
        private LayerMask256 _activeLayers;
        private bool _isEditing;

        // IGizmoInteractionHandler
        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public LayerControlGizmo(
            long anchorId,
            FdpEventBus interactionBus,
            IComponentEditService editService,
            IGizmoUiStatePublisher? uiPublisher = null)
        {
            _anchorId = anchorId;
            _interactionBus = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
            _projector = new StructInspectorProjector<LayerControlDto>(
                editService ?? throw new ArgumentNullException(nameof(editService)),
                uiPublisher);
            _activeLayers = _dto.ToMask();
        }

        // Called once per frame by GlobalGizmoManager regardless of focus state.
        public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
        {
            // Drain OpenLayerEditorEvent to toggle the inspector panel.
            foreach (var _ in _interactionBus.ReadManaged<OpenLayerEditorEvent>())
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
