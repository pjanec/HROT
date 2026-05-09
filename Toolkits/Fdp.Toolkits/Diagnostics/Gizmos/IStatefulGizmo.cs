using System;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// Per-entity stateful gizmo managed by <c>DataDrivenGizmoSystem</c> or
    /// <c>BehaviorGizmoManagerSystem</c>. Replaces the old <c>IStatefulGizmo</c>.
    ///
    /// Design (gizmo-input-focus-design.md §7):
    /// - View and entity are passed at construction time and stored by the implementation.
    ///   They do NOT appear on <c>UpdateAndDraw</c> (no per-call bloat).
    /// - <c>OnInitialize</c>/<c>OnTeardown</c> are removed; the constructor establishes
    ///   invariants and <see cref="IDisposable.Dispose"/> tears them down.
    /// - Extends <see cref="IGizmoInteractionHandler"/> for strongly typed input events.
    /// - <c>IsFocused</c> is set by the manager; the gizmo uses it to branch visual style.
    /// - <c>CreateUndoRecord</c> is kept as a virtual default method (opt-in undo support).
    /// </summary>
    public interface IEntityStatefulGizmo : IGizmoInteractionHandler, IDisposable
    {
        /// <summary>
        /// Called once per frame for every active gizmo, regardless of focus state.
        /// The gizmo emits visual primitives via <paramref name="drawBuilder"/>.
        /// The view and entity were stored at construction time.
        /// </summary>
        void UpdateAndDraw(float deltaTime, IDebugDrawBuilder drawBuilder);

        /// <summary>
        /// Returns an undo record for the most recent committed interaction, or
        /// <c>null</c> if this gizmo does not support undo.
        /// Called by <see cref="DataDrivenGizmoSystem"/> after processing
        /// <see cref="GizmoInteractionCommitEvent"/>.
        /// </summary>
        virtual IGizmoUndoRecord? CreateUndoRecord(GizmoInteractionCommitEvent commit) => null;
    }
}
