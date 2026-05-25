namespace Hrot.Blueprints.Editor.NodeDrawers;

public interface INodeEditSession : IDisposable
{
    bool IsDirty { get; }
    void Draw();
    void ResetDirty();
}
