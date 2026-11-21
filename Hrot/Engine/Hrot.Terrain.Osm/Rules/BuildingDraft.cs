using Clipper2Lib;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Osm;

/// <summary>A door or window before the ground is known: <see cref="Dz"/> is above its storey's floor.</summary>
public sealed record DraftOpening(string Type, double X, double Y, double Heading, int Storey, double Dz);

/// <summary>
/// A building as the rules decided it, in local metres — everything except the heights above sea level (the preview needs none; the write
/// stands it on the lowest ground under its footprint, <see cref="BuildingRules.Finish"/>).
/// </summary>
public sealed class BuildingDraft
{
    public required string Id { get; init; }
    /// <summary>Counter-clockwise, open (the last point is not repeated).</summary>
    public required PathD Ring { get; set; }
    /// <summary>OSM node ids parallel to <see cref="Ring"/> when the footprint is a whole way / ring (party walls by id, E-5).</summary>
    public long[]? NodeIds { get; init; }
    public required string Type { get; init; }
    public bool Enterable { get; set; } = true;
    public int Storeys { get; set; } = 1;
    public double StoreyHeight { get; set; } = 3.0;
    /// <summary>The wall height up to the eaves.</summary>
    public double Height { get; set; } = 3.0;
    public string Roof { get; set; } = "flat";
    public double RidgeRise { get; set; }
    public string? Material { get; set; }
    public double Area { get; set; }
    public bool HeightInferred { get; set; }
    public List<DraftOpening> Openings { get; } = new();
    /// <summary>Per edge <c>i</c> (<c>Ring[i] → Ring[i+1]</c>): the stretches (metres from the start) shared with another footprint.</summary>
    public List<(double T0, double T1)>[] Party { get; set; } = Array.Empty<List<(double, double)>>();
    /// <summary>⭐ CE-4382 — where the storeys came from: "tagged" (building:levels), "from height" (height tagged), "from type", "default".</summary>
    public string StoreysSource { get; set; } = "default";
    /// <summary>⭐ CE-4382 — the <c>roof:shape</c> tag when given ("gabled", "hipped", …).</summary>
    public string? RoofShape { get; set; }
    /// <summary>⭐ CE-4382 — where the roof came from: "tagged" (roof:shape), "from type", "storeys rule" (≤ 2 storeys pitched).</summary>
    public string RoofSource { get; set; } = "storeys rule";
    /// <summary>⭐ CE-4382 — how the doors were placed: "entrance node", "road-facing", "road-facing + back door", "longest wall" (no road near), "none".</summary>
    public string DoorRule { get; set; } = "none";
    /// <summary>⭐ CE-4382 — the ids of the footprints this one shares a wall with.</summary>
    public SortedSet<string> SharedWith { get; } = new(StringComparer.Ordinal);
    public int Doors => Openings.Count(o => o.Type == "door");
    public int Windows => Openings.Count(o => o.Type == "window");
}

/// <summary>What <see cref="BuildingRules.Apply"/> found, with the counts the preview's warnings are made of.</summary>
public sealed class BuildingRulesResult
{
    public List<BuildingDraft> Buildings { get; } = new();
    public int Enterable => Buildings.Count(b => b.Enterable);
    public int Solid => Buildings.Count(b => !b.Enterable);
    public int Doors { get; set; }
    public int Windows { get; set; }
    public int HeightsInferred { get; set; }
    public int SplitCourtyards { get; set; }
    public int FilledHoles { get; set; }
    public int DroppedSmall { get; set; }
    public int DroppedThin { get; set; }
    public int DroppedAtEdge { get; set; }
    public int NoWalls { get; set; }
    public int PartsIgnored { get; set; }
    public int PartsMerged { get; set; }
    public int SharedWallsById { get; set; }
    public int SharedWallsByDistance { get; set; }
    public int SnappedVertices { get; set; }
    public int NoFreeWall { get; set; }
    public int BrokenRings { get; set; }

    /// <summary>⭐ CE-4382 — per note kind (<see cref="NoteKinds"/>), the box (local metres) round everything it is about — where clicking the note zooms.</summary>
    public Dictionary<string, (double X0, double Y0, double X1, double Y1)> NoteBoxes { get; } = new(StringComparer.Ordinal);

    /// <summary>Grows the note's box over <paramref name="ring"/>.</summary>
    public void Touch(string kind, IReadOnlyList<PointD> ring)
    {
        if (ring.Count == 0) return;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in ring) { x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); }
        NoteBoxes[kind] = NoteBoxes.TryGetValue(kind, out var b) ? (Math.Min(b.X0, x0), Math.Min(b.Y0, y0), Math.Max(b.X1, x1), Math.Max(b.Y1, y1)) : (x0, y0, x1, y1);
    }
}

/// <summary>⭐ CE-4382 — the building notes' keys (the preview's notes and their boxes).</summary>
public static class NoteKinds
{
    public const string Solid = "solid", HeightsInferred = "heights-inferred", SplitCourtyards = "split-courtyards", SharedWalls = "shared-walls",
                        Snapped = "snapped", DroppedSmall = "dropped-small", DroppedThin = "dropped-thin", DroppedAtEdge = "dropped-edge", NoWalls = "no-walls",
                        PartsIgnored = "parts-ignored", PartsMerged = "parts-merged", NoFreeWall = "no-free-wall";
}
