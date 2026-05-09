using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    // Interaction-handler contract common to all stateful gizmos.
    // Decoupled from ECS, DDS, and presentation specifics.
    //
    // Design rules (from gizmo-input-focus-design.md):
    // - Methods are strongly typed; no chameleon OnInteraction(kind, payload).
    // - OnMouseEvent bundles button, isPressed, and worldPos to avoid interface bloat.
    // - worldPos is always in world space; the backend has no camera to unproject.
    // - RequiresExclusiveFocus is a property inspected once by the host; the gizmo
    //   does not emit InputCaptureBinding itself.
    public interface IGizmoInteractionHandler
    {
        // When true the hosting GizmoInteractionManager emits InputCaptureBinding(Exclusive=true)
        // on behalf of this gizmo, routing all raw HW events to it.
        bool RequiresExclusiveFocus { get; }

        // True while this gizmo holds input focus (exclusive or shared-active).
        // Set by the manager via SetFocus; readable from UpdateAndDraw so the gizmo
        // can alter its visual style when focused.
        bool IsFocused { get; }

        // Called by the manager when focus is granted or revoked.
        // Default no-op so types that do not yet support focus still compile.
        void SetFocus(bool isFocused) { }

        // Spatial / shared interaction -- originating from a hit-test on the terminal.
        // token carries the SubElementId of the hit handle so multi-handle gizmos
        // (e.g. a vertex editor) know which handle was activated.
        void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos);
        void OnDragUpdate(Vector3 worldPos);
        void OnCommit(Vector3 worldPos);
        void OnCancel();

        // Semantic action (context menu item selected).
        void OnMenuAction(int actionId);

        // Raw HW events -- only delivered while exclusive InputCaptureBinding is held.
        void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos);
        void OnKeyEvent(MapKeyboardKey key, bool isPressed);
    }
}
