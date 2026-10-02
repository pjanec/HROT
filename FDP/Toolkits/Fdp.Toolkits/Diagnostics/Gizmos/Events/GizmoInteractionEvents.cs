using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Events
{
    [EventId(8051)]
    public struct GizmoInteractionStartedEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;

        /// <summary>
        /// ⭐⭐⭐ <b>WHICH BUTTON STARTED IT.</b> 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.3 / §2.7.14
        /// (<c>UXI-11</c> <c>S-4b</c>).
        ///
        /// <para>🔴 <b>Why it had to exist.</b> A left-press and a right-release on an entity both emitted
        /// this event with nothing to tell them apart, so <c>SelectionInteractionSystem</c> treated every
        /// press as a left-click and issued an unconditional <c>Replace</c> ⇒ right-clicking one of five
        /// selected entities <b>collapsed the selection to one</b>, which §2.3 row 1 forbids and the
        /// <c>2026-09-10</c> fan-out ruling depends on.</para>
        ///
        /// <para>⭐⭐ <b>The rule is BUTTON-SPECIFIC, which is why a shared guard would not have done.</b>
        /// Left-press must keep replacing unconditionally — that is how an operator narrows a
        /// five-selection down to one by clicking a member. Only the right-press is exempted.</para>
        ///
        /// <para>⚠ <b><c>MapMouseButton.Left</c> is <c>0</c></b>, so every producer that does not set this
        /// field keeps its previous meaning exactly — including the DDS record's <c>ActionId</c>, which
        /// carries it on the wire and also defaults to <c>0</c>. ⛔ Not a silent default: it is the value
        /// the un-migrated producers genuinely had.</para>
        ///
        /// <para>⭐ It also carries <c>MapMouseButton</c>'s <c>ShiftMask</c>/<c>CtrlMask</c>/<c>AltMask</c>,
        /// which is what <c>SelectionInteractionSystem</c>'s multi-select <c>TODO</c> was waiting for.</para>
        /// </summary>
        public MapMouseButton Button;
    }

    [EventId(8052)]
    public struct GizmoDragUpdateEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
        // GZ047: coordinate space in which WorldPos is expressed.
        public CoordinateSpace Space;
    }

    [EventId(8053)]
    public struct GizmoInteractionCommitEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
        // GZ047: coordinate space in which WorldPos is expressed.
        public CoordinateSpace Space;
    }

    [EventId(8054)]
    public struct GizmoInteractionCancelEvent
    {
        public PickToken Token;
    }

    /// <summary>
    /// Published by the IG presentation layer (DebugGizmoLayer) when the operator
    /// selects an item from a gizmo-stream context menu.
    /// Consumed by <see cref="Hrot.Network.NED.Gizmos.GizmoInteractionEgressSystem"/>
    /// which forwards it to the SimHost as a <c>GizmoInteractionBatch</c> record
    /// with <c>Kind = MenuAction</c> and the clicked <see cref="ActionId"/>.
    /// </summary>
    [EventId(8055)]
    public struct GizmoMenuActionEvent
    {
        /// <summary>Network-level entity ID of the entity whose menu was shown.</summary>
        public long AnchorId;

        /// <summary>Integer ID of the menu item that was clicked.</summary>
        public int ActionId;

        /// <summary>Composite routing key; 0 for legacy events.</summary>
        public uint GizmoTypeId;
    }

    /// <summary>
    /// Published by <see cref="Hrot.Network.NED.Gizmos.GizmoInteractionIngressSystem"/> when
    /// a <c>RawInput</c> batch record carries a mouse button event (stateFlags bit7 = 1).
    /// Routed by <see cref="DataDrivenGizmoSystem"/> to the gizmo that holds exclusive focus.
    /// </summary>
    [EventId(8056)]
    public struct GizmoMouseEvent
    {
        public PickToken Token;
        public MapMouseButton Button;
        public bool IsPressed;
        public Vector3 WorldPos;
    }

    /// <summary>
    /// Published by <see cref="Hrot.Network.NED.Gizmos.GizmoInteractionIngressSystem"/> when
    /// a <c>RawInput</c> batch record carries a keyboard event (stateFlags bit7 = 0).
    /// Routed by <see cref="DataDrivenGizmoSystem"/> to the gizmo that holds exclusive focus.
    /// </summary>
    [EventId(8057)]
    public struct GizmoKeyEvent
    {
        public PickToken Token;
        public MapKeyboardKey Key;
        public bool IsPressed;
    }

    /// <summary>
    /// Published when an ECS component that is part of a GizmoRegistry rule is added
    /// to an entity that already exists (i.e., the entity was not just constructed).
    /// DataDrivenGizmoSystem processes this to late-activate any matching gizmo rules.
    /// </summary>
    [EventId(8058)]
    public struct GizmoComponentActivatedEvent
    {
        /// <summary>The entity whose component mask may now satisfy a registered gizmo rule.</summary>
        public Entity Entity;
    }

    /// <summary>
    /// Published by <see cref="GizmoInteractionIngressTranslator"/> (or equivalent) when a
    /// <c>StructUpdate</c> DDS record carries a JSON mutation committed by the operator via
    /// a <c>StructInspector</c> panel on the terminal.
    /// Routed by <see cref="GlobalGizmoManager"/> to the target gizmo via
    /// <see cref="IGizmoInteractionHandler.OnStructUpdate"/>.
    /// </summary>
    [EventId(8060)]
    public sealed class GizmoStructUpdateEvent
    {
        /// <summary>Stable gizmo anchor id; matches the key in <c>GlobalGizmoManager</c>.</summary>
        public long AnchorId;
        /// <summary>Composite routing key; 0 for legacy / GlobalGizmoManager-routed events.</summary>
        public uint GizmoTypeId;
        /// <summary>JSON string produced by the StructEdit Apply action on the terminal.</summary>
        public string PayloadJson = string.Empty;
    }
}
