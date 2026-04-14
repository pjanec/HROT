namespace FDP.Toolkit.Vis2D.Abstractions
{
    public interface IResourceProvider
    {
        T? Get<T>() where T : class;
        bool Has<T>() where T : class;
    }
}
