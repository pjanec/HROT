using System.Numerics;
using Xunit;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Terrain.InputSet;

namespace Hrot.Terrain.Import.Tests;

/// <summary>
/// ⭐⭐ <c>CE-6002</c> (showcase world, T-2; docs/DESIGN_Terrain_Authoring.md §10 / §18l) — MEASURED: a river in a BASE terrain made by the import pipeline (the
/// input set's water layer → <c>TerrainFolderWriter</c> → the terrain folder → the world a node loads → the Recast baker) is impassable to men AND to ground
/// vehicles. The real importer and the real baker; the control (the same set with no river) is walkable bank to bank, so a "no path" is the water's doing.
/// 🔴 Red-proof: the importer writing no water surface (<c>TerrainFolderWriter.WriteWorld</c>'s water loop skipped) ⇒ the path across exists ⇒ red.
/// </summary>
public sealed class RiverNavmeshTests : IDisposable
{
    private readonly Env _env = new();
    public void Dispose() => _env.Dispose();

    private static MiniSet Set(bool river)
    {
        var mini = new MiniSet { Ground = (x, y) => 10f };   // flat, so a Z of 10 is the ground everywhere
        if (river)
            mini.Water = new() { new AreaFeature(new (double, double)[] { (-10, -100), (10, -100), (10, 100), (-10, 100), (-10, -100) }, "river") };
        return mini;
    }

    private async Task<DotRecastNavmeshProvider> Baked(bool river)
    {
        string folder = await new TerrainImporter(_env.Terrains, _env.Scenarios).ImportAsync(Set(river).Write(Path.Combine(_env.Root, river ? "wet" : "dry")), river ? "wet" : "dry", false, null, default);
        string def = Path.Combine(folder, "terrain.json");
        var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), Path.GetFileName(folder))!;
        if (river) Assert.Contains(world.Surfaces, s => s.Type == TerrainSurfaceType.Water);
        var layers = NavLayerMask.Infantry | NavLayerMask.Vehicle;
        return Assert.IsType<DotRecastNavmeshProvider>(new RecastNavmeshFactory { Layers = layers, TileCache = new NavTileCache(Path.Combine(_env.Root, "cache-" + river)) }.Build(world));
    }

    // the world is the set's bounds shifted to 0..200: the river is x 90..110
    private static readonly Vector3 West = new(40, 100, 10), East = new(160, 100, 10), InTheRiver = new(100, 100, 10);

    [Fact]
    public async Task ARiverImportedIntoTheBaseTerrain_IsImpassable_OnFoot_AndByVehicle_AndTheSameTerrainWithoutItIsNot()
    {
        var dry = await Baked(river: false);
        var wet = await Baked(river: true);
        foreach (var layer in new[] { NavLayerMask.Infantry, NavLayerMask.Vehicle })
        {
            Assert.True(dry.PathExists(West, East, (uint)layer), $"{layer}: the control must cross");
            Assert.True(wet.IsWalkable(West, (uint)layer) && wet.IsWalkable(East, (uint)layer), $"{layer}: both banks are land");
            Assert.False(wet.IsWalkable(InTheRiver, (uint)layer), $"{layer}: the river is not walkable");
            Assert.False(wet.PathExists(West, East, (uint)layer), $"{layer}: a path across the river");
        }
    }
}
