using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using StructEdit.Core;
using StructEdit.Json;

namespace GizmoMap.Example
{
    // DTO matching the StructEdit schema for the layer control inspector.
    public class LayerControlDto
    {
        public bool BaseLayer    { get; set; }
        public bool UnitsLayer   { get; set; }
        public bool SensorsLayer { get; set; }

        // Converts the DTO to a LayerMask256 bitmask.
        public LayerMask256 ToMask()
        {
            var mask = new LayerMask256();
            if (BaseLayer)    mask.SetBit(0);
            if (UnitsLayer)   mask.SetBit(1);
            if (SensorsLayer) mask.SetBit(2);
            return mask;
        }
    }

    // Stateful gizmo that drives 256-bit layer visibility from a StructInspector panel.
    //
    // Responsibilities:
    // - Emits a LayerControlMask primitive every frame (authoritative backend state).
    // - Optionally emits a StructInspector panel when _isEditing == true.
    // - Injects a "View" main-menu entry so the operator can toggle the inspector.
    // - Handles OnStructUpdate by parsing the JSON, updating _dto, recomputing _activeLayers.
    public sealed class LayerControlGizmo : IStatefulGizmo
    {
        public const long AnchorId      = 9999L;
        public const uint SchemaHash    = 0x8899AABB;
        public const int  OpenActionId  = 250;

        // JSON for the "View" main menu entry with priority=30.
        private static readonly string MainMenuJson =
            "[{\"label\":\"View\",\"priority\":30,\"children\":[{\"id\":" + OpenActionId + ",\"label\":\"Tactical Map Layers...\"}]}]";

        private readonly IComponentEditService _editService;
        private LayerControlDto _dto = new() { BaseLayer = true, UnitsLayer = true, SensorsLayer = true };
        private LayerMask256    _activeLayers;
        private bool            _isEditing;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public LayerControlGizmo(IComponentEditService editService)
        {
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
            _activeLayers = _dto.ToMask();
        }

        public void ToggleEditor() => _isEditing = !_isEditing;

        // Emits the authoritative LayerControlMask, main menu binding, and optional StructInspector.
        public void UpdateAndDraw(float dt, IGizmoDrawBuilder draw)
        {
            // Always emit the authoritative layer control mask.
            draw.EmitRaw(DebugPrimitive.MakeLayerControlMask(_activeLayers));

            // Inject "View > Tactical Map Layers..." into the main menu bar.
            draw.DrawMainMenuBinding(MainMenuJson);

            // Optionally show the StructInspector panel when the operator requested it.
            if (_isEditing)
            {
                draw.EmitRaw(DebugPrimitive.MakeStructInspector(
                    networkId: AnchorId,
                    schemaHash: SchemaHash,
                    anchor: ScreenAnchor.Center,
                    sizeMode: SizeMode.ScreenPercent,
                    isReadOnly: false));
            }
        }

        // Called by GizmoInteractionManager when the StructInspector panel fires an Apply.
        public void OnStructUpdate(string payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return;
            try
            {
                using var session = _editService.Open(_dto, typeof(LayerControlDto));
                session.LoadJson(payloadJson);
                _dto = (LayerControlDto)session.Commit();
                _activeLayers = _dto.ToMask();
                _isEditing    = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dropped invalid layer StructUpdate: {ex.Message}");
            }
        }

        public void OnMenuAction(int actionId)
        {
            if (actionId == OpenActionId) _isEditing = !_isEditing;
        }

        // No-op for events this gizmo doesn't use.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMouseEvent(MapMouseButton button, bool pressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool pressed) { }
        public void Dispose() { }

    }
}
