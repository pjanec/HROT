using System.IO.Compression;
using System.Numerics;
using System.Text;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐ Terrain authoring U-1 (docs/DESIGN_Terrain_Authoring.md §11 B-5; mockup 2, the tiles) — a recipe's PICTURE: the recipe expanded at its DEFAULT SEED (1) on flat ground by the pure <see cref="FeatureExpander"/>, drawn top-down (tan
/// houses, dark-green woods, on the ground green) into a 320 × 180 PNG. ⭐ A stand-in for the thumbnail atlas of §2j (which would carry a model render): it is made on demand from the recipe, so a NEW recipe has a picture without any art step,
/// and it is cached by the CONTENT of the recipe and its templates, so an edited recipe gets a new picture. ⛔ Written to a cache folder, never the repository (no screenshots in git).
/// </summary>
public static class TerrainThumbnails
{
    public const int Width = 320, Height = 180;
    public const uint DefaultSeed = 1;
    public static readonly (byte R, byte G, byte B) Ground = (52, 74, 44), WoodFill = (31, 92, 52), WoodEdge = (60, 130, 78), HouseFill = (236, 206, 152), HouseEdge = (150, 110, 80);

    /// <summary>
    /// The PNG of <paramref name="pick"/> under <paramref name="cacheDir"/>, rendered when it is not there yet; null for a pick with no recipe or one that cannot be expanded. A house template draws its own footprint, alone.
    /// </summary>
    public static string? EnsureFor(TerrainPick pick, IReadOnlyList<string?> recipeRoots, IReadOnlyList<string?> buildingRoots, string cacheDir)
    {
        try
        {
            string? TemplateFile(string name) { foreach (var r in buildingRoots) if (!string.IsNullOrWhiteSpace(r) && Path.Combine(r, name + TerrainAssets.TemplateSuffix) is var f && File.Exists(f)) return f; return null; }
            var assets = new TerrainAssets { Templates = name => TemplateFile(name) is { } f ? File.ReadAllText(f) : null };
            var world = TerrainWorldParser.Parse("""{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[-400,-400,400,400],"groundZ":0},"features":[]}""", "thumb", assets);
            IReadOnlyList<Vector2[]> houses, woods; string stamp;
            if (pick.Kind == TerrainPickKind.Block)
            {
                var recipe = TerrainRecipe.Find(pick.Id, recipeRoots);
                var x = FeatureExpander.Expand(recipe, DefaultSeed, Vector2.Zero, 0f, TerrainBlock.FitAll, world, "thumb", assets);
                var shapes = x.ShapesOrEmpty;
                houses = shapes.Take(x.Houses).ToList(); woods = shapes.Skip(x.Houses).ToList();
                stamp = ContentStamp(recipeRoots.Where(r => r != null).Select(r => Path.Combine(r!, pick.Id + TerrainRecipe.Suffix)).Concat(recipe.Templates.Select(t => TemplateFile(t) ?? t)));
            }
            else
            {
                houses = new[] { FeatureExpander.HouseFootprint(assets, pick.Id, Vector2.Zero, 0f) }; woods = Array.Empty<Vector2[]>();
                stamp = ContentStamp(new[] { TemplateFile(pick.Id) ?? pick.Id });
            }
            string path = Path.Combine(cacheDir, $"{(pick.Kind == TerrainPickKind.Block ? "b" : "h")}-{pick.Id}-{stamp}.png");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(cacheDir);
            var png = Png(Render(houses, woods, pick.Kind == TerrainPickKind.House));
            string tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(tmp, png);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The cache folder: <c>%TEMP%/hrot-terrain-thumbs</c>.</summary>
    public static string DefaultCacheDir => Path.Combine(Path.GetTempPath(), "hrot-terrain-thumbs");

    private static string ContentStamp(IEnumerable<string> files)
    {
        uint h = 2166136261;
        foreach (var f in files)
            if (File.Exists(f)) foreach (byte b in File.ReadAllBytes(f)) { h ^= b; h *= 16777619; }
        return h.ToString("x8");
    }

    /// <summary>RGBA pixels of the picture: the shapes fitted into the tile with a margin, north up (world +Y up).</summary>
    public static byte[] Render(IReadOnlyList<Vector2[]> houses, IReadOnlyList<Vector2[]> woods, bool single)
    {
        var px = new byte[Width * Height * 4];
        for (int i = 0; i < Width * Height; i++) { px[i * 4] = Ground.R; px[i * 4 + 1] = Ground.G; px[i * 4 + 2] = Ground.B; px[i * 4 + 3] = 255; }
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var s in houses.Concat(woods)) foreach (var p in s) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        if (min.X > max.X) return px;
        float margin = 16f, w = Math.Max(max.X - min.X, 1f), h = Math.Max(max.Y - min.Y, 1f);
        float scale = Math.Min((Width - 2 * margin) / w, (Height - 2 * margin) / h);
        if (single) scale = Math.Min(scale, 9f);   // a lone house is not blown up to the whole tile
        var centre = (min + max) * 0.5f;
        Vector2 Map(Vector2 p) => new((p.X - centre.X) * scale + Width * 0.5f, Height * 0.5f - (p.Y - centre.Y) * scale);
        foreach (var s in woods) Poly(px, s.Select(Map).ToArray(), WoodFill, WoodEdge);
        foreach (var s in houses) Poly(px, Legible(s.Select(Map).ToArray()), HouseFill, HouseEdge);
        return px;
    }

    /// <summary>A house is drawn at least ~9 px wide (a stylised tile, as the mockup's): a small footprint is scaled up about its centre.</summary>
    private static Vector2[] Legible(Vector2[] p)
    {
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var v in p) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
        float w = Math.Max(max.X - min.X, 0.01f), f = Math.Clamp(9f / w, 1f, 4f);
        if (f <= 1f) return p;
        var c = (min + max) * 0.5f;
        return p.Select(v => c + (v - c) * f).ToArray();
    }

    private static void Poly(byte[] px, Vector2[] p, (byte R, byte G, byte B) fill, (byte R, byte G, byte B) edge)
    {
        float y0 = p.Min(v => v.Y), y1 = p.Max(v => v.Y);
        var xs = new List<float>();
        for (int y = Math.Max(0, (int)MathF.Floor(y0)); y <= Math.Min(Height - 1, (int)MathF.Ceiling(y1)); y++)
        {
            xs.Clear();
            float sy = y + 0.5f;
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Length];
                if ((a.Y <= sy && b.Y > sy) || (b.Y <= sy && a.Y > sy)) xs.Add(a.X + (sy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = Math.Max(0, (int)MathF.Round(xs[k])); x <= Math.Min(Width - 1, (int)MathF.Round(xs[k + 1])); x++) Put(px, x, y, fill);
        }
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Length];
            int n = (int)MathF.Ceiling(Vector2.Distance(a, b)) + 1;
            for (int s = 0; s <= n; s++) { var q = Vector2.Lerp(a, b, s / (float)n); int x = (int)MathF.Round(q.X), y = (int)MathF.Round(q.Y); if (x >= 0 && y >= 0 && x < Width && y < Height) Put(px, x, y, edge); }
        }
    }

    private static void Put(byte[] px, int x, int y, (byte R, byte G, byte B) c) { int i = (y * Width + x) * 4; px[i] = c.R; px[i + 1] = c.G; px[i + 2] = c.B; px[i + 3] = 255; }

    /// <summary>A minimal RGBA PNG (zlib-compressed scanlines, filter 0).</summary>
    public static byte[] Png(byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void Chunk(string type, byte[] data)
        {
            WriteBE(ms, data.Length);
            var t = Encoding.ASCII.GetBytes(type); ms.Write(t);
            ms.Write(data);
            uint crc = Crc(t, data);
            WriteBE(ms, (int)crc);
        }
        var ihdr = new byte[13];
        ihdr[0] = (byte)(Width >> 24); ihdr[1] = (byte)(Width >> 16); ihdr[2] = (byte)(Width >> 8); ihdr[3] = (byte)(Width & 0xFF);
        ihdr[4] = (byte)(Height >> 24); ihdr[5] = (byte)(Height >> 16); ihdr[6] = (byte)(Height >> 8); ihdr[7] = (byte)(Height & 0xFF);
        ihdr[8] = 8; ihdr[9] = 6;   // 8-bit RGBA
        Chunk("IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                for (int y = 0; y < Height; y++) { z.WriteByte(0); z.Write(rgba, y * Width * 4, Width * 4); }
            Chunk("IDAT", raw.ToArray());
        }
        Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteBE(Stream s, int v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    private static uint[]? _table;
    private static uint Crc(byte[] a, byte[] b)
    {
        _table ??= Enumerable.Range(0, 256).Select(n => { uint c = (uint)n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; return c; }).ToArray();
        uint crc = 0xFFFFFFFFu;
        foreach (byte x in a) crc = _table[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (byte x in b) crc = _table[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
