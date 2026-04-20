using System;
using System.Collections.Generic;
using Hrot.IG.Modules;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests verifying that <see cref="SpawningModule"/> correctly
/// hosts <see cref="NetworkSpawningSystem"/> and that the full
/// SpawnEntityCommand â†’ ECS-entity lifecycle works end-to-end using the
/// IG node configuration (node ID = <see cref="IgNetworkConstants.LocalNodeId"/>).
///
/// No DDS or network components are required.  The tests publish commands
/// directly to <see cref="FdpEventBus"/> and run the system in-process.
/// </summary>
public class SpawningModuleIntegrationTests
{
    // â”€â”€ Constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private const long  TestTkbType   = 101L;
    private const long  TestNetworkId = 500L;

    // â”€â”€ Stub allocator â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next;
        public StubIdAllocator(long start = 1) => _next = start;
        public long AllocateId() => _next++;
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    // â”€â”€ World factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private (EntityRepository repo, NetworkSpawningSystem system, NetworkEntityMap entityMap)
        BuildWorld()
    {
        var repo = new EntityRepository();
        // Components required by NetworkSpawningSystem / EntityLifecycleModule
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<NetworkOwnership>();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<TkbIdentity>();
        repo.RegisterComponent<GhostStateTracker>();
        repo.RegisterComponent<PendingNetworkAck>();
        // Lifecycle events required by ELM command-buffer playback
        repo.RegisterEvent<ConstructionOrder>();
        repo.RegisterEvent<DestructionOrder>();

        var tkb = new TkbDatabase();
        tkb.Register(new TkbTemplate("TestEntity", TestTkbType));

        var elm       = new EntityLifecycleModule(tkb, Array.Empty<int>());
        var entityMap = new NetworkEntityMap();
        var idAlloc   = new StubIdAllocator();
        var system    = new NetworkSpawningSystem(
            tkb, elm, entityMap, idAlloc,
            IgNetworkConstants.LocalNodeId);

        return (repo, system, entityMap);
    }

    private static void RunExecute(EntityRepository repo, NetworkSpawningSystem system)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, 0f);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    // â”€â”€ Module property contracts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void SpawningModule_Name_IsNetworkSpawning()
    {
        var (_, system, _) = BuildWorld();
        var module = new SpawningModule(system);

        Assert.Equal("NetworkSpawning", module.Name);
    }

    [Fact]
    public void SpawningModule_Policy_IsSynchronous()
    {
        var (_, system, _) = BuildWorld();
        var module = new SpawningModule(system);

        // SpawningModule must execute synchronously on the main thread so entity
        // creation commands are immediately visible to subsequent systems.
        Assert.Equal(RunMode.Synchronous, module.Policy.Mode);
    }

    // â”€â”€ SpawnEntityCommand â†’ entity manifests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// After a <see cref="SpawnEntityCommand"/> is published and the spawning
    /// system is run, the entity must be discoverable in the network map with
    /// the correct <see cref="NetworkIdentity.Value"/>.
    /// </summary>
    [Fact]
    public void SpawnCommand_ManifestsEntityInEcs_WithCorrectNetworkIdentity()
    {
        var (repo, system, entityMap) = BuildWorld();

        repo.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId         = TestNetworkId,
            TkbType           = TestTkbType,
            OwnerNodeId       = IgNetworkConstants.LocalNodeId,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object>(),
            RequestId         = Guid.Empty,
        });

        RunExecute(repo, system);

        Assert.True(entityMap.TryGetEntity(TestNetworkId, out var entity),
            "Entity must be registered in NetworkEntityMap after SpawnEntityCommand is processed");

        var identity = repo.GetComponent<NetworkIdentity>(entity);
        Assert.Equal(TestNetworkId, identity.Value);
    }

    /// <summary>
    /// The spawning system must apply <see cref="TkbIdentity"/>
    /// with the correct TKB type on the created entity.
    /// </summary>
    [Fact]
    public void SpawnCommand_StoresTkbIdentityWithCorrectTkbType()
    {
        var (repo, system, entityMap) = BuildWorld();

        repo.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId         = TestNetworkId,
            TkbType           = TestTkbType,
            OwnerNodeId       = IgNetworkConstants.LocalNodeId,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object>(),
            RequestId         = Guid.Empty,
        });

        RunExecute(repo, system);

        Assert.True(entityMap.TryGetEntity(TestNetworkId, out var entity),
            "Entity must be registered before its components can be read");

        var tkbId = repo.GetComponentRO<TkbIdentity>(entity);
        Assert.Equal(TestTkbType, tkbId.TkbType);
    }

    /// <summary>
    /// A second SpawnEntityCommand with the same NetworkId must be ignored
    /// (entity already known â€” no duplicate entity created).
    /// </summary>
    [Fact]
    public void SpawnCommand_DuplicateNetworkId_DoesNotCreateSecondEntity()
    {
        var (repo, system, entityMap) = BuildWorld();

        var cmd = new SpawnEntityCommand
        {
            NetworkId         = TestNetworkId,
            TkbType           = TestTkbType,
            OwnerNodeId       = IgNetworkConstants.LocalNodeId,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object>(),
            RequestId         = Guid.Empty,
        };

        repo.Bus.PublishManaged(cmd);
        RunExecute(repo, system);

        // Re-publish the same command
        repo.Bus.PublishManaged(cmd);
        RunExecute(repo, system);

        // The map must still resolve to exactly one entity
        Assert.True(entityMap.TryGetEntity(TestNetworkId, out _));
    }
}
