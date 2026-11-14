namespace Hrot.Terrain.Osm;

/// <summary>Where the OSM XML of an area comes from (E-1: ONE file, which later also goes to netconvert). Network sources live behind this; tests and the CLI's <c>--osm-file</c> use <see cref="FileOsmSource"/>.</summary>
public interface IOsmSource
{
    string Name { get; }
    /// <summary>Writes the OSM XML covering <paramref name="boxWithMargin"/> to <paramref name="destinationFile"/>.</summary>
    Task FetchAsync(GeoBox boxWithMargin, string destinationFile, IProgress<OsmProgress>? progress, CancellationToken cancellation);
}

/// <summary>An OSM XML file already on disk (<c>--osm-file</c>, CI, offline authoring).</summary>
public sealed class FileOsmSource : IOsmSource
{
    private readonly string _path;
    public FileOsmSource(string path) => _path = path;
    public string Name => "file " + _path;

    public Task FetchAsync(GeoBox boxWithMargin, string destinationFile, IProgress<OsmProgress>? progress, CancellationToken cancellation)
    {
        if (!File.Exists(_path)) throw new FileNotFoundException("the OSM file does not exist: " + _path, _path);
        cancellation.ThrowIfCancellationRequested();
        File.Copy(_path, destinationFile, overwrite: true);
        return Task.CompletedTask;
    }
}
