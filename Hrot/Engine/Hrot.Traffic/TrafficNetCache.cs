using Fdp.Core;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4021 / R-4022 C — the terrain's SUMO net, parsed ONCE per terrain on every node that composes traffic, and where
/// it sits. The SUMO engine, the mirror's dead reckoning (lanes) and the road layer all read THIS — one parse, three readers.
/// <see cref="NetHash"/> lets a receiver confirm it parsed the same net the publisher steps (R-4022 C).
/// <para>⛔ Never persisted — re-derived from the terrain on every load, so it cannot disagree with it.</para>
/// </summary>
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
[ComponentId(GlobalComponentIds.TrafficNetCache)]
public sealed class TrafficNetCache
{
    public TrafficNetCache(string file, string path, NetworkModel model, string netHash, SumoNetPlacement placement)
    {
        File = file; Path = path; Model = model; NetHash = netHash; Placement = placement;
    }

    /// <summary>The net file, relative to the terrain folder (as <c>terrain.json</c> names it).</summary>
    public string File { get; }

    /// <summary>The net file on THIS node's disk (the resident terrain's folder + <see cref="File"/>) — what SUMO itself loads.</summary>
    public string Path { get; }

    /// <summary>The parsed net (SumoSharp's model).</summary>
    public NetworkModel Model { get; }

    /// <summary>SHA-256 of the net file's bytes, first 16 hex digits.</summary>
    public string NetHash { get; }

    /// <summary>Net metres → terrain local metres.</summary>
    public SumoNetPlacement Placement { get; }

    /// <summary>The cache resident in <paramref name="world"/>, or <c>null</c> when the terrain carries no SUMO net.</summary>
    public static TrafficNetCache? Of(EntityRepository world)
        => world.HasSingletonManaged<TrafficNetCache>() ? world.GetSingletonManaged<TrafficNetCache>() : null;
}
