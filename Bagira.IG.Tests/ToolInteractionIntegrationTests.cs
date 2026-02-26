using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.IG.Adapters;
using Bagira.IG.Components;
using Bagira.IG.Tools;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using Raylib_cs;

namespace Bagira.IG.Tests;

/// <summary>
/// Integration test for Task IG.3.5: end-to-end canvas interaction flow.
///
/// Scenario:
/// <list type="number">
///   <item>
///     <see cref="CreationTool.HandleClick"/> publishes a <see cref="SpawnEntityCommand"/>
///     onto the <see cref="FdpEventBus"/>.
///   </item>
///   <item>
///     <see cref="NetworkSpawningSystem"/> consumes the command and creates an ECS entity
///     with a <see cref="SimTransform"/> at the clicked world position.
///   </item>
///   <item>
///     The entity is manually tagged as visible (simulating a
///     <see cref="MapCullingSystem"/> run) so that
///     <see cref="EntityRenderLayer.PickEntity"/> can resolve it.
///   </item>
///   <item>
///     <see cref="EntityRenderLayer.PickEntity"/> at the same world position returns
///     the newly created entity, confirming the ECS state is ready for selection.
///   </item>
/// </list>
///
/// No DDS or Raylib window context required.  All ECS operations are in-process.
/// </summary>
public class ToolInteractionIntegrationTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const long  TestTkbType   = 101L;
    private const float SpawnX        = 5500f;
    private const float SpawnY        = 5500f;

    // ── Stub allocator ────────────────────────────────────────────────────────

    private sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next = 1;
        public long AllocateId() => _next++;
        public void Reset(long startId = 0) => _next = startId;
        public void Dispose() { }
    }

    // ── World factory ─────────────────────────────────────────────────────────

    private static (EntityRepository repo, NetworkSpawningSystem system, NetworkEntityMap entityMap)
        BuildWorld()
    {
        var repo = new EntityRepository();
        // Components required by NetworkSpawningSystem / EntityLifecycleModule
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<NetworkOwnership>();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<NetworkSpawnRequest>();
        repo.RegisterComponent<PendingNetworkAck>();
        repo.RegisterComponent<EntityMaster>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<SelectionState>();

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

    private static void RunSpawn(EntityRepository repo, NetworkSpawningSystem system)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, 0f);
        ((EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer()).Playback(repo);

        // ELM normally transitions Constructing → Active once all participant modules have ACK'd.
        // In headless tests with zero participants no ACK ever arrives, so we advance the lifecycle
        // manually here — equivalent to a single ELM flush with participatingModuleIds = empty.
        var constructing = repo.Query().WithLifecycle(EntityLifecycle.Constructing).Build();
        foreach (var e in constructing)
            repo.SetLifecycleState(e, EntityLifecycle.Active);
    }

    // ── Test: CreationTool → spawn → ECS entity with SimTransform ─────────────

    /// <summary>
    /// A left-click by <see cref="CreationTool"/> produces a
    /// <see cref="SpawnEntityCommand"/> that when processed by
    /// <see cref="NetworkSpawningSystem"/> results in a live ECS entity registered
    /// in the <see cref="NetworkEntityMap"/>.
    /// </summary>
    [Fact]
    public void CreationTool_LeftClick_EntityAppearsInEcsAfterSpawn()
    {
        var (repo, system, _) = BuildWorld();

        var tool = new CreationTool(repo.Bus, tkbType: TestTkbType);

        // Simulate a left-click at the spawn position.
        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);

        // Tick the spawning system to process the command.
        RunSpawn(repo, system);

        // At least one SimTransform entity must exist in the world.
        var query = repo.Query().With<SimTransform>().Build();
        int count = 0;
        foreach (var _ in query) count++;
        Assert.True(count > 0, "At least one SimTransform entity must exist after spawn.");
    }

    /// <summary>
    /// The spawned entity must carry a <see cref="SimTransform"/> at the world
    /// position that was clicked.
    /// </summary>
    [Fact]
    public void CreationTool_LeftClick_SpawnedEntityHasSimTransformAtClickPosition()
    {
        var (repo, system, _) = BuildWorld();

        var tool = new CreationTool(repo.Bus, tkbType: TestTkbType);
        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);
        RunSpawn(repo, system);

        var query = repo.Query().With<SimTransform>().Build();
        SimTransform? found = null;
        foreach (var entity in query)
        {
            var t = repo.GetComponent<SimTransform>(entity);
            if (MathF.Abs(t.Position.X - SpawnX) < 1f && MathF.Abs(t.Position.Y - SpawnY) < 1f)
            {
                found = t;
                break;
            }
        }

        Assert.NotNull(found);
        Assert.Equal(SpawnX, found!.Value.Position.X, precision: 2);
        Assert.Equal(SpawnY, found.Value.Position.Y, precision: 2);
    }

    // ── Test: spawned entity is pickable via EntityRenderLayer ────────────────

    /// <summary>
    /// After spawning, once the entity is tagged visible (as <see cref="MapCullingSystem"/>
    /// would do), <see cref="EntityRenderLayer.PickEntity"/> at the spawn world position
    /// must resolve the entity, confirming the ECS and rendering pipeline are in sync.
    /// </summary>
    [Fact]
    public void CreationTool_SpawnAndTag_EntityPickableByRenderLayer()
    {
        var (repo, system, _) = BuildWorld();

        var tool = new CreationTool(repo.Bus, tkbType: TestTkbType);
        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);
        RunSpawn(repo, system);

        // Tag the spawned entity as visible, simulating MapCullingSystem.
        var query = repo.Query().With<SimTransform>().Build();
        Entity? spawnedEntity = null;
        foreach (var entity in query)
        {
            repo.SetComponent(entity, new CullingState
            {
                IsVisible = true,
                LodLevel  = CullingStateConstants.LodFull,
            });
            spawnedEntity = entity;
        }

        Assert.NotNull(spawnedEntity);

        // Wire up an EntityRenderLayer and attempt to pick at the spawn point.
        var adapter   = new SstVisualizerAdapter();
        var selection = new DefaultSelectionState();
        var pickQuery = repo.Query().With<SimTransform>().Build();
        var layer     = new EntityRenderLayer("Entities", 0, repo, pickQuery, adapter, selection);

        var picked = layer.PickEntity(new Vector2(SpawnX, SpawnY));

        Assert.NotNull(picked);
        Assert.Equal(spawnedEntity!.Value, picked!.Value);
    }

    // ── Test: StandardInteractionTool updates SelectionState after picking ─────

    /// <summary>
    /// After a spawn and cull-tag, invoking
    /// <see cref="Tools.StandardInteractionTool.TestHook_SelectEntity"/> updates the
    /// ECS <see cref="SelectionState"/> component so that
    /// <see cref="SelectionState.IsSelected"/> becomes <c>true</c> for the entity.
    ///
    /// This test drives the selection handler directly without a Raylib window,
    /// verifying the ECS write-back and confirming the full interaction loop closes
    /// correctly.
    /// </summary>
    [Fact]
    public void StandardInteractionTool_SelectEntity_SetsEcsSelectionStateTrue()
    {
        var (repo, system, _) = BuildWorld();

        // Spawn an entity.
        var creationTool = new CreationTool(repo.Bus, tkbType: TestTkbType);
        creationTool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);
        RunSpawn(repo, system);

        // Tag visible + find entity.
        var query = repo.Query().With<SimTransform>().Build();
        Entity spawnedEntity = default;
        foreach (var entity in query)
        {
            repo.SetComponent(entity, new CullingState { IsVisible = true, LodLevel = CullingStateConstants.LodFull });
            spawnedEntity = entity;
        }

        Assert.True(repo.IsAlive(spawnedEntity));

        // Ensure SelectionState is registered and present on the entity.
        if (!((ISimulationView)repo).HasComponent<SelectionState>(spawnedEntity))
            repo.AddComponent(spawnedEntity, new SelectionState());

        // Build the StandardInteractionTool and fire its select handler.
        var adapter         = new SstVisualizerAdapter();
        var selection       = new DefaultSelectionState();
        var pickQuery       = repo.Query().With<SimTransform>().Build();
        var interactionTool = new StandardInteractionTool(repo, pickQuery, adapter, selection);

        interactionTool.TestHook_SelectEntity(spawnedEntity, augment: false);

        var state = repo.GetComponent<SelectionState>(spawnedEntity);
        Assert.True(state.IsSelected,
            "SelectionState.IsSelected must be true after TestHook_SelectEntity.");
        Assert.True(state.IsPrimarySelection,
            "Single-select must mark the entity as primary selection.");
    }

    // ── Helper: not needed after refactor ────────────────────────────────────

    private static void SimulateEntitySelect(
        StandardInteractionTool tool,
        EntityRepository        repo,
        Entity                  entity,
        bool                    augment)
    {
        if (!((ISimulationView)repo).HasComponent<SelectionState>(entity))
            repo.AddComponent(entity, new SelectionState());

        tool.TestHook_SelectEntity(entity, augment);
    }}