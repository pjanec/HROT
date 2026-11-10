using Xunit;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Import.Tests;

/// <summary>
/// ⭐ CE-4393 — the navmesh baked at import (docs/DESIGN_Terrain_Import_UTM.md §12.1): the importer bakes the written terrain into its
/// <c>navtiles/</c> through the same folder → world step and baker a node's load uses, and a load reads them as a read-only seed. Real
/// importer, real baker, real tile cache; only the per-machine cache folder is a temp folder.
/// </summary>
public sealed class NavmeshPrefillTests : IDisposable
{
    private readonly Env _env = new();
    public void Dispose() => _env.Dispose();

    private static MiniSet Town(double shift = 0)
    {
        var mini = new MiniSet();
        mini.Buildings.Add(MiniSet.Rect("a", -40 + shift, -30, -20 + shift, -14, storeys: 2));
        mini.Buildings.Add(MiniSet.Rect("b", 20, 10, 36, 24));
        return mini;
    }

    private TerrainImporter Importer(bool prefill = true) => new(_env.Terrains, _env.Scenarios) { NavmeshPrefill = prefill ? new NavmeshPrefill() : null };

    /// <summary>A node's load: the one folder → world step, then the factory over a fresh per-machine cache.</summary>
    private (RecastNavmeshFactory Factory, DotRecastNavmeshProvider Provider) Load(string folder, string ownCache)
    {
        string def = Path.Combine(folder, "terrain.json");
        var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), Path.GetFileName(folder))!;
        var factory = new RecastNavmeshFactory { TileCache = new NavTileCache(ownCache) };
        var provider = Assert.IsType<DotRecastNavmeshProvider>(factory.Build(world));
        return (factory, provider);
    }

    private static Dictionary<string, DateTime> Files(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc);

    /// <summary>✅ Red-proof (run): the baker not consulting the seed ⇒ every tile bakes on the first load ⇒ red.</summary>
    [Fact]
    public async Task APrefilledTerrains_FirstLoad_BakesNoTile_ReadsTheSeedWithoutTouchingIt_AndCopiesNothing()
    {
        string set = Town().Write(Path.Combine(_env.Root, "set"));
        var importer = Importer();
        string folder = await importer.ImportAsync(set, "town", false, null, default);
        var rep = importer.LastReport!;
        string navtiles = Path.Combine(folder, NavTileCache.TerrainFolderName);
        Assert.True(Directory.Exists(navtiles));
        Assert.True(rep.NavmeshTiles > 0);
        Assert.Equal(rep.NavmeshTiles, rep.NavmeshBaked);                       // a fresh folder: every tile baked once, at import
        Assert.True(rep.NavmeshFiles > 0 && rep.NavmeshBytes > 0);
        Assert.True(rep.StageSeconds.ContainsKey("navmesh"));

        var before = Files(navtiles);
        Thread.Sleep(20);                                                       // a touched file would carry a later time
        string own = Path.Combine(_env.Root, "own-cache");
        var (factory, _) = Load(folder, own);

        Assert.Equal(0, factory.LastStats.Baked);                               // ⭐ the first load bakes nothing
        Assert.Equal(rep.NavmeshTiles, factory.LastStats.FromCache);
        Assert.Equal(before, Files(navtiles));                                  // ⭐ the seed: not touched, nothing written, nothing pruned
        Assert.False(Directory.Exists(own) && Directory.EnumerateFiles(own, "*", SearchOption.AllDirectories).Any());   // never copied
    }

    /// <summary>⭐ A stale <c>navtiles/</c> (another terrain's, or one baked before the geometry changed) is a MISS, never a wrong tile: the
    /// tiles the change reaches bake, the rest come from the seed, and the result's tile keys equal a bake with no seed at all.</summary>
    [Fact]
    public async Task AStaleSeed_IsAMissWhereTheGeometryChanged_NeverAWrongTile()
    {
        string folderA = await Importer().ImportAsync(Town().Write(Path.Combine(_env.Root, "setA")), "a", false, null, default);
        string folderB = await Importer(prefill: false).ImportAsync(Town(shift: 30).Write(Path.Combine(_env.Root, "setB")), "b", false, null, default);
        CopyTree(Path.Combine(folderA, NavTileCache.TerrainFolderName), Path.Combine(folderB, NavTileCache.TerrainFolderName));   // A's tiles under B

        var (seeded, seededProvider) = Load(folderB, Path.Combine(_env.Root, "own-1"));
        Assert.True(seeded.LastStats.Baked > 0, "the moved building's tiles must bake");
        Assert.True(seeded.LastStats.FromCache > 0, "the untouched tiles come from the seed");

        var withoutSeed = new RecastNavmeshFactory { TileCache = new NavTileCache(Path.Combine(_env.Root, "own-2")) };
        Directory.Delete(Path.Combine(folderB, NavTileCache.TerrainFolderName), recursive: true);
        string def = Path.Combine(folderB, "terrain.json");
        var fresh = Assert.IsType<DotRecastNavmeshProvider>(withoutSeed.Build(TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "b")!));
        Assert.Equal(0, withoutSeed.LastStats.FromCache);
        Assert.Equal(fresh.TileKeys.OrderBy(k => k.Key), seededProvider.TileKeys.OrderBy(k => k.Key));   // ⭐ the same tiles, tile for tile
    }

    [Fact]
    public async Task AnImportWithoutAPrefill_WritesNoNavtiles()
    {
        string folder = await Importer(prefill: false).ImportAsync(Town().Write(Path.Combine(_env.Root, "set")), "t", false, null, default);
        Assert.False(Directory.Exists(Path.Combine(folder, NavTileCache.TerrainFolderName)));
        Assert.False(_env.Importer().LastReport?.StageSeconds.ContainsKey("navmesh") ?? false);
    }

    [Fact]
    public async Task ACancelDuringTheNavmeshBake_LeavesNoFolder()
    {
        string set = Town().Write(Path.Combine(_env.Root, "set"));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.Stage == "navmesh") cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Importer().ImportAsync(set, "c", false, progress, cts.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_env.Terrains));
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string dst = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f, dst);
        }
    }
}
