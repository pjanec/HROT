using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.IG.Abstractions;
using Hrot.IG.Adapters;
using Hrot.IG.Components;
using Hrot.IG.Tools;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using Raylib_cs;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests verifying the canvas interaction flow after D001 refactor.
///
/// <see cref="CreationTool"/> now publishes <see cref="SpawnEntityCommand"/> via
/// a delegate rather than building a <see cref="Hrot.NED.Messages.CreateEntityRequest"/>.
/// Pick/select tests create entities directly in the repository to simulate
/// what the SimHost + ghost translator would do.
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
    /// A left-click must publish exactly one <see cref="SpawnEntityCommand"/> to the delegate.
    /// </summary>
    [Fact]
    public void CreationTool_LeftClick_WritesDdsCreateEntityRequest()
    {
        var captured = new List<SpawnEntityCommand>();
        var tool     = new CreationTool(cmd => captured.Add(cmd), tkbType: TestTkbType);

        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);

        Assert.Single(captured);
    }

    /// <summary>
    /// The published command must carry the requested TKB type and the click position
    /// encoded as <see cref="SpawnEntityCommand.InitialTransform"/>.
    /// </summary>
    [Fact]
    public void CreationTool_LeftClick_RequestContainsMasterAndGeoSpatialDescriptors()
    {
        var captured = new List<SpawnEntityCommand>();
        var tool     = new CreationTool(cmd => captured.Add(cmd), tkbType: TestTkbType);

        tool.HandleClick(new Vector2(SpawnX, SpawnY), MouseButton.Left);

        var cmd = captured[0];
        Assert.Equal(TestTkbType, cmd.TkbType);
        Assert.True(cmd.InitialTransform.HasValue);
        Assert.Equal(SpawnX, cmd.InitialTransform!.Value.Position.X, precision: 2);
        Assert.Equal(SpawnY, cmd.InitialTransform!.Value.Position.Y, precision: 2);
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

        var adapter   = new NedVisualizerAdapter();
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

        var adapter         = new NedVisualizerAdapter();
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
