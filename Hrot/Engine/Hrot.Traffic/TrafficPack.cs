using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common.Services;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 EM-1 (R-4045) / R-4047 — SUMO traffic as ONE node capability, composed by any host that should run it: the editor first
/// (embedded, networkless), a dedicated traffic node later (<see cref="NodeRole.Traffic"/>, R-4022). It attaches the SUMO-net loader
/// to the host's terrain residency (the net rides the terrain, R-4021) and registers the two traffic modules. Idle unless the
/// terrain has a net AND the scenario has the traffic entity: then zero SUMO work per frame.
/// </summary>
public sealed class TrafficPack : INodeCapability, IDisposable
{
    private readonly TerrainResidency _residency;

    /// <param name="residency">The host's terrain residency; the net loader is attached to it once, here.</param>
    public TrafficPack(TerrainResidency residency)
    {
        _residency = residency ?? throw new ArgumentNullException(nameof(residency));
        _residency.AttachContent(new SumoNetTerrainLoader());
    }

    public string Key => CapabilityKeys.Traffic;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    /// <summary>The thread hand-off (null before <see cref="Register"/>).</summary>
    public TrafficRuntime? Runtime { get; private set; }

    /// <summary>The main-thread mirror (null before <see cref="Register"/>): cars, replicas, light states.</summary>
    public TrafficMirrorSystem? Mirror { get; private set; }

    public void Register(HrotNodeContext context, NodeBootValues values)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        context.World.RegisterComponent<ReplicationRoute>();
        context.World.RegisterComponent<TrafficCar>();
        Runtime = new TrafficRuntime();
        Mirror = new TrafficMirrorSystem(Runtime, context.IdAllocator, context.NodeId);
        context.Kernel.RegisterModule(new TrafficMirrorModule(Mirror));
        context.Kernel.RegisterModule(new TrafficSteppingModule(Runtime));
    }

    public void Dispose() => Runtime?.Dispose();
}
