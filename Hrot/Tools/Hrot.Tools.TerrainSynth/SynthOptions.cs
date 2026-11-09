using System.Globalization;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Command line of the tool: a preset plus optional overrides.</summary>
public sealed record SynthOptions(
    string Preset, ulong Seed, string OutDir,
    double? Size, double? HeightSpacing, int? Buildings, int? Trees)
{
    public static SynthOptions Parse(string[] args)
    {
        string preset = "tiny";
        ulong seed = 1;
        string? outDir = null;
        double? size = null, spacing = null;
        int? buildings = null, trees = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a is not ("--preset" or "--seed" or "--out" or "--size" or "--height-spacing" or "--buildings" or "--trees"))
                throw new ArgumentException($"unknown argument {a}");
            if (i + 1 >= args.Length) throw new ArgumentException($"missing value for {a}");
            string v = args[++i];
            switch (a)
            {
                case "--preset": preset = v; break;
                case "--seed": seed = ParseU64(a, v); break;
                case "--out": outDir = v; break;
                case "--size": size = ParseDouble(a, v); break;
                case "--height-spacing": spacing = ParseDouble(a, v); break;
                case "--buildings": buildings = ParseInt(a, v); break;
                case "--trees": trees = ParseInt(a, v); break;
            }
        }

        if (preset != "tiny" && preset != "full") throw new ArgumentException($"unknown preset {preset} (tiny|full)");
        if (size is <= 0) throw new ArgumentException("--size must be positive");
        if (spacing is <= 0) throw new ArgumentException("--height-spacing must be positive");
        if (buildings is < 0 || trees is < 0) throw new ArgumentException("counts must not be negative");

        outDir ??= string.Create(CultureInfo.InvariantCulture, $"artifacts/terrain-synth/{preset}-{seed}");
        return new SynthOptions(preset, seed, outDir, size, spacing, buildings, trees);
    }

    /// <summary>The preset with the overrides applied. --size is the side length in metres; counts and road km scale by area.</summary>
    public SynthProfile Resolve()
    {
        var p = Preset == "full" ? SynthProfile.Full : SynthProfile.Tiny;

        if (Size is double side)
        {
            double k = side / p.Width;
            double cx = (p.MinX + p.MaxX) / 2, cy = (p.MinY + p.MaxY) / 2;
            double area = k * k;
            p = p with
            {
                MinX = cx - side / 2, MaxX = cx + side / 2,
                MinY = cy - side / 2, MaxY = cy + side / 2,
                NodataRects = p.NodataRects.Select(r => new Rect(r.MinX * k, r.MinY * k, r.MaxX * k, r.MaxY * k)).ToArray(),
                BuildingCount = (int)Math.Round(p.BuildingCount * area),
                TreeCount = (int)Math.Round(p.TreeCount * area),
                RoadKm = p.RoadKm.ToDictionary(e => e.Key, e => e.Value * area),
            };
        }
        if (HeightSpacing is double s) p = p with { HeightSpacing = s };
        if (Buildings is int b) p = p with { BuildingCount = b };
        if (Trees is int t) p = p with { TreeCount = t };
        return p;
    }

    private static ulong ParseU64(string name, string v) =>
        ulong.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : throw new ArgumentException($"bad value for {name}: {v}");

    private static int ParseInt(string name, string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : throw new ArgumentException($"bad value for {name}: {v}");

    private static double ParseDouble(string name, string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && double.IsFinite(r) ? r : throw new ArgumentException($"bad value for {name}: {v}");
}
