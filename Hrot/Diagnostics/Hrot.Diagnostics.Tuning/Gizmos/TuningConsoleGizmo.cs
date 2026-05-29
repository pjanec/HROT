using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Hrot.Diagnostics.Tuning.Gizmos
{
    // Generalized tuning console gizmo. Pattern follows LayerControlGizmo.
    // Injects a main-menu entry and, when editing, emits a StructInspector panel
    // whose mutations are forwarded to TuningRegistry via OnStructUpdate.
    public sealed class TuningConsoleGizmo : IStatefulGizmo
    {
        public const  long AnchorId     = 9001L;
        public const  int  OpenActionId = 260;

        private static readonly uint   SchemaHash =
            Fnv1a32("Hrot.Diagnostics.Tuning.TuningConsoleGizmo");

        // JSON menu descriptor injected into the terminal's main menu bar.
        private static readonly string MainMenuJson =
            "[{\"label\":\"Tools\",\"priority\":50,\"children\":[{\"id\":"
            + OpenActionId
            + ",\"label\":\"AI Tuning Console...\"}]}]";

        private readonly TuningRegistry _registry;
        private bool _isEditing;

        public bool RequiresExclusiveFocus => false;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        public TuningConsoleGizmo(TuningRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        // Exposes the toggle so integration tests can drive the editing state directly.
        public void ToggleEditor() => _isEditing = !_isEditing;

        public void UpdateAndDraw(float deltaTime, IGizmoDrawBuilder draw)
        {
            // Always inject the Tools > AI Tuning Console menu item.
            draw.DrawMainMenuBinding(MainMenuJson);
            if (_isEditing)
            {
                draw.EmitRaw(DebugPrimitive.MakeStructInspector(
                    networkId:  AnchorId,
                    schemaHash: SchemaHash,
                    anchor:     ScreenAnchor.Center,
                    sizeMode:   SizeMode.ScreenPercent,
                    isReadOnly: false));
            }
        }

        public void OnStructUpdate(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.TryGetSingle(out float v))
                        _registry.Apply(new TuningKey(prop.Name), v);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[TuningConsoleGizmo] Dropped invalid StructUpdate: {ex.Message}");
            }
        }

        public void OnMenuAction(int actionId)
        {
            if (actionId == OpenActionId)
                _isEditing = !_isEditing;
        }

        // No-op stubs for IGizmoInteractionHandler methods not used by this gizmo.
        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
        public void OnDragUpdate(Vector3 worldPos) { }
        public void OnCommit(Vector3 worldPos) { }
        public void OnCancel() { }
        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
        public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
        public void Dispose() { }

        private static uint Fnv1a32(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
