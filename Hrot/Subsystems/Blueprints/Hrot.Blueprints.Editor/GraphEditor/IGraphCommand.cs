namespace Hrot.Blueprints.Editor.GraphEditor;

public interface IGraphCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
