using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hrot.Terrain.Import;

/// <summary>One building as the world file places it: a template instance (enterable) or a solid prism, plus its roof cap when pitched.</summary>
internal sealed class BuiltBuilding
{
    public required string Id { get; init; }
    public string? TemplateName { get; init; }
    public string? TemplateJson { get; init; }
    /// <summary>Terrain-local, counter-clockwise, open.</summary>
    public required IReadOnlyList<(double X, double Y)> Ring { get; init; }
    public (double X, double Y) Origin { get; init; }
    public double BaseZ { get; init; }
    /// <summary>A solid prism's height (to the eaves; a pitched roof is its cap, below).</summary>
    public double SolidHeight { get; init; }
    public int Floors { get; init; }
    public string Material { get; init; } = "brick";
    /// <summary>The cap prism of a pitched roof (over a template or a solid prism): eaves z and the height up to the ridge; 0 = none.</summary>
    public double CapBaseZ { get; init; }
    public double CapHeight { get; init; }
    public bool Solid => TemplateName == null;
}

/// <summary>
/// ⭐ I-c / I-d / I-h / I-i: ONE template per enterable building (R-4191), named by its id — footprint in template-local metres (origin = the
/// footprint's first vertex), storeys from <c>storeyZ</c> differences (the top one up to <c>baseZ + height</c>), outer walls 0.3 m on the footprint
/// edges, openings by <see cref="OpeningPlacer"/>, party walls once (<see cref="PartyWalls"/>), one stair per storey (<see cref="StairPlanner"/>),
/// a flat roof (+ a solid cap to the ridge when pitched). A solid building, or one whose stairs do not fit, is a prism.
/// </summary>
internal static class BuildingTemplateGenerator
{
    public const double WallThickness = 0.3, MinStoreyHeight = 2.4;

    private sealed class Draft
    {
        public required Building B;
        public required List<(double X, double Y)> Ring;
        public required List<double> Heights;
        public required List<Opening> Openings;   // terrain-local
        public StairPlanner.Plan? Plan;
        public bool Templated;
        public string Name = "";
    }

    public static List<BuiltBuilding> Generate(IReadOnlyList<Building> buildings, double swX, double swY, ImportReport rep, CancellationToken ct)
    {
        var drafts = new List<Draft>(buildings.Count);
        foreach (var b in buildings)
        {
            ct.ThrowIfCancellationRequested();
            var ring = b.Footprint.Take(b.Footprint.Count - 1).Select(p => (p.X - swX, p.Y - swY)).ToList();
            var d = new Draft { B = b, Ring = ring, Heights = StoreyHeights(b, rep), Openings = b.Openings.Select(o => o with { X = o.X - swX, Y = o.Y - swY }).ToList() };
            if (b.Enterable)
            {
                d.Plan = StairPlanner.TryPlan(ring, d.Heights);
                d.Templated = d.Plan != null;
                if (!d.Templated) { rep.SolidBecauseNoStair++; rep.Warnings.Add($"building {b.Id}: no stair fits its footprint — imported as a solid prism"); }
            }
            drafts.Add(d);
        }

        var party = new PartyWalls(drafts.Where(d => d.Templated).Select(d => (d.B.Id, (IReadOnlyList<(double, double)>)d.Ring)));
        rep.SharedWallsMerged = party.SharedEdges;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BuiltBuilding>(drafts.Count);
        foreach (var d in drafts)
        {
            ct.ThrowIfCancellationRequested();
            if (!d.Templated) { result.Add(Solid(d, rep)); rep.BuildingsSolid++; continue; }
            d.Name = Unique(Sanitize(d.B.Id), names);
            result.Add(Template(d, party, rep));
            rep.BuildingsEnterable++;
        }
        return result;
    }

    private static List<double> StoreyHeights(Building b, ImportReport rep)
    {
        var z = b.StoreyZ;
        var h = new List<double>();
        for (int i = 0; i < z.Count; i++)
        {
            double top = i + 1 < z.Count ? z[i + 1] : b.BaseZ + b.Height;
            double v = top - z[i];
            if (v < MinStoreyHeight)
            {
                rep.Warnings.Add($"building {b.Id}: storey {i} is {v:0.##} m — raised to {MinStoreyHeight} m");
                v = MinStoreyHeight;
            }
            h.Add(Math.Round(v, 3));
        }
        return h;
    }

    private static string Sanitize(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (char c in id) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return sb.Length == 0 ? "b" : sb.ToString();
    }

    private static string Unique(string name, HashSet<string> used)
    {
        string n = name; int i = 2;
        while (!used.Add(n)) n = name + "_" + i++;
        return n;
    }

    public static string MaterialOf(string? m) => m switch
    {
        "brick" => "brick", "concrete" => "concrete", "stone" => "concrete", "wood" => "fence-wood", "metal" => "steel-plate", "mud" => "earth-berm",
        _ => "brick",   // the default of the input set (Terrain Import UTM §10, Q-X6)
    };

    /// <summary>
    /// A solid building: one prism to the eaves — ⭐ and, when pitched, the SAME cap a template gets (I-h: a solid prism from the eaves to the ridge, labelled <c>&lt;id&gt;-roof</c>),
    /// so the maps draw its roof (a gable, DESIGN_Map_3D_Mode §6ai R-1) instead of a box as tall as the ridge. The two prisms fill exactly the old one's volume: sight and fire
    /// are unchanged. (ui, `2026-11-10`, at the user's ask — it was one prism to the ridge.)
    /// </summary>
    private static BuiltBuilding Solid(Draft d, ImportReport rep)
    {
        var b = d.B;
        double eaves = b.BaseZ + b.Height, capH = 0;
        if (b.Roof == "pitched" && b.RidgeZ is double ridge && ridge > eaves + 0.05) { capH = Math.Round(ridge - eaves, 3); rep.RoofCaps++; }
        return new BuiltBuilding
        {
            Id = b.Id, Ring = d.Ring, Origin = d.Ring[0], BaseZ = b.BaseZ, SolidHeight = Math.Round(b.Height, 3),
            Floors = b.StoreyZ.Count, Material = MaterialOf(b.Material), CapBaseZ = Math.Round(eaves, 3), CapHeight = capH,
        };
    }

    private static BuiltBuilding Template(Draft d, PartyWalls party, ImportReport rep)
    {
        var b = d.B;
        var o = d.Ring[0];
        (double X, double Y) L((double X, double Y) p) => (Math.Round(p.X - o.X, 3), Math.Round(p.Y - o.Y, 3));
        int n = d.Ring.Count, storeys = d.Heights.Count;
        var (placed, counts) = OpeningPlacer.Place(d.Ring, b.Id, d.Openings, storeys, party);
        rep.OpeningsPlaced += counts.Placed; rep.OpeningsDroppedOffWall += counts.OffWall; rep.OpeningsDroppedOnPartyWall += counts.OnParty;
        rep.OpeningsDroppedOverlap += counts.Overlap; rep.OpeningsDroppedOther += counts.Other;
        rep.Stairs += d.Plan!.Stairs.Sum(s => s.Runs.Count); rep.StairsSwitchBack += d.Plan.Stairs.Count(s => s.SwitchBack);

        string material = MaterialOf(b.Material);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            void Pt((double X, double Y) p) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteEndArray(); }
            w.WriteStartObject();
            w.WriteString("$comment", "Generated by hrot-terrain-import (CE-4368): one exact building of the input set (R-4191). Local metres; origin = the footprint's first vertex.");
            w.WriteString("name", d.Name);
            w.WriteString("material", material);
            w.WriteStartArray("footprint");
            foreach (var p in d.Ring) Pt(L(p));
            w.WriteEndArray();
            w.WriteStartArray("storeys");
            for (int k = 0; k < storeys; k++)
            {
                w.WriteStartObject();
                w.WriteNumber("height", d.Heights[k]);
                if (k > 0)
                {
                    w.WriteStartArray("floor");
                    foreach (var ring in d.Plan.Floors[k])
                    {
                        w.WriteStartArray();
                        foreach (var p in ring) Pt(L(p));
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                }
                w.WriteStartArray("walls");
                for (int e = 0; e < n; e++)
                {
                    if (!party.Writes(b.Id, e)) continue;   // a shared edge is ONE wall, written by the first building (I-f)
                    var a = d.Ring[e]; var c = d.Ring[(e + 1) % n];
                    if (Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y)) < 0.05) continue;
                    w.WriteStartObject();
                    w.WritePropertyName("from"); Pt(L(a));
                    w.WritePropertyName("to"); Pt(L(c));
                    w.WriteNumber("thickness", WallThickness);
                    var ops = placed.Where(x => x.Edge == e && x.Storey == k).OrderBy(x => x.At).ToList();
                    if (ops.Count > 0)
                    {
                        w.WriteStartArray("openings");
                        foreach (var op in ops)
                        {
                            w.WriteStartObject();
                            w.WriteString("kind", op.Kind);
                            w.WriteNumber("at", op.At);
                            w.WriteNumber("width", op.Width);
                            if (op.DoorId != null) { w.WriteString("doorId", op.DoorId); w.WriteString("initial", "closed"); }
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                var stairs = d.Plan.Stairs.Where(s => s.FromStorey == k).ToList();
                if (stairs.Count > 0)
                {
                    w.WriteStartArray("stairs");
                    foreach (var s in stairs)
                        foreach (var run in s.Runs)
                        {
                            w.WriteStartObject();
                            w.WritePropertyName("from"); Pt(L(run.From));
                            w.WritePropertyName("to"); Pt(L(run.To));
                            w.WriteNumber("width", StairPlanner.Width);
                            w.WriteNumber("toStorey", k + 1);
                            if (run.FromRise is double fr) w.WriteNumber("fromRise", Math.Round(fr, 3));   // ⭐ CE-4379: a switch-back's runs end at its landing
                            if (run.ToRise is double tr) w.WriteNumber("toRise", Math.Round(tr, 3));
                            w.WriteEndObject();
                        }
                    w.WriteEndArray();
                }
                var landings = stairs.Where(s => s.SwitchBack).ToList();
                if (landings.Count > 0)
                {
                    w.WriteStartArray("landings");
                    foreach (var s in landings)
                    {
                        w.WriteStartObject();
                        w.WriteNumber("rise", Math.Round(s.LandingRise, 3));
                        w.WriteStartArray("ring");
                        foreach (var p in s.Landing!) Pt(L(p));
                        w.WriteEndArray();
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("roof", "flat");
            w.WriteEndObject();
        }
        double eaves = b.BaseZ + d.Heights.Sum();
        double capH = 0;
        if (b.Roof == "pitched" && b.RidgeZ is double ridge && ridge > eaves + 0.05) { capH = Math.Round(ridge - eaves, 3); rep.RoofCaps++; }
        return new BuiltBuilding
        {
            Id = b.Id, TemplateName = d.Name, TemplateJson = Encoding.UTF8.GetString(ms.ToArray()), Ring = d.Ring, Origin = o,
            BaseZ = b.BaseZ, Floors = storeys, Material = material, CapBaseZ = Math.Round(eaves, 3), CapHeight = capH,
        };
    }
}
