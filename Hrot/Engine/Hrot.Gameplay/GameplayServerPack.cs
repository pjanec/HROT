using Hrot.Common.Infrastructure;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join D-2 / §7c (R-4115) — the gameplay SERVER as ONE node capability, composed by default on the Brain node (<c>CgfNodeBootstrapper</c>, beside
/// <c>TrafficPack.Mirroring()</c>) — not in the orchestrator (R-4102). It declares the feature <see cref="GameplayFeatures.DeployServerId"/> (ON) and adds, in the
/// Input phase, <see cref="DeployProcessorSystem"/> and the player-slot reader; both are gated by that feature, and the lazy <see cref="GameplayWire"/> creates a
/// topic only when one of them first runs — so a node with the feature OFF has no gameplay topic on the bus.
/// </summary>
public sealed class GameplayServerPack : INodeCapability
{
    /// <summary>The capability key.</summary>
    public const string CapabilityKey = "gameplay.server";

    private readonly Func<HrotNodeContext, DeployServerElection?>? _election;
    private readonly Func<Hrot.Common.EntityCreation.EntityCreation?>? _creation;

    /// <param name="creation">⭐ S2 — the node's entity-creation pack (the bootstrapper's <c>EntityCreation</c>, resolved LAZILY: it is built after the capabilities resolve), through which a role's man is spawned; null ⇒ role requests are refused.</param>
    /// <param name="election">Reads the cluster cache of the node being composed (null ⇒ no election: this node always serves). The host passes it because this assembly does not reference the NED network layer.</param>
    public GameplayServerPack(Func<HrotNodeContext, DeployServerElection?>? election = null, Func<Hrot.Common.EntityCreation.EntityCreation?>? creation = null)
    {
        _election = election;
        _creation = creation;
    }

    public string Key => CapabilityKey;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    public IReadOnlyList<FeatureDeclaration> Features { get; } = new[] { GameplayFeatures.DeployServer };

    /// <summary>The wire (null before <see cref="PopulateSystems"/>) — the rails read what was created.</summary>
    public GameplayWire? Wire { get; private set; }

    public DeployProcessorSystem? Processor { get; private set; }

    /// <summary>
    /// The composition proper, from the pieces a node context holds — ⭐ the one place the pack's systems are built, which a host rail can also drive without a whole node.
    /// </summary>
    public void Compose(CycloneDDS.Runtime.DdsParticipant? participant, Fdp.Toolkit.Replication.Services.NetworkEntityMap entityMap, int nodeId,
                        DeployServerElection? election, List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> input,
                        DeployProcessorSystem.RoleSpawner? spawner = null, Random? rng = null)
    {
        if (Wire is not null) throw new InvalidOperationException("GameplayServerPack is composed once per node.");
        Wire = new GameplayWire(participant);
        Processor = new DeployProcessorSystem(Wire, entityMap, nodeId, election, spawner: spawner, rng: rng);
        input.Add(Processor);
        input.Add(new ServerPlayerSlotReaderSystem(Wire));
    }

    public void PopulateSystems(HrotNodeContext context,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> input,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> simulation,
                                List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> postSimulation)
    {
        // ⚠ A null context is the SHAPE probe several host rails make; every production host passes its context (TrafficPack's rule).
        if (context is null) return;
        Compose(context.Participant, context.EntityMap, context.NodeId.Value, _election?.Invoke(context), input, SpawnerOf(_creation));
    }

    /// <summary>The spawner over the node's creation pack: one man, owned by THIS node (the Brain that runs the server), his network id
    /// PRE-ALLOCATED so the server knows him from the start (⭐ CE-4266 — the load phase's and traffic's way; nothing about the request stays on
    /// him); 0 when the pack is not there (yet).</summary>
    public static DeployProcessorSystem.RoleSpawner? SpawnerOf(Func<Hrot.Common.EntityCreation.EntityCreation?>? creation)
        => creation is null ? null : (tkb, at, comps) =>
        {
            var c = creation();
            if (c is null) return 0;
            int netId = c.AllocateNetworkId();
            var components = new List<object>(comps.Count + 1);
            for (int i = 0; i < comps.Count; i++) components.Add(comps[i]);
            components.Add(at);
            c.Publish(new Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest
            {
                RequestId             = Guid.NewGuid(),
                OwnerAppInstanceId    = c.NodeId,
                TkbType               = (Fdp.Core.TkbTypeId)(tkb),
                InitialComponents     = components,
                InitType              = Fdp.Toolkit.Replication.ReliableInitType.AllPeers,   // RequestEntityCreation's default
                PreAllocatedNetworkId = netId,
            });
            return netId;
        };
}
