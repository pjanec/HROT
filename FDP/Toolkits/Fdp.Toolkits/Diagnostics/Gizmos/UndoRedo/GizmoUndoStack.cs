using System;
using System.Collections.Generic;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo
{
    /// <summary>
    /// Manages undo/redo history for gizmo interactions.
    /// Not thread-safe — call only from the ECS/render thread.
    /// </summary>
    public sealed class GizmoUndoStack
    {
        private readonly Stack<IGizmoUndoRecord> _undoStack = new();
        private readonly Stack<IGizmoUndoRecord> _redoStack = new();

        public int MaxDepth { get; init; } = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public string UndoDescription => CanUndo ? _undoStack.Peek().Description : string.Empty;
        public string RedoDescription => CanRedo ? _redoStack.Peek().Description : string.Empty;

        /// <summary>
        /// Records a new committed action. Clears the redo stack (new branch).
        /// Drops the oldest entry if depth would exceed <see cref="MaxDepth"/>.
        /// </summary>
        public void Push(IGizmoUndoRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            // If at capacity, rebuild stack without the oldest (bottom) entry.
            if (_undoStack.Count >= MaxDepth)
            {
                var items = _undoStack.ToArray(); // index 0 = top (newest)
                _undoStack.Clear();
                // Re-push all except the last (oldest, index Count-1), then the new record.
                for (int i = items.Length - 2; i >= 0; i--)
                    _undoStack.Push(items[i]);
            }

            _undoStack.Push(record);
            _redoStack.Clear();
        }

        /// <summary>
        /// Performs the undo operation. No-op if <see cref="CanUndo"/> is false.
        /// </summary>
        public void Undo(IEntityCommandBuffer cmd)
        {
            if (!CanUndo) return;
            var record = _undoStack.Pop();
            record.Undo(cmd);
            _redoStack.Push(record);
        }

        /// <summary>
        /// Performs the redo operation. No-op if <see cref="CanRedo"/> is false.
        /// </summary>
        public void Redo(IEntityCommandBuffer cmd)
        {
            if (!CanRedo) return;
            var record = _redoStack.Pop();
            record.Redo(cmd);
            _undoStack.Push(record);
        }

        /// <summary>
        /// Clears both undo and redo history. Call on world/scenario reset.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
