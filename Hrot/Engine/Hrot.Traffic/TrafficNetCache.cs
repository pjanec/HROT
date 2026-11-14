using Fdp.Core;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 R-4021 / R-4022 C — the terrain's SUMO net, parsed ONCE per terrain on every node that composes traffic, and where
/// it sits. The SUMO engine, the mirror's dead reckoning (lanes) and the road layer all read THIS — one parse, three readers.
/// <see cref="NetHash"/> lets a receiver confirm it parsed the same net the publisher steps (R-4022 C).
/// <para>⛔ Never persisted — re-derived from the terrain on every load, so it cannot disagree with it.</para>
/// </summary>
// ⭐ G-7 / CE-4354 NodeLocal (DESIGN_Review_Perspective §6.4 I9): a per-node traffic network cache — never copied into a merged view.
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay | DataPolicy.NodeLocal)]
[ComponentId(GlobalComponentIds.TrafficNetCache)]
public sealed class TrafficNetCache
{
    public TrafficNetCache(string file, string path, NetworkModel model, string netHash, SumoNetPlacement placement,
                           TrafficRoadScene? roads = null, Func<float, float, float>? drape = null)
    {
        File = file; Path = path; Model = model; NetHash = netHash; Placement = placement; Roads = roads ?? TrafficRoadScene.Empty; Drape = drape;
    }

    /// <summary>⭐ AQ93 V7 (R-4049) — the net's static road scene in terrain-local metres, built with the parse (off the main thread).
    /// Every map host draws THIS: the 3-D layer and the 2-D gizmo.</summary>
    public TrafficRoadScene Roads { get; }

    /// <summary>⭐ R-4030 (E2b) — our ground at a terrain-local (x, y) when the net is FLAT (no lane z), else <c>null</c>. The cars and pedestrians
    /// the mirrors resolve take <c>z + Drape(x, y)</c>, the same function the road scene was laid with; a net with z is used as-is (R-4025).</summary>
    public Func<float, float, float>? Drape { get; }

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
