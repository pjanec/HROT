using NodeEditor.Core.Interfaces;

namespace NodeEditor.Core.Commands;

/// <summary>
/// Undo/redo stack for editor commands. Owned by the editor; not the host.
///
/// The editor produces inverse commands by snapshotting affected state
/// BEFORE applying mutation. The host's command sink is consulted to apply
/// the inverse on undo/redo.
///
/// Important semantics:
/// <list type="bullet">
///   <item>One user action = one stack entry. Multi-step ops wrap in <c>Batch</c>.</item>
///   <item>Transient drag updates do not push entries; only the final committed move does.</item>
///   <item>On wholesale external change the stack is cleared.</item>
/// </list>
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();
    private readonly IGraphCommandSink _sink;
    private readonly int _maxEntries;

    /// <summary>Create an undo stack feeding mutations to the given sink.</summary>
    public UndoStack(IGraphCommandSink sink, int maxEntries = 256)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _maxEntries = maxEntries;
    }

    /// <summary>Total entries in undo direction.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Total entries in redo direction.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>True if an undo is available.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True if a redo is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the next undoable action, or null if none.</summary>
    public string? UndoLabel => _undo.TryPeek(out var e) ? e.Label : null;

    /// <summary>Label of the next redoable action, or null if none.</summary>
    public string? RedoLabel => _redo.TryPeek(out var e) ? e.Label : null;

    /// <summary>
    /// Apply a command via the sink, recording an inverse for undo.
    /// The caller supplies the inverse explicitly because only it knows
    /// the pre-mutation state.
    /// </summary>
    public GraphCommandResult ApplyAndRecord(
        GraphCommand forward,
        GraphCommand inverse,
        string label)
    {
        var result = _sink.Apply(forward);
        if (!result.Success) return result;

        _undo.Push(new UndoEntry(label, forward, inverse));
        _redo.Clear();
        TrimToMax();
        return result;
    }

    /// <summary>Pop the undo stack, apply the inverse, push to redo.</summary>
    public bool Undo()
    {
        if (!_undo.TryPop(out var entry)) return false;
        var r = _sink.Apply(entry.Inverse);
        if (!r.Success)
        {
            // Restore stack to avoid loss; the inverse couldn't apply.
            _undo.Push(entry);
            return false;
        }

        _redo.Push(entry);
        return true;
    }

    /// <summary>Pop the redo stack, re-apply the forward, push to undo.</summary>
    public bool Redo()
    {
        if (!_redo.TryPop(out var entry)) return false;
        var r = _sink.Apply(entry.Forward);
        if (!r.Success)
        {
            _redo.Push(entry);
            return false;
        }

        _undo.Push(entry);
        return true;
    }

    /// <summary>Clear both stacks. Called when external wholesale change occurs.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void TrimToMax()
    {
        if (_undo.Count <= _maxEntries) return;

        // Pop from the bottom of the stack (oldest). Stack<T> has no API
        // for this; rebuild via reverse.
        var keep = new Stack<UndoEntry>(_maxEntries);
        var arr = _undo.ToArray();              // top-first
        for (int i = 0; i < _maxEntries; i++)
            keep.Push(arr[_maxEntries - 1 - i]); // restore top-first order

        _undo.Clear();
        foreach (var e in keep.Reverse()) _undo.Push(e);
    }

    private readonly record struct UndoEntry(string Label, GraphCommand Forward, GraphCommand Inverse);
}
