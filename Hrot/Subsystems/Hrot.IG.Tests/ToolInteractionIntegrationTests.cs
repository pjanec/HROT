using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.IG.Abstractions;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Tools;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication;
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
    // â”€â”€ Test constants (Â§CODE-STANDARDS Â§1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private const long  TestTkbType = 101L;
    private const float SpawnX      = 5500f;
    private const float SpawnY      = 5500f;

    // â”€â”€ CapturingDdsWriter<T> stub â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class CapturingDdsWriter<T> : IDdsWriter<T>
    {
        public List<T> Written { get; } = new List<T>();
        public void Write(T sample) => Written.Add(sample);
    }

    // â”€â”€ Repository factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Tests: DDS payload from CreationTool â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Tests: StandardInteractionTool selection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        var selection       = new DefaultSelectionState();
        var pickQuery       = repo.Query().With<SimTransform>().Build();
        var interactionTool = new StandardInteractionTool(repo, pickQuery, selection);

        interactionTool.TestHook_SelectEntity(spawnedEntity, augment: false);

        var state = repo.GetComponent<SelectionState>(spawnedEntity);
        Assert.True(state.IsSelected,
            "SelectionState.IsSelected must be true after TestHook_SelectEntity.");
        Assert.True(state.IsPrimarySelection,
            "Single-select must mark the entity as primary selection.");
    }
}
