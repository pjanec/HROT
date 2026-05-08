using System.Collections.Generic;

namespace GizmoMap.Presentation
{
    public interface IGizmoUndoRecord
    {
        void Undo();
    }

    /// <summary>
    /// Minimal gizmo undo stack. No ECS dependencies.
    /// </summary>
    public sealed class GizmoUndoStack
    {
        private readonly Stack<IGizmoUndoRecord> _records = new();

        public void Push(IGizmoUndoRecord record) => _records.Push(record);

        public bool TryUndo(out IGizmoUndoRecord? record)
        {
            if (_records.Count == 0)
            {
                record = null;
                return false;
            }
            record = _records.Pop();
            return true;
        }
    }
}
