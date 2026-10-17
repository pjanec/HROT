using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Infrastructure;
using Hrot.Map.Common.Services;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 EM-1 (R-4045), §12 N1/N6/N8 (R-4054, R-4056) — SUMO traffic as ONE node capability, in two shapes:
/// <list type="bullet">
///   <item><see cref="Serving"/> — the node RUNS SUMO for the traffic settings it is granted (the dedicated <c>--mode traffic</c> node, and
///   the networkless editor, which is its own traffic node), tees its cars to DDS when it has a participant, AND mirrors every wire.</item>
///   <item><see cref="Mirroring"/> — every other map or sim host (SimHost, CGF, IG, Player): mirrors every traffic node's wire, so its
///   sensors, rounds and maps see the cars.</item>
/// </list>
/// Both make every terrain residency of the process load the SUMO net (the net rides the terrain, R-4021) and are idle without a net.
/// </summary>
public sealed class TrafficPack : INodeCapability, IDisposable
{
    private readonly bool _serves;

    /// <summary>The key the SUMO-net loader registers under (<see cref="TerrainResidency.RegisterContentLoader"/>).</summary>
    public const string NetLoaderKey = "sumo-net";

    private TrafficPack(bool serves)
    {
        _serves = serves;
        // ⭐ N6 / R-4021 — every residency in this process loads the terrain's SUMO net from its next terrain on, whenever the host
        //   built it (each host builds its residency after its plan, deep in its own orchestration code).
        TerrainResidency.RegisterContentLoader(NetLoaderKey, () => new SumoNetTerrainLoader());
    }

    /// <summary>A node that runs SUMO (role <c>Traffic</c>).</summary>
    public static TrafficPack Serving() => new(serves: true);

    /// <summary>A node that only mirrors other nodes' SUMO.</summary>
    public static TrafficPack Mirroring() => new(serves: false);

    public bool Serves => _serves;

    public string Key => _serves ? CapabilityKeys.Traffic : CapabilityKeys.TrafficMirror;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    /// <summary>The thread hand-off (null on a mirroring node, and before <see cref="Register"/>).</summary>
    public TrafficRuntime? Runtime { get; private set; }

    /// <summary>The main-thread mirror (null before <see cref="Register"/>): cars, replicas, senders.</summary>
    public TrafficMirrorSystem? Mirror { get; private set; }

    /// <summary>
    /// ⭐ The ONE hook every host calls (editor, SimHost, CGF, IG — measured: CGF and IG never call <c>Register</c>, and IG resolves its
    /// plan before it has a context): the mirror joins the INPUT phase, and a serving node's stepping module is registered on the
    /// node's kernel from the same context.
    /// </summary>
    public void PopulateSystems(HrotNodeContext context, List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> input,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> simulation,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> postSimulation)
    {
        // ⚠ A null context is the SHAPE probe several host rails make (every other capability ignores it): the pack composes from the
        //   node's kernel, participant and allocator, so it contributes nothing there. Every production host passes its context.
        if (context is null) return;
        if (Mirror is not null) throw new InvalidOperationException("TrafficPack is composed once per node.");
        context.World.RegisterComponent<ReplicationRoute>();
        context.World.RegisterComponent<TrafficCar>();
        if (_serves)
        {
            long local = 0;
            var ids = context.IdAllocator;
            Runtime = new TrafficRuntime(new TrafficServeOptions(
                NextNetworkId: ids is not null ? ids.AllocateId : () => Interlocked.Decrement(ref local),
                Participant: context.Participant,
                Source: (uint)context.NodeId));
            context.Kernel.RegisterModule(new TrafficSteppingModule(Runtime));
        }
        Mirror = new TrafficMirrorSystem(Runtime, context.Participant, context.EntityMap, context.NodeId);
        input.Add(Mirror);
    }

    public void Dispose() => Runtime?.Dispose();
}
