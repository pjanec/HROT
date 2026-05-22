namespace Hrot.Blueprints.Editor.Inspector;

public interface IStructEditDrawer<T>
{
    /// <summary>Returns true if the value was modified.</summary>
    bool Draw(string label, ref T value, DrawContext ctx);
}
