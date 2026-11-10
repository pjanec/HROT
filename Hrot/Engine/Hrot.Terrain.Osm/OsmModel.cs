namespace Hrot.Terrain.Osm;

/// <summary>The tags of an OSM element — never null; a shared empty one for untagged elements.</summary>
public static class OsmTags
{
    public static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    public static string? Get(IReadOnlyDictionary<string, string> tags, string key) => tags.TryGetValue(key, out var v) ? v : null;
    public static bool Is(IReadOnlyDictionary<string, string> tags, string key, string value) => tags.TryGetValue(key, out var v) && v == value;
    public static bool Has(IReadOnlyDictionary<string, string> tags, string key) => tags.ContainsKey(key);
}

public sealed record OsmWay(long Id, long[] Nodes, IReadOnlyDictionary<string, string> Tags)
{
    public bool IsClosed => Nodes.Length >= 4 && Nodes[0] == Nodes[^1];
}

/// <param name="Type">'n', 'w' or 'r'</param>
public sealed record OsmMember(char Type, long Ref, string Role);

public sealed record OsmRelation(long Id, OsmMember[] Members, IReadOnlyDictionary<string, string> Tags);

/// <summary>
/// ⭐ The compact in-memory form of an OSM extract (E-2): node positions are already LOCAL metres of the area's frame (x east, y north), ways
/// are node-id arrays, relations keep their members. Built by <see cref="OsmXmlReader"/>; every rule is a pure function of it (E-9).
/// </summary>
public sealed class OsmModel
{
    public Dictionary<long, (double X, double Y)> Nodes { get; } = new();
    /// <summary>Only tagged nodes (entrances, trees, …).</summary>
    public Dictionary<long, IReadOnlyDictionary<string, string>> NodeTags { get; } = new();
    public List<OsmWay> Ways { get; } = new();
    public Dictionary<long, OsmWay> WayById { get; } = new();
    public List<OsmRelation> Relations { get; } = new();
    /// <summary>Overpass' <c>osm_base</c> — when the data was current.</summary>
    public string? DataTimestamp { get; set; }
}
