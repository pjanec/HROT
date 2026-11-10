using Hrot.Terrain.Import;

namespace Hrot.Tools.TerrainImport;

/// <summary><c>hrot-terrain-import --set DIR --name N [--replace] [--terrains DIR]</c> — prints counts, warnings and seconds per stage.</summary>
public static class ImportCli
{
    /// <summary>⭐ CE-4393 — the long stages (the navmesh bake takes minutes on a large terrain) say where they are: every 10 %, on any thread.</summary>
    internal sealed class StageProgress : IProgress<Hrot.Terrain.InputSet.ImportProgress>
    {
        private readonly TextWriter _out;
        private readonly object _gate = new();
        private string? _stage;
        private int _tenth = -1;
        public StageProgress(TextWriter output) => _out = output;
        public void Report(Hrot.Terrain.InputSet.ImportProgress p)
        {
            if (p.Stage != "navmesh") return;   // the other stages are quick; their seconds are in the report
            int tenth = (int)(p.Fraction * 10);
            lock (_gate)
            {
                if (p.Stage == _stage && tenth <= _tenth) return;
                _stage = p.Stage; _tenth = tenth;
                _out.WriteLine(System.FormattableString.Invariant($"{p.Stage}: {p.Fraction * 100:0} %"));
            }
        }
    }

    public const string Usage = "usage: hrot-terrain-import --set DIR --name NAME [--replace] [--terrains DIR] [--scenarios DIR] [--verify] [--no-navmesh]";

    /// <summary>
    /// --verify: reads the written terrain through the ONE folder → world step a node's load uses (definition + world + templates + height
    /// store) and says how long it took; ⭐ CE-4393 — then bakes its navmesh the way a first load does (the prefilled <c>navtiles/</c> as the
    /// seed, a memory-only cache: nothing written) and says how many tiles came from the seed.
    /// </summary>
    private static void Verify(string folder, string name, TextWriter output)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string definitionPath = Path.Combine(folder, "terrain.json");
        var def = Fdp.Toolkit.Terrain.TerrainFolderWorld.ReadDefinition(definitionPath);
        var world = Fdp.Toolkit.Terrain.TerrainFolderWorld.ReadWorld(definitionPath, def, name);
        if (world == null) { output.WriteLine("verify: the terrain declares no world"); return; }
        output.WriteLine(System.FormattableString.Invariant(
            $"verify: the terrain loader parsed it in {sw.Elapsed.TotalSeconds:0.0} s — {world.Buildings.Count} buildings ({world.Buildings.Count(b => b.Solid)} solid), {world.Prisms.Count} prisms, {world.Panels.Count} wall panels, {world.Doors.Count} doors, {world.Walkables.Count} slabs/ramps, {world.Surfaces.Count} surfaces"));
        sw.Restart();
        var factory = new Fdp.Toolkit.Navigation.Recast.RecastNavmeshFactory { TileCache = new Fdp.Toolkit.Navigation.Recast.NavTileCache(folder: null) };
        factory.Build(world);
        var st = factory.LastStats;
        output.WriteLine(System.FormattableString.Invariant(
            $"verify: the first load's navmesh in {sw.Elapsed.TotalSeconds:0.0} s — {st.Tiles} tiles, {st.FromCache} from navtiles/, {st.Baked} baked"));
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        string? set = null, name = null, terrains = null, scenarios = null; bool replace = false, verify = false, noNavmesh = false;
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
                    case "--no-navmesh": noNavmesh = true; break;   // ⭐ CE-4393 — skip the navmesh prefill (the first load then bakes it)
                    default: throw new ArgumentException("unknown argument " + args[i]);
                }
            }
            if (set == null) throw new ArgumentException("--set is required");
            if (name == null) throw new ArgumentException("--name is required");
        }
        catch (ArgumentException e) { error.WriteLine(e.Message); error.WriteLine(Usage); return 2; }

        try
        {
            // ⭐ CE-4393 (UTM §12.1) — the navmesh baked into the terrain folder, so its first load bakes nothing
            var importer = new TerrainImporter(terrains, scenarios) { NavmeshPrefill = noNavmesh ? null : new NavmeshPrefill() };
            if (replace && Directory.Exists(Path.Combine(importer.TerrainsRoot, name)))
            {
                var used = importer.ScenariosUsing(name);
                if (used.Count > 0) output.WriteLine($"replacing '{name}' — used by {used.Count} scenario(s): {string.Join(", ", used.Take(10))}");
            }
            var stages = new StageProgress(output);
            string folder = await importer.ImportAsync(set, name, replace, stages, ct).ConfigureAwait(false);
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
