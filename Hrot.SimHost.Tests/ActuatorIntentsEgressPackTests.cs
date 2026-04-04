using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Messages;
using Hrot.SimHost.Translators;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Tests for <see cref="ActuatorIntentsEgressPack"/> (PACK2-P002).
/// </summary>
[Collection("SimHostDds")]
public class ActuatorIntentsEgressPackTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class CapturingWriter<T> : IDdsWriter<T>
    {
        public List<T> Publishes { get; } = new();
        public void Write(T sample) => Publishes.Add(sample);
        public void DisposeInstance(T key) { }
    }

    private sealed class CapturingRegistry : ISystemRegistry
    {
        private readonly List<IEcsModuleSystem> _systems = new();
        public IReadOnlyList<IEcsModuleSystem> RegisteredSystems => _systems;

        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => _systems.Add(system);
    }

    private sealed class IdentityGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double lat, double lon, double alt) { }

        public Vector3 ToCartesian(double lat, double lon, double alt)
            => new Vector3((float)lon, (float)lat, (float)alt);

        public (double lat, double lon, double alt) ToGeodetic(Vector3 pos)
            => (pos.Y, pos.X, pos.Z);
    }

    // ── Smoke tests (DDS participant required) ────────────────────────────────

    [Fact]
    public void Name_IsActuatorIntentsEgress()
    {
        const uint domainId = 211u;

        using var participant = new DdsParticipant(domainId);
        var entityMap         = new NetworkEntityMap();
        var eventBus          = new FdpEventBus();
        var geoTransform      = new IdentityGeoTransform();

        var pack = new ActuatorIntentsEgressPack(participant, entityMap, geoTransform, eventBus);

        Assert.Equal("ActuatorIntentsEgress", pack.Name);
    }

    [Fact]
    public void RegisterSystems_RegistersOneCycloneEgressSystem()
    {
        const uint domainId = 212u;

        using var participant = new DdsParticipant(domainId);
        var entityMap         = new NetworkEntityMap();
        var eventBus          = new FdpEventBus();
        var geoTransform      = new IdentityGeoTransform();
        var registry          = new CapturingRegistry();

        var pack = new ActuatorIntentsEgressPack(participant, entityMap, geoTransform, eventBus);
        pack.RegisterSystems(registry);

        Assert.Equal(1, registry.RegisteredSystems.Count);
        Assert.IsType<CycloneEgressSystem>(registry.RegisteredSystems[0]);
    }

    // ── Translator-level behaviour tests (no DDS required) ────────────────────
    // These tests validate the translators that ActuatorIntentsEgressPack bundles,
    // using testable constructors with injected CapturingWriter stubs.

    [Fact]
    public void SpawnEntityCommand_IsConsumed_WritesCreateEntityRequest_WithMatchingRequestId()
    {
        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<CreateEntityRequest>();
        var translator = new SpawnEntityCommandEgressTranslator(writer, bus, geoTransform: null);

        var requestId = Guid.NewGuid();
        bus.PublishManaged(new SpawnEntityCommand { TkbType = 1L, RequestId = requestId });
        bus.SwapBuffers();

        translator.PollIngress(null!, null!);

        Assert.Single(writer.Publishes);
        Assert.Equal(requestId, writer.Publishes[0].RequestId);
    }

    [Fact]
    public void DestroyEntityCommand_IsConsumed_WritesDeleteEntityRequest_WithMatchingNetworkId()
    {
        var bus        = new FdpEventBus();
        var writer     = new CapturingWriter<DeleteEntityRequest>();
        var translator = new DestroyEntityCommandEgressTranslator(writer, bus);

        bus.PublishManaged(new DestroyEntityCommand { NetworkId = 55L, Reason = "test" });
        bus.SwapBuffers();

        translator.PollIngress(null!, null!);

        Assert.Equal(1, writer.Publishes.Count);
        Assert.Equal(55, writer.Publishes[0].EntityId);
    }
}
