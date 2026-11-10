using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ CE-4393 (docs/DESIGN_Terrain_Import_UTM.md §12.1) — bakes a written terrain folder's whole navmesh into <c>&lt;folder&gt;/navtiles</c>
/// (<see cref="NavTileCache.TerrainFolderName"/>). ⭐ The SAME two steps as a node's load — <see cref="TerrainFolderWorld"/> then
/// <see cref="RecastNavmeshFactory"/> (both layers, doorways, woods, fine areas) — so every tile key a load computes is found there by
/// construction; only the cache differs (<see cref="NavTileCache.ForPrefill"/>: written, never pruned, nothing kept in memory).
/// </summary>
public sealed class NavmeshPrefill : ITerrainNavmeshPrefill
{
    public NavmeshPrefillReport Bake(string terrainFolder, IProgress<double>? progress, CancellationToken cancellation)
    {
        string definitionPath = Path.Combine(terrainFolder, "terrain.json");
        var definition = TerrainFolderWorld.ReadDefinition(definitionPath);
        var world = TerrainFolderWorld.ReadWorld(definitionPath, definition, Path.GetFileName(Path.GetFullPath(terrainFolder)));
        if (world == null) return default;   // a terrain with no world has nothing to walk on

        string navtiles = Path.Combine(terrainFolder, NavTileCache.TerrainFolderName);
        var factory = new RecastNavmeshFactory
        {
            TileCache = NavTileCache.ForPrefill(navtiles),
            Progress = progress == null ? null : (done, total) => progress.Report(total == 0 ? 1.0 : (double)done / total),
            Cancel = cancellation,
        };
        factory.Build(world);
        var st = factory.LastStats;
        int files = 0; long bytes = 0;
        if (Directory.Exists(navtiles))
            foreach (var f in Directory.EnumerateFiles(navtiles, "*.navtile", SearchOption.AllDirectories)) { files++; bytes += new FileInfo(f).Length; }
        return new NavmeshPrefillReport(st.Baked + st.FromCache, st.Baked, files, bytes);
    }
}
