using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.SimHost.Translators;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using FDP.Framework.Runner;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// In Phase 1 the CGF acts only as a heartbeating <see cref="CgfApplication.ClusterSlave"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem
{
    private CgfApplication? _app;
    private NetworkEntityMap? _entityMap;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.08f, 0.22f, 0.38f, 1f);

    /// <summary>TestHook: exposes the ghost entity map for integration tests.</summary>
    internal NetworkEntityMap? GhostEntityMap => _entityMap;

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {
        _app = new CgfApplication(config.DomainId, nodeId: config.NodeId != 0 ? config.NodeId : 400);

        // ── Brain-role pack installation (PACK2-R002) ─────────────────────────
        var doctrineRegistry = new DoctrineRegistry();
        _entityMap           = new NetworkEntityMap();
        var geoTransform     = HrotEnvironment.CreateGeoTransform();
        var ghostCreation    = new GhostCreationSystem(_entityMap);

        _app.Install(new CgfLogicPack(doctrineRegistry, _entityMap));
        _app.Install(new EntityStatesIngressPack(
            PackRole.Ingress,
            _app.Participant,
            _entityMap,
            _app.EventBus,
            ghostCreation,
            geoTransform));
        _app.Install(new ActuatorIntentsEgressPack(
            PackRole.Egress,
            _app.Participant,
            _entityMap,
            geoTransform,
            _app.EventBus));
    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        _app?.Tick();
    }

    /// <inheritdoc/>
    public void DrawWorld() { }

    /// <inheritdoc/>
    public void DrawUI() { }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _app?.Dispose();
        _app = null;
    }
}
