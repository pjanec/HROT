using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager whose sole responsibility is polling the <see cref="StorageGatewayModule"/>
/// every 5 seconds, scanning local and NAS directories, and broadcasting
/// <see cref="AssetInventoryUpdateEvent"/> on the event bus.
///
/// <para>Extracted from <see cref="ClusterMaster"/> so the 2PC orchestration engine has
/// zero knowledge of file systems, NAS paths, or storage gateways (SRP / CGF1-S0506).</para>
/// </summary>
public sealed class AssetInventoryProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;
    private DateTime _lastInventoryScan = DateTime.MinValue;

    public AssetInventoryProcessManager(FdpEventBus bus, StorageGatewayModule gateway, string nasBasePath)
    {
        _bus         = bus         ?? throw new ArgumentNullException(nameof(bus));
        _gateway     = gateway     ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath = nasBasePath ?? throw new ArgumentNullException(nameof(nasBasePath));
    }

    /// <summary>
    /// Scans the storage gateway every 5 seconds and publishes
    /// <see cref="AssetInventoryUpdateEvent"/> when the interval elapses.
    /// Call once per frame from the Update loop.
    /// </summary>
    public void Tick()
    {
        if ((DateTime.UtcNow - _lastInventoryScan).TotalSeconds >= 5.0)
        {
            var localScenarios    = _gateway.ScanLocalScenarios(_nasBasePath);
            var localExercises    = _gateway.ScanLocalExercises(_nasBasePath);
            var archivedExercises = _gateway.ScanNasExercises(_nasBasePath);
            var unarchived        = localExercises.Except(archivedExercises).ToArray();

            _bus.PublishManaged(new AssetInventoryUpdateEvent
            {
                LocalScenarios           = localScenarios.ToArray(),
                LocalExercises           = localExercises.ToArray(),
                ArchivedExercises        = archivedExercises.ToArray(),
                UnarchivedLocalExercises = unarchived,
            });
            _lastInventoryScan = DateTime.UtcNow;
        }
    }
}
