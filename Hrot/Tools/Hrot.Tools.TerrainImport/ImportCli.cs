using Hrot.Terrain.Import;

namespace Hrot.Tools.TerrainImport;

/// <summary><c>hrot-terrain-import --set DIR --name N [--replace] [--terrains DIR]</c> — prints counts, warnings and seconds per stage.</summary>
public static class ImportCli
{
    public const string Usage = "usage: hrot-terrain-import --set DIR --name NAME [--replace] [--terrains DIR] [--scenarios DIR] [--verify]";

    /// <summary>--verify: parses the written terrain with the real terrain loader (definition + world + templates + height store) and says how long it took.</summary>
    private static void Verify(string folder, string name, TextWriter output)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var def = Fdp.Toolkit.Terrain.TerrainDefinitionParser.Parse(File.ReadAllText(Path.Combine(folder, "terrain.json")));
        var assets = Fdp.Toolkit.Terrain.TerrainAssets.ForFolder(folder, Path.Combine(Path.GetTempPath(), "no-shared-buildings"));
        var world = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(folder, def.World)), name, assets);
        output.WriteLine(System.FormattableString.Invariant(
            $"verify: the terrain loader parsed it in {sw.Elapsed.TotalSeconds:0.0} s — {world.Buildings.Count} buildings ({world.Buildings.Count(b => b.Solid)} solid), {world.Prisms.Count} prisms, {world.Panels.Count} wall panels, {world.Doors.Count} doors, {world.Walkables.Count} slabs/ramps, {world.Surfaces.Count} surfaces"));
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        string? set = null, name = null, terrains = null, scenarios = null; bool replace = false, verify = false;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value");
                switch (args[i])
                {
                    case "--set": set = Next(); break;
                    case "--name": name = Next(); break;
                    case "--terrains": terrains = Next(); break;
                    case "--scenarios": scenarios = Next(); break;
                    case "--replace": replace = true; break;
                    case "--verify": verify = true; break;
                    default: throw new ArgumentException("unknown argument " + args[i]);
                }
            }
            if (set == null) throw new ArgumentException("--set is required");
            if (name == null) throw new ArgumentException("--name is required");
        }
        catch (ArgumentException e) { error.WriteLine(e.Message); error.WriteLine(Usage); return 2; }

        try
        {
            var importer = new TerrainImporter(terrains, scenarios);
            if (replace && Directory.Exists(Path.Combine(importer.TerrainsRoot, name)))
            {
                var used = importer.ScenariosUsing(name);
                if (used.Count > 0) output.WriteLine($"replacing '{name}' — used by {used.Count} scenario(s): {string.Join(", ", used.Take(10))}");
            }
            string folder = await importer.ImportAsync(set, name, replace, null, ct).ConfigureAwait(false);
            importer.LastReport!.WriteTo(output);
            output.WriteLine("terrain written to " + Path.GetFullPath(folder));
            if (verify) Verify(folder, name, output);
            return 0;
        }
        catch (OperationCanceledException) { error.WriteLine("cancelled — nothing was written"); return 130; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or FileNotFoundException)
        {
            error.WriteLine("failed: " + e.Message);
            return 1;
        }
    }
}
