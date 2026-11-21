using System.Globalization;
using System.Numerics;
using Clipper2Lib;

namespace Hrot.Terrain.Osm;

/// <summary>⭐ §5.7: the content budget, thresholds in ONE place — warn above 2×, refuse above 3× of the tuned load.</summary>
public sealed record OsmBudget
{
    public int TunedBuildings { get; init; } = 1800;
    public int TunedOpenings { get; init; } = 70000;
    public int TunedTrees { get; init; } = 176000;
    public double WarnFactor { get; init; } = 2.0;
    public double RefuseFactor { get; init; } = 3.0;

    public static OsmBudget Default { get; } = new();

    /// <summary>Buildings are the ENTERABLE ones (R-4193: solid garages and sheds are counted apart); openings = doors + windows; trees = lone + woods area × density.</summary>
    public (BudgetVerdict Verdict, string? Reason) Evaluate(OsmCounts c)
    {
        var rows = new (string Name, double Value, double Tuned)[]
        {
            ("enterable buildings", c.Buildings, TunedBuildings),
            ("doors and windows", c.Doors + c.Windows, TunedOpenings),
            ("trees", c.EstimatedTrees, TunedTrees),
        };
        var worst = rows.OrderByDescending(r => r.Value / r.Tuned).First();
        double ratio = worst.Value / worst.Tuned;
        if (ratio <= WarnFactor) return (BudgetVerdict.Ok, null);
        string what = $"{Fmt(worst.Value)} {worst.Name} — HROT is tuned for {Fmt(worst.Tuned)}";
        if (ratio > RefuseFactor)
        {
            double side = Math.Sqrt(WarnFactor / ratio);
            return (BudgetVerdict.Refused, $"{what}: too much to import. Pick a smaller box — about {Math.Floor(side * 100):0} % of this one's width and height.");
        }
        return (BudgetVerdict.Heavy, $"{what}: heavier than HROT is tuned for; the import is allowed.");
    }

    private static string Fmt(double v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
}

/// <summary>Everything the rules found in an area — the preview is made of it and so is the write.</summary>
internal sealed class AreaAnalysis
{
    public required AreaBounds Bounds { get; init; }
    public required string OsmFile { get; init; }
    public required OsmModel Model { get; init; }
    public required BuildingRulesResult Buildings { get; init; }
    public required VegetationResult Vegetation { get; init; }
    public required List<WallDraft> Walls { get; init; }
    public required List<AreaDraft> Water { get; init; }
    public required List<RoadLine> Roads { get; init; }
    public int FilledHoles { get; init; }
}

public static class PreviewBuilder
{
    public static string Attribution(string demName) => "© OpenStreetMap contributors · " + demName;

    internal static OsmCounts CountsOf(AreaAnalysis a) => new(
        a.Buildings.Enterable, a.Buildings.Doors, a.Buildings.Windows,
        a.Vegetation.LoneTrees + a.Vegetation.RowTrees, a.Vegetation.WoodsAreaM2,
        a.Vegetation.LoneTrees + a.Vegetation.RowTrees + a.Vegetation.EstimatedWoodsTrees,
        RoadRules.LengthMetres(a.Roads) / 1000.0, a.Walls.Count, a.Water.Count);

    internal static OsmPreview Build(AreaAnalysis a, OsmBudget budget, string demName)
    {
        var counts = CountsOf(a);
        var (verdict, reason) = budget.Evaluate(counts);
        var notes = Notes(a);
        static Vector2[] V(IEnumerable<PointD> p) => p.Select(q => new Vector2((float)q.x, (float)q.y)).ToArray();
        var areas = new List<PreviewArea>();
        foreach (var w in a.Vegetation.Woods) areas.Add(new PreviewArea("woods", V(w.Ring)));
        foreach (var s in a.Vegetation.Scrub) areas.Add(new PreviewArea("scrub", V(s.Ring)));
        foreach (var w in a.Water) areas.Add(new PreviewArea("water", V(w.Ring)));
        var lines = new List<PreviewLine>();
        foreach (var w in a.Walls) lines.Add(new PreviewLine(w.Barrier, V(w.Line)));
        foreach (var r in a.Vegetation.Rows) lines.Add(new PreviewLine("tree_row", V(r)));
        return new OsmPreview
        {
            Frame = a.Bounds.Frame,
            Bounds = new[] { a.Bounds.MinX, a.Bounds.MinY, a.Bounds.MaxX, a.Bounds.MaxY },
            Counts = counts,
            Verdict = verdict,
            VerdictReason = reason,
            Warnings = notes.Select(n => n.Text).ToList(),
            Notes = notes,
            Roads = a.Roads.Select(r => new PreviewRoad(r.Class, V(r.Points))).ToList(),
            Buildings = a.Buildings.Buildings.Select(b => new PreviewBuilding(b.Id, V(b.Ring), b.Storeys, b.Enterable)
            {
                Type = b.Type, StoreysSource = b.StoreysSource, Roof = b.Roof, RoofShape = b.RoofShape, RoofSource = b.RoofSource,
                Doors = b.Doors, Windows = b.Windows, DoorRule = b.DoorRule, SharedWallsWith = b.SharedWith.ToArray(),
            }).ToList(),
            Areas = areas,
            Lines = lines,
            Attribution = Attribution(demName),
            Trees = a.Vegetation.Trees.Select(t => new Vector2((float)t.X, (float)t.Y)).ToList(),
            DataTimestamp = a.Model.DataTimestamp,
        };
    }

    /// <summary>⭐ CE-4382 — the notes: one per thing the rules did, with its severity and the box round what it is about (where clicking it zooms).</summary>
    internal static List<PreviewNote> Notes(AreaAnalysis a)
    {
        var b = a.Buildings;
        var w = new List<PreviewNote>();
        void Add(int n, string text, NoteSeverity severity, string? kind)
        {
            if (n <= 0) return;
            double[]? box = kind != null && b.NoteBoxes.TryGetValue(kind, out var x) ? new[] { x.X0, x.Y0, x.X1, x.Y1 } : null;
            w.Add(new PreviewNote(string.Create(CultureInfo.InvariantCulture, $"{n} {text}"), severity, box));
        }
        Add(b.Solid, "buildings are SOLID, not enterable (garages, sheds, huts, kiosks, ruins, anything under 25 m²) — counted apart from the enterable budget", NoteSeverity.Info, NoteKinds.Solid);
        Add(b.HeightsInferred, "building heights inferred (no levels or height tagged) from the building type or the default storeys", NoteSeverity.Info, NoteKinds.HeightsInferred);
        Add(b.SplitCourtyards, "buildings with courtyards split into hole-free parts", NoteSeverity.Info, NoteKinds.SplitCourtyards);
        Add(b.SharedWallsById + b.SharedWallsByDistance, "shared walls (terraced / semi-detached) get no doors or windows", NoteSeverity.Info, NoteKinds.SharedWalls);
        Add(b.SnappedVertices, "footprint corners snapped (≤ 0.3 m) so neighbours share one wall", NoteSeverity.Info, NoteKinds.Snapped);
        Add(b.DroppedSmall, "footprints dropped: under 12 m²", NoteSeverity.Warning, NoteKinds.DroppedSmall);
        Add(b.DroppedThin, "footprints dropped: thinner than 2.5 m", NoteSeverity.Warning, NoteKinds.DroppedThin);
        Add(b.DroppedAtEdge, "buildings dropped: they cross the edge of the area", NoteSeverity.Warning, NoteKinds.DroppedAtEdge);
        Add(b.NoWalls, "roofs / carports without walls skipped", NoteSeverity.Info, NoteKinds.NoWalls);
        Add(b.PartsIgnored, "building parts ignored (an overhang above ground level)", NoteSeverity.Info, NoteKinds.PartsIgnored);
        Add(b.PartsMerged, "building parts merged into their building's outline", NoteSeverity.Info, NoteKinds.PartsMerged);
        Add(b.NoFreeWall, "buildings have no free wall for a door", NoteSeverity.Warning, NoteKinds.NoFreeWall);
        Add(b.BrokenRings + a.Vegetation.FilledHoles + a.FilledHoles, "shapes the rules could not make valid were dropped or simplified (unclosed rings, concave holes)", NoteSeverity.Warning, null);
        return w;
    }

    /// <summary>The notes' texts (the contract's <see cref="OsmPreview.Warnings"/>).</summary>
    internal static List<string> Warnings(AreaAnalysis a) => Notes(a).Select(n => n.Text).ToList();
}
