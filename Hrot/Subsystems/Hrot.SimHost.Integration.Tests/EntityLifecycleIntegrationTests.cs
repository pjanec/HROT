using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hrot.NED.Descriptors;
using Hrot.IG;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.SimHost;
using Hrot.SimHost.Configuration;
using Hrot.Network.NED.Factory;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Network.Cyclone.Services;
using Fdp.ModuleHost.Network.Interfaces;

namespace Hrot.SimHost.Integration.Tests;

[Collection("LogCapture")]
public sealed class EntityLifecycleIntegrationTests : IDisposable
{
    private const int DomainId = 10;
    private const float Dt = 1f / 60f;
    private const int MaxTicks = 300;
    private const int TickSleepMs = 12;

    private readonly SimHostApp _simHost;
    private readonly IgApplication _ig;
    private readonly DdsParticipant _idAllocatorParticipant;
    private readonly DdsIdAllocatorServer _idAllocatorServer;
    private readonly DdsParticipant _igParticipant;

    public EntityLifecycleIntegrationTests()
    {
        _idAllocatorParticipant = new DdsParticipant(DomainId);
        _idAllocatorServer = new DdsIdAllocatorServer(_idAllocatorParticipant);

        _simHost = new SimHostApp();
        var factory = new NedNetworkFactory(
            participant:  null,
            entityMap:    new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround | NodeRole.Perception);
        _simHost.InitializeHeadless(domainIdOverride: DomainId, networkFactory: factory);

        _igParticipant = new DdsParticipant(DomainId);
        var igFactory = new NedNetworkFactory(
            participant:  _igParticipant,
            entityMap:    new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.ImageGenerator);
        _ig = new IgApplication();
        _ig.InitializeEmbedded(headless: true, domainIdOverride: DomainId, networkFactory: igFactory);
    }

    public void Dispose()
    {
        _ig.Shutdown(ownsWindow: false);
        _igParticipant.Dispose();
        _simHost.Dispose();
        _idAllocatorServer.Dispose();
        _idAllocatorParticipant.Dispose();
    }

    [Fact]
    public void EndToEnd_EntityLifecycle_SpawnToStyleResolution_Headless()
    {
        var spawnPos = new Vector2(1000f, 2000f);
        var spawnCmd = BuildSpawnCommand(spawnPos);

        _simHost.World.Bus.PublishManaged(spawnCmd);

        long? networkId = null;
        bool igHasEntity = false;
        bool igHasTransform = false;
        bool igHasStyle = false;
        bool igHasNetworkPosition = false;
        bool simHostHasSimTransform = false;
        for (int i = 0; i < MaxTicks; i++)
        {
            _idAllocatorServer.ProcessRequests();
            _simHost.Tick(Dt);
            _ig.Update(Dt);

            if (networkId == null && TryGetFirstNetworkId(_simHost.World, out var id))
                networkId = id;

            if (networkId.HasValue && IgHasEntity(_ig.World, networkId.Value))
                igHasEntity = true;

            if (networkId.HasValue && IgHasSimTransform(_ig.World, networkId.Value))
                igHasTransform = true;

            if (networkId.HasValue && IgHasResolvedStyle(_ig.World, networkId.Value))
                igHasStyle = true;

            if (networkId.HasValue && IgHasNetworkPosition(_ig.World, networkId.Value))
                igHasNetworkPosition = true;

            if (networkId.HasValue && IgHasSimTransform(_simHost.World, networkId.Value))
                simHostHasSimTransform = true;

            if (networkId.HasValue && igHasStyle && igHasNetworkPosition && simHostHasSimTransform)
                break;

            Thread.Sleep(TickSleepMs);
        }

        Assert.True(networkId.HasValue, "Expected SimHost to allocate a network ID.");
        Assert.True(
            igHasStyle,
            $"Expected IG to resolve style for NetID {networkId.Value} within {MaxTicks} ticks. " +
            $"IG entity observed: {igHasEntity}. SimTransform observed: {igHasTransform}. " +
            $"ResolvedStyle observed: {igHasStyle}. NetworkPosition observed: {igHasNetworkPosition}. " +
            $"SimHost SimTransform observed: {simHostHasSimTransform}.");

        Assert.True(
            igHasNetworkPosition,
            $"Expected IG to receive NetworkPosition for NetID {networkId.Value}.");

        Assert.True(
            simHostHasSimTransform,
            $"Expected SimHost to have SimTransform for NetID {networkId.Value}.");
    }

    [Fact]
    public void DomainIsolation_Domain0Spawn_DoesNotAffectDomain10()
    {
        // Settle loop: drain any transient-local DDS data left over from tests that ran
        // earlier in the same LogCapture collection on the same DDS domain (10).
        // Without this, the baseline could include stale entities published by previous tests.
        const int SettleFrames = 30;
        for (int i = 0; i < SettleFrames; i++)
        {
            _idAllocatorServer.ProcessRequests();
            _simHost.Tick(Dt);
            _ig.Update(Dt);
            Thread.Sleep(TickSleepMs);
        }

        int baselineCount = CountIgEntities(_ig.World);

        var domain0Cfg = new NodeConfiguration
        {
            DdsDomainId = 0,
        };
        using var simHostDomain0 = new SimHostApp(0, NodeRole.MuscleGround | NodeRole.Perception, domain0Cfg);
        using var idParticipant0 = new DdsParticipant(0);
        using var idServer0 = new DdsIdAllocatorServer(idParticipant0);
        simHostDomain0.InitializeHeadless(domainIdOverride: 0);

        simHostDomain0.World.Bus.PublishManaged(BuildSpawnCommand(new Vector2(500f, 600f)));

        for (int i = 0; i < 60; i++)
        {
            _idAllocatorServer.ProcessRequests();
            idServer0.ProcessRequests();
            simHostDomain0.Tick(Dt);
            _ig.Update(Dt);
            Thread.Sleep(TickSleepMs);
        }

        int finalCount = CountIgEntities(_ig.World);

        Assert.Equal(baselineCount, finalCount);
    }

    private static SpawnEntityCommand BuildSpawnCommand(Vector2 position)
    {
        return new SpawnEntityCommand
        {
            NetworkId = 0,
            TkbType = TkbEntityTypes.Truck_HMMWV,
            DisType = 0,
            OwnerNodeId = SimHostNetworkConstants.LocalNodeId,
            InitType = ReliableInitType.AllPeers,
            InitialComponents = new List<object>
            {
                new SimTransform
                {
                    Position = new Vector3(position.X, position.Y, 0f),
                    Rotation = Quaternion.Identity,
                },
            },
            RequestId = Guid.NewGuid(),
        };
    }

    private static bool TryGetFirstNetworkId(EntityRepository world, out long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().Build();
        foreach (var entity in query)
        {
            networkId = world.GetComponent<NetworkIdentity>(entity).Value;
            return true;
        }

        networkId = 0;
        return false;
    }

    private static bool IgHasResolvedStyle(EntityRepository world, long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().With<ResolvedStyle>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
                return true;
        }

        return false;
    }

    private static bool IgHasEntity(EntityRepository world, long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
                return true;
        }

        return false;
    }

    private static bool IgHasSimTransform(EntityRepository world, long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().With<SimTransform>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
                return true;
        }

        return false;
    }


    private static bool IgHasNetworkPosition(EntityRepository world, long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().With<NetworkTransform>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
            {
                var pos = world.GetComponent<NetworkTransform>(entity).LastPosition;
                return Math.Abs(pos.X) > 0.001f || Math.Abs(pos.Y) > 0.001f;
            }
        }

        return false;
    }

    private static int CountIgEntities(EntityRepository world)
    {
        int count = 0;
        var query = world.Query().With<NetworkIdentity>().Build();
        foreach (var _ in query)
            count++;
        return count;
    }
}
