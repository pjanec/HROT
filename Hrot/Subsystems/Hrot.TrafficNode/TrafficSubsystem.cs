using System;
using System.IO;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Presentation.DebugApi;

namespace Hrot.TrafficNode;

/// <summary>
/// ⭐ AQ93 §12 N1 (R-4054) — <c>--mode traffic</c>: a headless ECS node with role <see cref="TrafficNodeBootstrapper.Role"/> that runs
/// SUMO for the traffic settings it is granted and tees its cars to DDS. The runner discovers it by type name ("Traffic").
/// </summary>
public sealed class TrafficSubsystem : ISubsystem, IProvidesDebugSurface, Hrot.Common.EntityCreation.IEntityCreationHost
{
    private readonly INetworkFactory _networkFactory;
    private TrafficNodeBootstrapper? _bootstrapper;

    public TrafficSubsystem(INetworkFactory networkFactory)
        => _networkFactory = networkFactory ?? throw new ArgumentNullException(nameof(networkFactory));

    public string Name => "Traffic";

    /// <summary>Amber — distinct from SimHost (red), IG (green) and ExCon (violet).</summary>
    public System.Numerics.Vector4 TitleBarColor => new(0.45f, 0.32f, 0.05f, 1f);

    /// <summary>The node's bootstrapper (null before <see cref="Initialize"/>).</summary>
    public TrafficNodeBootstrapper? Bootstrapper => _bootstrapper;

    /// <inheritdoc/>
    public Hrot.Common.EntityCreation.EntityCreation? EntityCreation => _bootstrapper?.EntityCreation;

    public void Initialize(SubsystemConfig config)
    {
        if (_bootstrapper != null) throw new InvalidOperationException("TrafficSubsystem is initialised once.");
        int nodeId = config.NodeId != 0 ? config.NodeId : 800;

        // UNIFY: identical to the HrotNodeConfig of SimHostApp.cs:365-380 and CgfSubsystem.cs:715-727 — the participant comes
        //   from the composition root (the runner made it with sender tracking, which N5 needs: receivers read the car's owner
        //   from the sample's sender), Headless=false because Headless means "no window", not "no network".
        var hrotConfig = HrotNodeConfig.ForNode(config.DomainId, nodeId, Name, _networkFactory.Participant);   // ⭐ CE-4013 D13

        _bootstrapper = new TrafficNodeBootstrapper();
        _bootstrapper.BootstrapNode(hrotConfig, TrafficNodeBootstrapper.Role, _networkFactory);
        FdpLog<TrafficSubsystem>.Info("[Traffic] node {0} booted on domain {1}, role {2}, capabilities [{3}].",
            nodeId, config.DomainId, TrafficNodeBootstrapper.Role,
            string.Join(", ", System.Linq.Enumerable.Select(_bootstrapper.Capabilities, c => c.Key)));
    }

    public void Update(float deltaTime) => _bootstrapper?.Tick();

    public void DrawWorld() { }

    public void DrawUI() { }

    public void Shutdown()
    {
        _bootstrapper?.Dispose();
        _bootstrapper = null;
    }

    /// <summary>The node's debug surface: its world, entity map, catalogue, cluster state, control bus and architecture.</summary>
    public ISubsystemDebugProvider? CreateDebugProvider()
        // ⭐⭐⭐ the ONE ECS-node debug surface (EcsNodeDebugProvider): the traffic node adds nothing of its own.
        => EcsNodeDebugProvider.For(Name, Name, () => _bootstrapper?.Context);
}
