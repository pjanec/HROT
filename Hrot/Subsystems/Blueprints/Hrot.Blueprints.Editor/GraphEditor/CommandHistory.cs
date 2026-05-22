namespace Hrot.Blueprints.Editor.GraphEditor;

public sealed class CommandHistory
{
    private const int Capacity = 64;
    private readonly IGraphCommand[] _history = new IGraphCommand[Capacity];
    private int _head;
    private int _count;
    private int _undoIndex;  // points to the next command to undo

    public int Count => _count;

    public void Execute(IGraphCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        // Discard redo history when a new command is executed.
        _count = _undoIndex;
        var idx = (_head + _count) % Capacity;
        _history[idx] = command;
        if (_count < Capacity) _count++;
        else _head = (_head + 1) % Capacity;  // evict oldest
        _undoIndex = _count;
        command.Execute();
    }

    public bool CanUndo => _undoIndex > 0;
    public bool CanRedo => _undoIndex < _count;

    public void Undo()
    {
        if (!CanUndo) return;
        _undoIndex--;
        var idx = (_head + _undoIndex) % Capacity;
        _history[idx].Undo();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var idx = (_head + _undoIndex) % Capacity;
        _history[idx].Execute();
        _undoIndex++;
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        _undoIndex = 0;
        Array.Clear(_history, 0, Capacity);
    }
}
