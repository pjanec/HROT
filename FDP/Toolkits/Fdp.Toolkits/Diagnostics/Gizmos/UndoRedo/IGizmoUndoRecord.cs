using Fdp.Interfaces;

namespace Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo
{
    /// <summary>
    /// Encapsulates a reversible gizmo interaction. Implementations are created by
    /// stateful gizmos via <see cref="IStatefulGizmo.CreateUndoRecord"/>.
    /// </summary>
    public interface IGizmoUndoRecord
    {
        /// <summary>Human-readable label for status bar (e.g. "Move entity 42").</summary>
        string Description { get; }

        /// <summary>
        /// Re-applies the committed change. Called when the user triggers Redo.
        /// Must be idempotent.
        /// </summary>
        void Redo(IEntityCommandBuffer cmd);

        /// <summary>
        /// Reverts the committed change. Called when the user triggers Undo.
        /// Must be idempotent.
        /// </summary>
        void Undo(IEntityCommandBuffer cmd);
    }
}
