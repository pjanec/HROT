using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using GizmoMap.Network;

namespace GizmoMap.Example
{
    // ECS-agnostic manager that owns the lifecycle and focus arbitration for all
    // stateful gizmos. Implements IGizmoSource so the host loop calls Emit once per frame.
    //
    // Design rules (from gizmo-input-focus-design.md section 8):
    // - Registry keyed by stable long AnchorId; O(1) dispatch.
    // - Only the holder of the exclusive-focus slot may have InputCaptureBinding emitted.
    // - The MANAGER emits InputCaptureBinding on the gizmo's behalf; gizmos do not emit it.
    // - Removing a tool disposes it and releases the focus lock.
    public sealed class GizmoInteractionManager : IGizmoSource
    {
        private readonly Dictionary<long, IStatefulGizmo> _activeTools = new();
        private IStatefulGizmo? _exclusiveFocusHolder;
        private long _exclusiveFocusAnchorId;

        public bool HasTool(long anchorId) => _activeTools.ContainsKey(anchorId);

        public void AddTool(long anchorId, IStatefulGizmo tool)
        {
            _activeTools[anchorId] = tool;
            if (tool.RequiresExclusiveFocus && _exclusiveFocusHolder == null)
            {
                _exclusiveFocusHolder   = tool;
                _exclusiveFocusAnchorId = anchorId;
                tool.SetFocus(true);
            }
        }

        public void RemoveTool(long anchorId)
        {
            if (_activeTools.Remove(anchorId, out var tool))
            {
                if (_exclusiveFocusHolder == tool)
                {
                    tool.SetFocus(false);
                    _exclusiveFocusHolder   = null;
                    _exclusiveFocusAnchorId = 0;
                }
                tool.Dispose();
            }
        }

        // Dispatches a gizmo interaction event to the tool registered under token.AnchorId.
        // For RawInput events: stateFlags bit7=1 mouse/0 keyboard; bit0=1 pressed/0 released.
        // actionId is (int)MapMouseButton or (int)MapKeyboardKey.
        public void DispatchEvent(
            GizmoPickToken token,
            GizmoInteractionEventKind kind,
            Vector3 worldPos,
            int actionId,
            byte stateFlags)
        {
            if (!_activeTools.TryGetValue(token.AnchorId, out var tool)) return;

            switch (kind)
            {
                case GizmoInteractionEventKind.Started:
                    tool.OnInteractionStarted(token, worldPos);
                    break;
                case GizmoInteractionEventKind.DragUpdate:
                    tool.OnDragUpdate(worldPos);
                    break;
                case GizmoInteractionEventKind.Commit:
                    tool.OnCommit(worldPos);
                    break;
                case GizmoInteractionEventKind.Cancel:
                    tool.OnCancel();
                    break;
                case GizmoInteractionEventKind.MenuAction:
                    tool.OnMenuAction(actionId);
                    break;
                case GizmoInteractionEventKind.RawInput:
                    bool isMouse   = (stateFlags & 0x80) != 0;
                    bool isPressed = (stateFlags & 0x01) != 0;
                    if (isMouse)
                        tool.OnMouseEvent((MapMouseButton)actionId, isPressed, worldPos);
                    else
                        tool.OnKeyEvent((MapKeyboardKey)actionId, isPressed);
                    break;
            }
        }

        // IGizmoSource implementation. Calls UpdateAndDraw on every tool, then emits
        // InputCaptureBinding(Exclusive=true) for the exclusive-focus holder so the
        // terminal routes all raw HW events to it next frame.
        public void Emit(float deltaTime, IGizmoDrawBuilder draw)
        {
            foreach (var (anchorId, tool) in _activeTools)
            {
                tool.UpdateAndDraw(deltaTime, draw);

                if (tool == _exclusiveFocusHolder)
                {
                    var binding = DebugPrimitive.MakeInputCaptureBinding(
                        networkId: anchorId, subElementId: 0, exclusive: true);
                    draw.EmitRaw(in binding);
                }
            }
        }
    }
}
