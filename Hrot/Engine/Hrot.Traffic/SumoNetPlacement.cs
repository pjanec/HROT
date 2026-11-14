using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Terrain;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 E1 / E1b (R-4025, R-4052) — where a SUMO net's coordinates sit in the terrain's local metres (X east, Y north).
/// <list type="bullet">
///   <item><b>Not georeferenced</b> (<c>projParameter="!"</c> or no <c>&lt;location&gt;</c>): terrain local = net − anchor
///   (<see cref="TerrainSumoNet.AnchorX"/>/<c>Y</c>, from <c>terrain.json</c>).</item>
///   <item><b>UTM</b> (CE-4396, R-4197): through the node's geo transform — the SAME zone ⇒ a pure translation (net − netOffset − (E0, N0), exact);
///   another zone ⇒ via geodetic. A terrain with no geo frame refuses a georeferenced net.</item>
///   <item>⛔ Any other projection is refused — a net placed in the wrong place would move every road and car.</item>
/// </list>
/// Heights are SUMO's own (R-4025); this places only the horizontal position.
/// </summary>
public sealed class SumoNetPlacement
{
    private readonly bool _georeferenced;
    private readonly double _anchorX, _anchorY;
    private readonly double _offsetX, _offsetY;
    private readonly int _zone;
    private readonly bool _south;
    private readonly IGeographicTransform? _geo;
    /// <summary>Non-null when the net's zone is the node frame's own: placement is a translation.</summary>
    private readonly UtmTransform? _sameZone;

    private SumoNetPlacement(bool georeferenced, double ax, double ay, double ox, double oy, int zone, bool south, IGeographicTransform? geo)
    {
        _georeferenced = georeferenced; _anchorX = ax; _anchorY = ay; _offsetX = ox; _offsetY = oy; _zone = zone; _south = south; _geo = geo;
        var inner = geo is SwitchableGeoTransform sw ? sw.Inner : geo;
        if (inner is UtmTransform u && u.Zone == zone && u.South == south) _sameZone = u;
    }

    /// <summary>True when the net is placed through its projection and the terrain origin (E1), false when by the anchor (E1b).</summary>
    public bool IsGeoreferenced => _georeferenced;

    /// <summary>Builds the placement for <paramref name="net"/> declared by <paramref name="declared"/> on a node whose geo transform is <paramref name="geo"/> (<see cref="TerrainGeoFrame.Of"/> from a terrain definition).</summary>
    public static SumoNetPlacement For(NetworkModel net, TerrainSumoNet declared, IGeographicTransform? geo)
    {
        var location = net.Location;
        if (location == null || !location.IsGeoreferenced)
            return new SumoNetPlacement(false, declared.AnchorX, declared.AnchorY, 0, 0, 0, false, null);

        if (!Utm.TryParseProj(location.ProjParameter, out var zone, out var south))
            throw new InvalidDataException(
                $"[Traffic] The SUMO net '{declared.File}' is projected with '{location.ProjParameter}'; only '+proj=utm' is supported "
              + "(AQ93 E1). Re-export the net in UTM, or as plain local metres with an anchor in terrain.json.");

        if (geo == null || !geo.HasFrame)
            throw new InvalidDataException(
                $"[Traffic] The SUMO net '{declared.File}' is georeferenced (UTM), but the terrain has no geo frame (no 'origin' in terrain.json) — it cannot be placed.");
        return new SumoNetPlacement(true, 0, 0, location.NetOffsetX, location.NetOffsetY, zone, south, geo);
    }

    /// <summary>A net point (metres) → the terrain's local metres.</summary>
    public (double X, double Y) ToLocal(double netX, double netY)
    {
        if (!_georeferenced) return (netX - _anchorX, netY - _anchorY);
        if (_sameZone is { } u) return (netX - _offsetX - u.E0, netY - _offsetY - u.N0);   // the same UTM zone: a translation
        var (lat, lon) = Utm.ToGeodetic(netX - _offsetX, netY - _offsetY, _zone, _south);
        var local = _geo!.ToCartesian(lat, lon, 0.0);
        return (local.X, local.Y);
    }

    /// <summary>⭐ AQ93 §13 F5 — the terrain's local metres → a net point: the exact inverse of <see cref="ToLocal"/> (what SUMO is told
    /// about our bodies).</summary>
    public (double X, double Y) ToNet(double localX, double localY)
    {
        if (!_georeferenced) return (localX + _anchorX, localY + _anchorY);
        if (_sameZone is { } u) return (localX + u.E0 + _offsetX, localY + u.N0 + _offsetY);
        var (lat, lon, _) = _geo!.ToGeodetic(new System.Numerics.Vector3((float)localX, (float)localY, 0f));
        var (e, n) = Utm.FromGeodetic(lat, lon, _zone, _south);
        return (e + _offsetX, n + _offsetY);
    }
}
