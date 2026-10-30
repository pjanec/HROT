using System.Security.Cryptography;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4021 — loads the terrain's SUMO net with the terrain (<see cref="TerrainResidency.AttachContent"/>): parses it off
/// the main thread, publishes a <see cref="TrafficNetCache"/> on commit, clears it when the terrain has no net or is unloaded.
/// </summary>
public sealed class SumoNetTerrainLoader : ITerrainContentLoader
{
    /// <inheritdoc/>
    public object? Prepare(TerrainDefinition definition, string terrainFolder, string terrainName, TerrainWorld world)
    {
        if (definition.SumoNet is not { } declared) return null;

        string path = Path.Combine(terrainFolder, declared.File);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException(
                $"[Traffic] Terrain '{terrainName}' declares the SUMO net '{declared.File}', which does not exist at '{path}'.", path);

        byte[] bytes = System.IO.File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(bytes))[..16];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        NetworkModel model;
        model = NetworkParser.ParseXml(System.Text.Encoding.UTF8.GetString(bytes));
        var placement = SumoNetPlacement.For(model, declared, definition.Origin);
        FdpLog<SumoNetTerrainLoader>.Info(
            $"[Traffic] '{terrainName}': SUMO net '{declared.File}' parsed in {sw.ElapsedMilliseconds} ms — {model.Edges.Count} edges, "
          + $"{model.Junctions.Count} junctions, {model.TlLogicsById.Count} traffic lights, hash {hash}, placed "
          + (placement.IsGeoreferenced ? "through its UTM projection and the terrain origin." : $"by the anchor ({declared.AnchorX}, {declared.AnchorY})."));
        sw.Restart();
        var roads = TrafficRoadScene.Build(model, placement, world.BoundsMin, world.BoundsMax);
        FdpLog<SumoNetTerrainLoader>.Info($"[Traffic] '{terrainName}': road scene built in {sw.ElapsedMilliseconds} ms — {roads.TriangleCount} triangles, "
          + $"{roads.Signals.Count} signal heads.");
        AttachBeyond(world, model, placement);
        return new TrafficNetCache(declared.File, System.IO.Path.GetFullPath(path), model, hash, placement, roads);
    }

    /// <summary>
    /// ⭐ CE-4122 (HB2, <c>docs/DESIGN_Map_3D_Mode.md</c> §6ad-2) — the ground beyond our terrain follows SUMO's own height field where the net is:
    /// the field is built once, and handed to the world in LOCAL metres through the placement (its footprint is the net's bounds, which a UTM
    /// placement may rotate). ⭐ Runs in <see cref="Prepare"/> — before the world is published — so the world is immutable by the time anyone reads it
    /// (and the render geometry's identity, the world object, is new for the load). A world with no rectangle (no world file) has no "beyond".
    /// </summary>
    private static void AttachBeyond(TerrainWorld world, NetworkModel model, SumoNetPlacement placement)
    {
        if (!(world.BoundsMax.X > world.BoundsMin.X && world.BoundsMax.Y > world.BoundsMin.Y) || world.Beyond != null) return;
        var field = Sim.Scene.TerrainField.FromNetwork(model);
        var (minX, minY, maxX, maxY) = TrafficRoadScene.NetBounds(model);
        System.Numerics.Vector2 Corner(double nx, double ny) { var (lx, ly) = placement.ToLocal(nx, ny); return new System.Numerics.Vector2((float)lx, (float)ly); }
        var quad = new[] { Corner(minX, minY), Corner(maxX, minY), Corner(maxX, maxY), Corner(minX, maxY) };
        world.AttachBeyond(new BeyondGround((lx, ly) =>
        {
            var (nx, ny) = placement.ToNet(lx, ly);
            return (float)field.HeightAt(nx, ny);
        }, quad));
    }

    /// <inheritdoc/>
    public void Commit(EntityRepository world, object? prepared)
    {
        world.RegisterManagedComponent<TrafficNetCache>();
        world.SetSingletonManaged((TrafficNetCache?)prepared!);
    }

    /// <inheritdoc/>
    public void Unload(EntityRepository world)
    {
        if (world.HasSingletonManaged<TrafficNetCache>()) world.SetSingletonManaged<TrafficNetCache>(null!);
    }
}
