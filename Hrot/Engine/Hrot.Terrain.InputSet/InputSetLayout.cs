namespace Hrot.Terrain.InputSet;

/// <summary>The relative paths of the input set's files (§10) — the one place they are spelled.</summary>
public static class InputSetLayout
{
    public const string Manifest = "manifest.json";
    public const string HeightsF32 = "heights/heights.f32";
    public const string HeightsHdr = "heights/heights.hdr";
    public const string HeightsJson = "heights/heights.json";
    public const string Buildings = "buildings/buildings.geojson";
    public const string Unassigned = "buildings/unassigned.geojson";
    public const string Trees = "trees/trees.csv";
    public const string RoadsNet = "roads/roads.net.xml";
    public const string Walls = "walls/walls.geojson";
    /// <summary>Optional (§10.9).</summary>
    public const string Water = "water/water.geojson";
    /// <summary>Optional (§10.9).</summary>
    public const string Scrub = "vegetation/scrub.geojson";
    /// <summary>Optional (§10.9 ②): woods as polygons with a density; then trees.csv holds only trees outside woods.</summary>
    public const string Woods = "vegetation/woods.geojson";

    public static readonly string[] Required = { Manifest, HeightsF32, HeightsHdr, HeightsJson, Buildings, Unassigned, Trees, Walls };
    public static readonly string[] Optional = { RoadsNet, Water, Scrub, Woods };
}
