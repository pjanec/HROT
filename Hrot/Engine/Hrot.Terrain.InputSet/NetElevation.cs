using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hrot.Terrain.InputSet;

/// <summary>
/// ⭐ AQ93 E2b / R-4025 — the ONE post-netconvert step that gives a SUMO net its HEIGHTS: netconvert writes a flat (2-D) net from OSM or from the synth's
/// plain files, so the roads would sit at z 0 under a terrain at 300–400 m. This writes our own ground into every <c>shape</c> of the net, so a
/// terrain ships a net whose z matches its height grid — and SUMO's own sim (cars, pedestrians, <c>PoseResolver</c>), the road scene and
/// SumoSharp's <c>TerrainField</c> all read the right heights from the file, with no host-side drape (<c>docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md</c> §4 E2b).
/// </summary>
/// <remarks>
/// <para>⭐ Both importers call it, once, AFTER the heights grid is written and netconvert has run: the OSM pipeline (<c>OsmTerrainPipeline</c>) and the random generator
/// (<c>TerrainSynthesizer.Write</c>). One lookup — <see cref="HeightSampler"/>, the planar-triangle rule of the terrain's own <c>TerrainHeightGrid.Sample</c> — serves
/// every lane, junction outline, crossing and walking area, so an outline and the lane ends that meet it get the same z at the same point.</para>
/// <para>⭐ A TEXT rewrite of the <c>shape="…"</c> attributes (and a <c>z</c> on each <c>&lt;junction&gt;</c>): everything else in the file stays byte-for-byte, lane
/// <c>length</c>s are untouched (a densified straight run has the same 2-D length), and a point netconvert already gave a z keeps it.</para>
/// <para>⭐ SumoSharp gives a lane <c>ShapeZ</c> only when EVERY point of its shape has three components (<c>NetworkParser.ParseShapeZ</c> returns null otherwise) — which is why
/// every point of every shape is filled, never a subset.</para>
/// <para>⭐ <see cref="Spacing"/> = 10 m: about one cell of the compiled terrain grid, so a denser point carries no more information, while a longer run would cut across a hill
/// (a 1 cm sag needs a curvature under 0.001 /m at 10 m); a lane of 2–3 points per 50 m is what a straight netconvert edge has.</para>
/// </remarks>
public static class NetElevation
{
    /// <summary>The longest straight run (m) left between two points of a shape.</summary>
    public const double Spacing = 10.0;

    public sealed record Result(int Shapes, int PointsBefore, int PointsAfter, long BytesBefore, long BytesAfter);

    /// <summary>Gives <paramref name="netPath"/> the heights of the input set in <paramref name="setFolder"/> (its <c>heights/</c> grid). The net's coordinates are the grid's (local metres, anchor 0,0).</summary>
    public static Result Apply(string netPath, string setFolder)
    {
        var (info, z) = HeightsGrid.ReadAll(setFolder);
        return Apply(netPath, new HeightSampler(info, z).At);
    }

    /// <summary>Gives <paramref name="netPath"/> the heights <paramref name="ground"/> returns, in place.</summary>
    /// <param name="force">⭐ CE-4403 (G-25, V-21) — overwrite a z a point (or a junction) already has: the importer re-elevates the net over the LEVELLED ground,
    /// where the pipeline's z (on the unlevelled ground) is stale. False (the default) keeps netconvert's own z, as before.</param>
    public static Result Apply(string netPath, Func<double, double, double> ground, double spacing = Spacing, bool force = false)
    {
        string text = File.ReadAllText(netPath);
        long before = new FileInfo(netPath).Length;
        int shapes = 0, pointsBefore = 0, pointsAfter = 0;

        string rewritten = ShapeAttribute.Replace(text, m =>
        {
            var (s, pb, pa) = Shape(m.Groups[1].Value, ground, spacing, force);
            shapes++; pointsBefore += pb; pointsAfter += pa;
            return " shape=\"" + s + "\"";
        });
        rewritten = JunctionTag.Replace(rewritten, m =>
        {
            string tag = m.Value;
            if (Regex.IsMatch(tag, "\\sz=\"")) { if (!force) return tag; tag = Regex.Replace(tag, "\\sz=\"[^\"]*\"", ""); }
            var xy = Regex.Match(tag, "\\sx=\"([^\"]*)\"\\s+y=\"([^\"]*)\"");
            if (!xy.Success) return tag;
            double x = D(xy.Groups[1].Value), y = D(xy.Groups[2].Value);
            return tag.Insert(xy.Index + xy.Length, " z=\"" + F(ground(x, y)) + "\"");
        });

        if (rewritten != text) File.WriteAllText(netPath, rewritten, new UTF8Encoding(false));
        return new Result(shapes, pointsBefore, pointsAfter, before, new FileInfo(netPath).Length);
    }

    private static readonly Regex ShapeAttribute = new("\\sshape=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex JunctionTag = new("<junction\\b[^>]*>", RegexOptions.Compiled);

    private static (string Shape, int Before, int After) Shape(string shape, Func<double, double, double> ground, double spacing, bool force = false)
    {
        var pts = new List<(double X, double Y, double? Z, string XText, string YText)>();
        foreach (var pair in shape.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = pair.Split(',');
            if (c.Length < 2) return (shape, 0, 0);   // not a coordinate list: leave it
            pts.Add((D(c[0]), D(c[1]), c.Length >= 3 && !force ? D(c[2]) : null, c[0], c[1]));
        }
        if (pts.Count == 0) return (shape, 0, 0);

        var sb = new StringBuilder(shape.Length + pts.Count * 16);
        void Emit(string x, string y, double z) { if (sb.Length > 0) sb.Append(' '); sb.Append(x).Append(',').Append(y).Append(',').Append(F(z)); }
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            if (i > 0)
            {
                var q = pts[i - 1];
                double len = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
                int n = (int)Math.Ceiling(len / spacing);
                for (int k = 1; k < n; k++)
                {
                    double t = (double)k / n, x = q.X + (p.X - q.X) * t, y = q.Y + (p.Y - q.Y) * t;
                    // between two points that already carry a z the run keeps its own slope; otherwise it lies on the ground
                    double z = q.Z is { } qz && p.Z is { } pz ? qz + (pz - qz) * t : ground(x, y);
                    Emit(F(x), F(y), z);
                }
            }
            Emit(p.XText, p.YText, p.Z ?? ground(p.X, p.Y));
        }
        // keep the original text of a point that already had its z (netconvert's own digits)
        string result = sb.ToString();
        if (pts.All(p => p.Z is not null) && result.Split(' ').Length == pts.Count) return (shape, pts.Count, pts.Count);
        return (result, pts.Count, result.Split(' ').Length);
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
}

/// <summary>
/// The terrain's height lookup over an input set's grid: the SAME rule as the runtime's <c>TerrainHeightGrid.Sample</c> (the grid's edge holds outside it; planar inside each of a cell's
/// two triangles), so a net given these heights agrees with the ground it is drawn on.
/// </summary>
public sealed class HeightSampler
{
    private readonly HeightsInfo _info;
    private readonly float[] _z;   // row-major, SOUTH row first (HeightsGrid.ReadAll)

    public HeightSampler(HeightsInfo info, float[] z)
    {
        if (info.Cols < 2 || info.Rows < 2) throw new ArgumentException("a height grid needs at least 2 × 2 posts");
        _info = info; _z = z;
    }

    private float Post(int c, int r) { float v = _z[(long)r * _info.Cols + c]; return v <= HeightsInfo.NoData + 1f ? 0f : v; }

    public double At(double x, double y)
    {
        double fx = Math.Clamp((x - _info.OriginX) / _info.CellSize, 0, _info.Cols - 1);
        double fy = Math.Clamp((y - _info.OriginY) / _info.CellSize, 0, _info.Rows - 1);
        int c = Math.Min((int)fx, _info.Cols - 2), r = Math.Min((int)fy, _info.Rows - 2);
        double u = fx - c, v = fy - r;
        double z00 = Post(c, r), z10 = Post(c + 1, r), z01 = Post(c, r + 1), z11 = Post(c + 1, r + 1);
        return u >= v ? z00 + u * (z10 - z00) + v * (z11 - z10)
                      : z00 + v * (z01 - z00) + u * (z11 - z01);
    }
}
