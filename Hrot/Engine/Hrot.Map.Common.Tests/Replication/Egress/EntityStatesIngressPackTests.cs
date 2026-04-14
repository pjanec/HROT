using System.Collections.Generic;
using Hrot.Map.Common.Translators;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.Map.Common.Tests;

/// <summary>
/// Smoke tests for <see cref="EntityStatesIngressPack"/> (PACK2-P002).
/// </summary>
public class EntityStatesIngressPackTests
{
    // ── Stub ISystemRegistry ──────────────────────────────────────────────────

    private sealed class CapturingRegistry : ISystemRegistry
    {
        private readonly List<IEcsModuleSystem> _systems = new();
        public IReadOnlyList<IEcsModuleSystem> RegisteredSystems => _systems;

        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => _systems.Add(system);
    }

    // ── Stub IGeographicTransform ─────────────────────────────────────────────

    private sealed class NullGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }

        public System.Numerics.Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
            => System.Numerics.Vector3.Zero;

        public (double lat, double lon, double alt) ToGeodetic(System.Numerics.Vector3 localPos)
            => (0, 0, 0);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_IsEntityStatesIngress()
    {
        var entityMap          = new NetworkEntityMap();
        var eventBus           = new FdpEventBus();
        var ghostCreationSystem = new GhostCreationSystem(entityMap);
        var geoTransform       = new NullGeoTransform();

        var pack = new EntityStatesIngressPack(
            PackRole.Ingress,
            participant: null,
            entityMap, localNodeId: 1, eventBus, ghostCreationSystem, geoTransform);

        Assert.Equal("EntityStatesIngress", pack.Name);
    }

    [Fact]
    public void RegisterSystems_DoesNotThrow_AndRegistersOneCycloneNetworkIngressSystem()
    {
        var entityMap          = new NetworkEntityMap();
        var eventBus           = new FdpEventBus();
        var ghostCreationSystem = new GhostCreationSystem(entityMap);
        var geoTransform       = new NullGeoTransform();
        var registry           = new CapturingRegistry();

        var pack = new EntityStatesIngressPack(
            PackRole.Ingress,
            participant: null,
            entityMap, localNodeId: 1, eventBus, ghostCreationSystem, geoTransform);

        var ex = Record.Exception(() => pack.RegisterSystems(registry));

        Assert.Null(ex);
        Assert.Equal(1, registry.RegisteredSystems.Count);
    }
}
