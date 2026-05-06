using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A per-entity stateful gizmo instance managed by <c>DataDrivenGizmoSystem</c>.
    /// The system owns the lifecycle: OnInitialize on construction, UpdateAndDraw each frame,
    /// OnTeardown on entity destruction.
    /// </summary>
    public interface IStatefulGizmo
    {
        void OnInitialize(ISimulationView view, Entity entity);

        void UpdateAndDraw(ISimulationView view, Entity entity, float deltaTime,
                           IDebugDrawBuilder drawBuilder);

        void OnTeardown();

        /// <summary>
        /// Returns an undo record for the most recent committed interaction, or
        /// <c>null</c> if this gizmo does not support undo.
        /// Default implementation returns <c>null</c> (opt-out).
        /// Called by <see cref="DataDrivenGizmoSystem"/> after processing
        /// <see cref="GizmoInteractionCommitEvent"/>.
        /// </summary>
        virtual IGizmoUndoRecord? CreateUndoRecord(GizmoInteractionCommitEvent commit) => null;
    }
}
