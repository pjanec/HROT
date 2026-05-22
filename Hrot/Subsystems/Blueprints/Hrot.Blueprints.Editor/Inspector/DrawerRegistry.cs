namespace Hrot.Blueprints.Editor.Inspector;

public sealed class DrawerRegistry
{
    private readonly Dictionary<Type, object> _drawers = new();

    public void Register<T>(IStructEditDrawer<T> drawer)
        => _drawers[typeof(T)] = drawer ?? throw new ArgumentNullException(nameof(drawer));

    public bool TryGet<T>(out IStructEditDrawer<T> drawer)
    {
        if (_drawers.TryGetValue(typeof(T), out var obj) && obj is IStructEditDrawer<T> d)
        {
            drawer = d;
            return true;
        }
        drawer = null!;
        return false;
    }
}
