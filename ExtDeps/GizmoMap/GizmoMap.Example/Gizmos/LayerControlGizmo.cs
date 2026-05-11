using System;
using System.Numerics;
using System.Text.Json;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace GizmoMap.Example
{
    // DTO matching the StructEdit schema for the layer control inspector.
    public struct LayerControlDto
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

        private LayerControlDto _dto = new() { BaseLayer = true, UnitsLayer = true, SensorsLayer = true };
        private LayerMask256    _activeLayers;
        private bool            _isEditing;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public LayerControlGizmo()
        {
            _activeLayers = _dto.ToMask();
        }

        public void ToggleEditor() => _isEditing = !_isEditing;

        // Emits the authoritative LayerControlMask, main menu binding, and optional StructInspector.
        public void UpdateAndDraw(float dt, IGizmoDrawBuilder draw)
        {
            // Always emit the authoritative layer control mask.
            var maskPrim = default(DebugPrimitive);
            maskPrim.Shape = DebugPrimitiveShape.LayerControlMask;
            maskPrim.ActiveLayers = _activeLayers;
            draw.EmitRaw(in maskPrim);

            // Inject "View > Tactical Map Layers..." into the main menu bar.
            draw.DrawMainMenuBinding(MainMenuJson);

            // Optionally show the StructInspector panel when the operator requested it.
            if (_isEditing)
            {
                var inspPrim = default(DebugPrimitive);
                inspPrim.Shape            = DebugPrimitiveShape.StructInspector;
                inspPrim.TargetView       = PipelineTarget.All;
                inspPrim.StructNetworkId  = AnchorId;
                inspPrim.StructSchemaHash = SchemaHash;
                inspPrim.StructAnchor     = ScreenAnchor.Center;
                inspPrim.StructOffsetX    = 0f;
                inspPrim.StructOffsetY    = 0f;
                inspPrim.SizeMode         = SizeMode.ScreenPercent;
                inspPrim.StructIsReadOnly = 0;
                draw.EmitRaw(in inspPrim);
            }
        }

        // Called by GizmoInteractionManager when the StructInspector panel fires an Apply.
        public void OnStructUpdate(string payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) return;
            try
            {
                var dto = JsonSerializer.Deserialize<LayerControlDto>(payloadJson, _jsonOptions);
                _dto          = dto;
                _activeLayers = dto.ToMask();
                _isEditing    = false;
            }
            catch (JsonException)
            {
                // Malformed payload from terminal: ignore.
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

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };
    }
}
