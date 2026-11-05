using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.Common.Orders;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join D-4 / §7c (R-4115) — the gameplay CLIENT as ONE node capability, composed by default on the Player host (<c>IgNodeBootstrapper</c>, beside
/// <c>TrafficPack.Mirroring()</c>): the plain <see cref="PlayerSlotReaderSystem"/> (reads only while a server exists) and <see cref="DeployFlowSystem"/> (Input).
/// ⛔ No <c>[NodeFeature]</c> on either — the Player declares no gameplay feature; its wire stays lazy: nothing is created until the flow needs it, so a Player with
/// no deploy server puts no gameplay topic on the bus.
/// </summary>
public sealed class GameplayClientPack : INodeCapability
{
    public const string CapabilityKey = "gameplay.client";

    private readonly Func<HrotNodeContext, DeployServerElection?>? _election;

    /// <param name="election">Reads the cluster cache of the node being composed (null ⇒ no server is ever seen: the Player observes).</param>
    public GameplayClientPack(Func<HrotNodeContext, DeployServerElection?>? election = null) => _election = election;

    public string Key => CapabilityKey;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    public GameplayWire? Wire { get; private set; }

    public DeployFlowSystem? Flow { get; private set; }

    /// <summary>The composition proper — what a host rail can also drive without a whole node.</summary>
    public void Compose(CycloneDDS.Runtime.DdsParticipant? participant, Fdp.Core.EntityRepository world, Fdp.Toolkit.Replication.Services.NetworkEntityMap entityMap,
                        int nodeId, DeployFlowEnvironment env, List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> input)
    {
        if (Wire is not null) throw new InvalidOperationException("GameplayClientPack is composed once per node.");
        Wire = new GameplayWire(participant);
        Flow = new DeployFlowSystem(Wire, world, entityMap, nodeId, env);
        var flow = Flow;
        input.Add(new PlayerSlotReaderSystem(Wire, () => flow.ServerExists));
        input.Add(flow);
    }

    public void PopulateSystems(HrotNodeContext context,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> input,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> simulation,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> postSimulation)
    {
        if (context is null) return;   // the SHAPE probe (TrafficPack's rule)
        var election = _election?.Invoke(context);
        // ⭐ the node's ONE EntityOrders recipe (PossessionStation builds it the same way); this node IS the station
        var orders = new EntityOrders(context.World, context.EntityMap, context.GeoTransform) { Station = context.NodeId.Value };
        var env = new DeployFlowEnvironment(
            GateOpen:      () => context.ClusterSlave.View.GateOpen(HostGatePolicy.Player),
            OperatingLive: () => context.ClusterSlave.View.State == ClusterState.OperatingLive,
            ServerExists:  () => election?.AnyServer() ?? false,
            Possess:       unit => { var r = orders.Possess(unit); return (r.Accepted, r.Detail); });
        Compose(context.Participant, context.World, context.EntityMap, context.NodeId.Value, env, input);
    }
}
