using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Bagira.BDC.SSTD;
using Bagira.IG;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.SimHost;
using Bagira.SimHost.Configuration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic.Components;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Integration.Tests;

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

    public EntityLifecycleIntegrationTests()
    {
        _idAllocatorParticipant = new DdsParticipant(DomainId);
        _idAllocatorServer = new DdsIdAllocatorServer(_idAllocatorParticipant);

        _simHost = new SimHostApp();
        _simHost.InitializeHeadless(domainIdOverride: DomainId);

        _ig = new IgApplication();
        _ig.InitializeEmbedded(headless: true, domainIdOverride: DomainId);
    }

    public void Dispose()
    {
        _ig.Shutdown(ownsWindow: false);
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
        bool simHostHasGeoTransform = false;
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

            if (networkId.HasValue && SimHostHasGeoTransform(_simHost.World, networkId.Value))
                simHostHasGeoTransform = true;

            if (networkId.HasValue && igHasStyle && igHasNetworkPosition && simHostHasGeoTransform)
                break;

            Thread.Sleep(TickSleepMs);
        }

        Assert.True(networkId.HasValue, "Expected SimHost to allocate a network ID.");
        Assert.True(
            igHasStyle,
            $"Expected IG to resolve style for NetID {networkId.Value} within {MaxTicks} ticks. " +
            $"IG entity observed: {igHasEntity}. SimTransform observed: {igHasTransform}. " +
            $"ResolvedStyle observed: {igHasStyle}. NetworkPosition observed: {igHasNetworkPosition}. " +
            $"SimHost GeoTransform observed: {simHostHasGeoTransform}.");

        Assert.True(
            igHasNetworkPosition,
            $"Expected IG to receive NetworkPosition for NetID {networkId.Value}.");

        Assert.True(
            simHostHasGeoTransform,
            $"Expected SimHost to produce GeoTransform for NetID {networkId.Value}.");
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

        using var simHostDomain0 = new SimHostApp();
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
        var query = world.Query().With<NetworkIdentity>().With<NetworkPosition>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
            {
                var pos = world.GetComponent<NetworkPosition>(entity).Value;
                return Math.Abs(pos.X) > 0.001f || Math.Abs(pos.Y) > 0.001f;
            }
        }

        return false;
    }

    private static bool SimHostHasGeoTransform(EntityRepository world, long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().With<GeoTransform>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
                return true;
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
