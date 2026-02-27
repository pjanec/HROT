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
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.SimHost.Integration.Tests;

public sealed class EntityLifecycleIntegrationTests : IDisposable
{
    private const int DomainId = 10;
    private const float Dt = 1f / 60f;
    private const int MaxTicks = 120;
    private const long FixedNetworkId = 1001;
    private const int TickSleepMs = 12;

    private readonly SimHostApp _simHost;
    private readonly IgApplication _ig;

    public EntityLifecycleIntegrationTests()
    {
        _simHost = new SimHostApp();
        _simHost.InitializeHeadless(domainIdOverride: DomainId);

        _ig = new IgApplication();
        _ig.InitializeEmbedded(headless: true, domainIdOverride: DomainId);
    }

    public void Dispose()
    {
        _ig.Shutdown(ownsWindow: false);
        _simHost.Dispose();
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
        bool igHasMaster = false;
        for (int i = 0; i < MaxTicks; i++)
        {
            _simHost.Tick(Dt);
            _ig.Update(Dt);

            if (networkId == null && TryGetFirstNetworkId(_simHost.World, out var id))
                networkId = id;

            if (networkId.HasValue && IgHasEntity(_ig.World, networkId.Value))
                igHasEntity = true;

            if (networkId.HasValue && IgHasSimTransform(_ig.World, networkId.Value))
                igHasTransform = true;

            if (networkId.HasValue && IgHasEntityMaster(_ig.World, networkId.Value))
                igHasMaster = true;

            if (networkId.HasValue && IgHasResolvedStyle(_ig.World, networkId.Value))
                break;

            Thread.Sleep(TickSleepMs);
        }

        Assert.True(networkId.HasValue, "Expected SimHost to allocate a network ID.");
        Assert.True(
            IgHasResolvedStyle(_ig.World, networkId!.Value),
            $"Expected IG to resolve style for NetID {networkId.Value} within {MaxTicks} ticks. " +
            $"IG entity observed: {igHasEntity}. SimTransform observed: {igHasTransform}. " +
            $"EntityMaster observed: {igHasMaster}.");
    }

    private static SpawnEntityCommand BuildSpawnCommand(Vector2 position)
    {
        return new SpawnEntityCommand
        {
            NetworkId = FixedNetworkId,
            TkbType = TkbEntityTypes.Tank_M1Abrams,
            OwnerNodeId = SimHostNetworkConstants.LocalNodeId,
            InitType = ReliableInitType.None,
            InitialComponents = new List<object>
            {
                new EntityMaster { EntityId = (int)FixedNetworkId, TkbType = TkbEntityTypes.Tank_M1Abrams },
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

    private static bool IgHasEntityMaster(EntityRepository world, long networkId)
    {
        var query = world.Query().With<NetworkIdentity>().With<EntityMaster>().Build();
        foreach (var entity in query)
        {
            if (world.GetComponent<NetworkIdentity>(entity).Value == networkId)
                return true;
        }

        return false;
    }
}
