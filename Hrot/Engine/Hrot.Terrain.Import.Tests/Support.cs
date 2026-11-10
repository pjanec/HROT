using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;
using Hrot.Terrain.Osm.Tests;
using Hrot.Tools.TerrainSynth;

namespace Hrot.Terrain.Import.Tests;

/// <summary>Sets and loading for the importer's rails. The strongest check of an import is that the terrain LOADS through the real parsers.</summary>
public sealed class Env : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "hrot-import-tests-" + Guid.NewGuid().ToString("N")[..10]);
    public string Terrains => Path.Combine(Root, "terrains");
    public string Scenarios => Path.Combine(Root, "scenarios");
    public Env() { Directory.CreateDirectory(Terrains); Directory.CreateDirectory(Scenarios); }
    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }

    public TerrainImporter Importer() => new(Terrains, Scenarios);

    /// <summary>hrot-terrain-synth: tiny, seed 3, 4 m posts, real noise, terraces, materials, extras (the InputSet tests' set).</summary>
    public string Synth(params string[] extra)
    {
        string dir = Path.Combine(Root, "set-" + Guid.NewGuid().ToString("N")[..8]);
        var args = new[] { "--preset", "tiny", "--seed", "3", "--height-spacing", "4", "--noise", "real", "--terraces", "on", "--materials", "on", "--extras", "--out", dir }.Concat(extra).ToArray();
        if (TerrainSynthesizer.Run(SynthOptions.Parse(args), new StringWriter()) != 0) throw new InvalidOperationException("synth failed");
        return dir;
    }

    /// <summary>The synthetic town through the OSM pipeline with a test DEM and a test road builder.</summary>
    public async Task<string> OsmFixtureSet()
    {
        string outDir = Path.Combine(Root, "osm-" + Guid.NewGuid().ToString("N")[..8]);
        var p = Fx.Pipeline(FuncDem.EastSlope(), new TestRoads(), new DownloadCache(Path.Combine(Root, "cache")));
        return await p.WriteInputSetAsync(Fx.Box, Fx.Options, outDir, null, default);
    }

    public static TerrainWorld Load(string terrainFolder, string name)
    {
        var def = TerrainDefinitionParser.Parse(File.ReadAllText(Path.Combine(terrainFolder, "terrain.json")));
        var assets = TerrainAssets.ForFolder(terrainFolder, Path.Combine(Path.GetTempPath(), "no-shared-buildings"));
        return TerrainWorldParser.Parse(File.ReadAllText(Path.Combine(terrainFolder, def.World)), name, assets);
    }
}

/// <summary>Reports on the calling thread (no synchronisation context): the importer's stages, as they happen.</summary>
internal sealed class SyncProgress : IProgress<ImportProgress>
{
    private readonly Action<ImportProgress> _f;
    public SyncProgress(Action<ImportProgress> f) => _f = f;
    public void Report(ImportProgress v) => _f(v);
}
