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
    public object? Prepare(TerrainDefinition definition, string terrainFolder, string terrainName)
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
        return new TrafficNetCache(declared.File, model, hash, placement);
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
