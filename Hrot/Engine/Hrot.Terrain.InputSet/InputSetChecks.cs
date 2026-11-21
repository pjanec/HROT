namespace Hrot.Terrain.InputSet;

/// <summary>
/// ⭐ The input set's FORMAT conformance — what every producer's output must pass and what the importer may rely on: the files exist, the
/// manifest's hashes and sizes match, the height grid's size agrees with its description, every polygon is closed and counter-clockwise,
/// ids are unique, the per-building fields are consistent. ⛔ It does NOT check the clean-geometry rules (doors on their floor, headings outward):
/// a real set has noise (§10.7: 3 % of headings point inwards) and the importer tolerates it.
/// </summary>
public static class InputSetChecks
{
    public static IReadOnlyList<string> Check(string folder)
    {
        var problems = new List<string>();
        foreach (var rel in InputSetLayout.Required)
            if (!File.Exists(Path.Combine(folder, rel))) problems.Add($"missing {rel}");
        if (problems.Count > 0) return problems;

        InputSetReader set;
        try { set = InputSetReader.Open(folder); }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException) { return new[] { "manifest: " + e.Message }; }

        var m = set.Manifest;
        if (!(m.MinX < m.MaxX && m.MinY < m.MaxY)) problems.Add("manifest: bounds are empty or inverted");
        if (m.Frame.Zone < 1 || m.Frame.Zone > 60) problems.Add($"manifest: UTM zone {m.Frame.Zone} is not 1…60");
        foreach (var rel in InputSetLayout.Required.Concat(InputSetLayout.Optional))
        {
            if (rel == InputSetLayout.Manifest || !set.Has(rel)) continue;
            if (!m.Files.TryGetValue(rel, out var f)) { problems.Add($"manifest: {rel} is not listed"); continue; }
            if (f.Bytes != new FileInfo(set.PathOf(rel)).Length) problems.Add($"manifest: {rel} size differs");
            else if (!string.Equals(f.Sha256, Manifest.Sha256(set.PathOf(rel)), StringComparison.OrdinalIgnoreCase)) problems.Add($"manifest: {rel} sha256 differs");
        }

        try
        {
            var h = set.Heights;
            long expected = (long)h.Cols * h.Rows * 4, actual = new FileInfo(set.PathOf(InputSetLayout.HeightsF32)).Length;
            if (expected != actual) problems.Add($"heights: heights.f32 is {actual} bytes, cols × rows × 4 = {expected}");
            if (h.CellSize <= 0) problems.Add("heights: cellSize must be positive");
            if (h.Frame != m.Frame) problems.Add("heights: the frame differs from the manifest's");
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException) { problems.Add("heights: " + e.Message); }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in set.ReadBuildings())
        {
            string who = $"building {b.Id}";
            if (string.IsNullOrEmpty(b.Id)) problems.Add("a building has no id");
            else if (!ids.Add(b.Id)) problems.Add($"{who}: duplicate id");
            Ring(problems, who, b.Footprint);
            if (b.StoreyZ.Count == 0) problems.Add($"{who}: no storeys");
            for (int i = 1; i < b.StoreyZ.Count; i++)
                if (!(b.StoreyZ[i] > b.StoreyZ[i - 1])) { problems.Add($"{who}: storeyZ not ascending"); break; }
            if (b.StoreyZ.Count > 0 && Math.Abs(b.StoreyZ[0] - b.BaseZ) > 1e-6) problems.Add($"{who}: storeyZ[0] != baseZ");
            if (!(b.Height > 0)) problems.Add($"{who}: height must be positive");
            if (b.Roof != "flat" && b.Roof != "pitched") problems.Add($"{who}: roof '{b.Roof}' is not flat / pitched");
            if (b.Roof == "pitched" && !(b.RidgeZ > b.BaseZ + b.Height - 1e-6)) problems.Add($"{who}: a pitched roof's ridgeZ must be above the eaves");
            foreach (var o in b.Openings)
            {
                if (o.Type != "door" && o.Type != "window") { problems.Add($"{who}: opening type '{o.Type}'"); break; }
                if (o.Storey < 0 || o.Storey >= b.StoreyZ.Count) { problems.Add($"{who}: an opening's storey {o.Storey} is outside 0…{b.StoreyZ.Count - 1}"); break; }
                if (!(o.Heading >= 0 && o.Heading < 360)) { problems.Add($"{who}: heading {o.Heading} is not in [0, 360)"); break; }
            }
        }
        foreach (var o in set.ReadUnassigned())
            if (o.Type != "door" && o.Type != "window") { problems.Add($"unassigned: type '{o.Type}'"); break; }
        foreach (var rel in new[] { InputSetLayout.Water, InputSetLayout.Scrub, InputSetLayout.Woods })
            foreach (var a in set.ReadAreas(rel))
            {
                Ring(problems, rel, a.Ring);
                if (rel == InputSetLayout.Woods && !(a.Density > 0)) problems.Add($"{rel}: a wood without a positive density");
            }
        foreach (var w in set.ReadWalls())
            if (w.Line.Count < 2 || !(w.Height > 0)) { problems.Add("walls: a wall needs ≥ 2 points and a positive height"); break; }
        try { set.ReadTrees(); } catch (FormatException e) { problems.Add("trees: " + e.Message); } catch (InvalidDataException e) { problems.Add("trees: " + e.Message); }
        return problems;
    }

    private static void Ring(List<string> problems, string who, IReadOnlyList<(double X, double Y)> ring)
    {
        if (ring.Count < 4) { problems.Add($"{who}: a ring needs ≥ 4 points (closed)"); return; }
        if (ring[0] != ring[^1]) { problems.Add($"{who}: the ring is not closed (first != last)"); return; }
        double a2 = 0;
        for (int i = 0; i + 1 < ring.Count; i++) a2 += (ring[i].X - ring[0].X) * (ring[i + 1].Y - ring[0].Y) - (ring[i + 1].X - ring[0].X) * (ring[i].Y - ring[0].Y);
        if (!(a2 > 0)) problems.Add($"{who}: the ring is not counter-clockwise (or has no area)");
    }
}
