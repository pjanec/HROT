using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Abstractions;
using Bagira.IG.Adapters;
using Bagira.IG.Components;
using Bagira.IG.Tools;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using Raylib_cs;

namespace Bagira.IG.Tests;

/// <summary>
/// Integration tests verifying the canvas interaction flow after TASK-IF006.
///
/// <para><b>Changes from prior revision:</b> <see cref="CreationTool"/> now writes
/// a <see cref="CreateEntityRequest"/> over DDS rather than publishing a
/// <c>SpawnEntityCommand</c> onto the local <see cref="FdpEventBus"/>. Therefore
/// spawn-verification tests now assert the DDS payload, and pick/select tests
/// create entities directly in the repository (simulating what the SimHost + ghost
/// translator would do) instead of relying on the local spawning path.</para>
/// </summary>
public class ToolInteractionIntegrationTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const long  TestTkbType = 101L;
    private const float SpawnX      = 5500f;
    private const float SpawnY      = 5500f;

    // ── CapturingDdsWriter<T> stub ────────────────────────────────────────────

    private sealed class CapturingDdsWriter<T> : IDdsWriter<T>
    {
        public List<T> Written { get; } = new List<T>();
        public void Write(T sample) => Written.Add(sample);
    }

    // ── Repository factory ────────────────────────────────────────────────────

    private static EntityRepository BuildRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<NetworkOwnership>();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<CullingState>();
        repo.RegisterComponent<ResolvedStyle>();
        repo.RegisterComponent<SelectionState>();

        repo.RegisterEvent<ConstructionOrder>();
        repo.RegisterEvent<DestructionOrder>();

        return repo;
    }

    /// <summary>Creates a live, active entity with a <see cref="SimTransform"/> at <paramref name="pos"/>.</summary>
    private static Entity SpawnDirect(EntityRepository repo, Vector2 pos)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new SimTransform
        {
            Position = new System.Numerics.Vector3(pos.X, pos.Y, 0f),
            Rotation = System.Numerics.Quaternion.Identity,
        });
        repo.AddComponent(e, new CullingState());
        repo.AddComponent(e, new SelectionState());
        return e;
    }

    // ── Tests: DDS payload from CreationTool ─────────────────────────────────

    /// <summary>
    /// A left-click must write exactly one <see cref="CreateEntityRequest"/> to DDS,
    /// confirming the tool no longer triggers a local spawn via the event bus.
    /// </summary>
    [Fact]
    public void CreationTool_LeftClick_WritesDdsCreateEntityRequest()
    {
        var writer = new CapturingDdsWriter<CreateEntityRequest>();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);

        Assert.Single(writer.Written);
    }

    /// <summary>
    /// The DDS payload must include both a <c>dtEntityMaster</c> descriptor (with
    /// the requested TKB type) and a <c>dtGeoSpatial</c> descriptor (with coordinates
    /// derived from the click position), satisfying the SimHost contract.
    /// </summary>
    [Fact]
    public void CreationTool_LeftClick_RequestContainsMasterAndGeoSpatialDescriptors()
    {
        var writer = new CapturingDdsWriter<CreateEntityRequest>();
        var tool   = new CreationTool(writer, tkbType: TestTkbType);

        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);

        var req         = writer.Written[0];
        var descriptors = req.InitialDescriptors;

        var master = descriptors.First(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(TestTkbType, master.EntityMaster.TkbType);

        var geo = descriptors.First(d => d._d == EDescriptorType.dtGeoSpatial);
        Assert.Equal(SpawnY, geo.GeoSpatial.Pos.Latitude,  precision: 2);
        Assert.Equal(SpawnX, geo.GeoSpatial.Pos.Longitude, precision: 2);
    }

    // ── Tests: pick after direct spawn ───────────────────────────────────────

    /// <summary>
    /// After an entity is present in the ECS (as would happen once the SimHost creates
    /// it and the ghost translator replicates it), <see cref="EntityRenderLayer.PickEntity"/>
    /// at the spawn position must resolve it.
    /// </summary>
    [Fact]
    public void CreationTool_SpawnAndTag_EntityPickableByRenderLayer()
    {
        var repo          = BuildRepo();
        var spawnedEntity = SpawnDirect(repo, new Vector2(SpawnX, SpawnY));

        // Tag the entity as visible (simulating MapCullingSystem).
        repo.SetComponent(spawnedEntity, new CullingState
        {
            IsVisible = true,
            LodLevel  = CullingStateConstants.LodFull,
        });

        var adapter   = new SstVisualizerAdapter();
        var selection = new DefaultSelectionState();
        var pickQuery = repo.Query().With<SimTransform>().Build();
        var layer     = new EntityRenderLayer("Entities", 0, repo, pickQuery, adapter, selection);

        var picked = layer.PickEntity(new Vector2(SpawnX, SpawnY));

        Assert.NotNull(picked);
        Assert.Equal(spawnedEntity, picked!.Value);
    }

    // ── Tests: StandardInteractionTool selection ──────────────────────────────

    /// <summary>
    /// Invoking <see cref="StandardInteractionTool.TestHook_SelectEntity"/> on a live
    /// entity must set <see cref="SelectionState.IsSelected"/> and
    /// <see cref="SelectionState.IsPrimarySelection"/> to <c>true</c>.
    /// </summary>
    [Fact]
    public void StandardInteractionTool_SelectEntity_SetsEcsSelectionStateTrue()
    {
        var repo          = BuildRepo();
        var spawnedEntity = SpawnDirect(repo, new Vector2(SpawnX, SpawnY));

        repo.SetComponent(spawnedEntity, new CullingState
        {
            IsVisible = true,
            LodLevel  = CullingStateConstants.LodFull,
        });

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
}
