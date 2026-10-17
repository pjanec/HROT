using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Traffic;
using Hrot.Common.Infrastructure;
using Hrot.Traffic;
using Hrot.TrafficNode;
using Xunit;

namespace Hrot.NodeComposition.Tests;

/// <summary>
/// ⭐ AQ93 §12 N1 (R-4054) — the dedicated traffic node boots on the shared bootstrap with role <see cref="NodeRole.Traffic"/> and
/// composes the serving traffic pack: the mirror in the input phase and the stepping module on the kernel.
/// </summary>
public sealed class TrafficNodeBootstrapperTests
{
    private static TrafficNodeBootstrapper Boot()
    {
        var bootstrapper = new TrafficNodeBootstrapper();
        bootstrapper.BootstrapNode(new HrotNodeConfig
        {
            Headless             = true,
            SkipAllocatorRouting = true,
            SubsystemName        = "Traffic",
            NodeId               = 807,
            LocalTempRoot        = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-traffic-node-tests"),
        }, TrafficNodeBootstrapper.Role, new Hrot.Editor.OfflineNetworkFactory());
        return bootstrapper;
    }

    [Fact]
    public void TheTrafficNodeComposesTheServingPack_OnTheSharedBootstrap_AQ93_N1()
    {
        using var node = Boot();

        Assert.Equal(NodeRole.Traffic, TrafficNodeBootstrapper.Role);
        Assert.Contains(node.Capabilities, c => c.Key == CapabilityKeys.Traffic);
        Assert.True(node.Traffic.Serves);
        Assert.NotNull(node.Traffic.Runtime);

        // The mirror runs in the node's (replay-togglable) input group; the stepping module sits on the node's kernel.
        var systems = node.Context.Kernel.SystemScheduler.GetAllSystems().ToList();
        var input = systems.OfType<Fdp.ModuleHost.Scheduling.TogglableInputGroup>().Single();
        Assert.Contains(input.GetSystems(), s => ReferenceEquals(s, node.Traffic.Mirror));
        Assert.Contains(node.Context.Kernel.GetModuleDiagnostics(),
            m => m.ModuleTypeName == nameof(TrafficSteppingModule));

        // The scenario's traffic settings can arrive here (S1): the component is registered, and ghost promotion is scheduled.
        Assert.True(node.Context.World.IsComponentTypeRegistered<TrafficDemand>());
        Assert.Contains(systems, s => ReferenceEquals(s, node.EntityCreation!.PromotionSystem));
    }

    /// <summary>
    /// ⭐ AQ93 N1/S3 — on a networked node the NED replication module composes for the Traffic role, so the scenario's traffic
    /// settings arrive through the shared pack's <c>EntityTrafficDemandIngressTranslator</c> (no traffic-only translator).
    /// </summary>
    [Fact]
    public void TheNetworkedTrafficNodeIngestsTheTrafficDemand_ThroughTheSharedPack_AQ93_N1()
    {
        const int domain = 187, nodeId = 807;
        var participant = Hrot.Map.Common.HrotEnvironment.CreateParticipant(domain);
        participant.EnableSenderTracking(new CycloneDDS.Runtime.Tracking.SenderIdentityConfig { AppDomainId = domain, AppInstanceId = nodeId });
        var factory = new Hrot.Network.NED.Factory.NedNetworkFactory(
            participant, new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
            Hrot.Map.Common.HrotEnvironment.CreateGeoTransform(), new Fdp.Core.FdpEventBus(), nodeId, NodeRole.None);

        using var node = new TrafficNodeBootstrapper();
        node.BootstrapNode(new HrotNodeConfig
        {
            DomainId             = domain,
            NodeId               = nodeId,
            SkipAllocatorRouting = true,
            ExternalParticipant  = participant,
            SubsystemName        = "Traffic",
            LocalTempRoot        = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-traffic-node-tests"),
        }, TrafficNodeBootstrapper.Role, factory);

        Assert.NotNull(node.Context.NedReplication);
        var ingress = node.Context.Kernel.SystemScheduler.GetAllSystems()
            .OfType<Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem>()
            .SelectMany(s => s.Translators);
        Assert.Contains(ingress, t => t is Hrot.Map.Common.Replication.Ingress.EntityTrafficDemandIngressTranslator);
    }

    [Fact]
    public void TheTrafficNodeTicksIdle_WithNoNet_AQ93_N1()
    {
        using var node = Boot();
        for (int i = 0; i < 5; i++) node.Tick();
        Assert.Empty(node.Traffic.Mirror!.Cars);
    }
}
