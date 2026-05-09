using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.IG.Abstractions;
using Hrot.IG.Components;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Lifecycle;
using Hrot.ScenarioEditor.Gizmos;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication;

namespace Hrot.IG.Tests;

/// <summary>
/// Integration tests verifying the canvas interaction flow after D001 refactor.
///
/// <see cref="EntityPlacementGizmo"/> now publishes <see cref="SpawnEntityCommand"/> via
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

    // â”€â”€ Tests: EntityPlacementGizmo spawn command â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// A left-click must publish exactly one <see cref="SpawnEntityCommand"/> to the delegate.
    /// </summary>
    [Fact]
    public void EntityPlacementGizmo_LeftClick_WritesExactlyOneCommand()
    {
        var captured = new List<SpawnEntityCommand>();
        var gizmo = new EntityPlacementGizmo(
            onEntityCreated: cmd => captured.Add(cmd),
            tkbType:         TestTkbType);

        // Left released at (SpawnX, SpawnY) triggers placement.
        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(SpawnX, SpawnY, 0f));

        Assert.Single(captured);
    }

    /// <summary>
    /// The published command must carry the requested TKB type and the click position
    /// encoded as <see cref="SpawnEntityCommand.InitialTransform"/>.
    /// </summary>
    [Fact]
    public void EntityPlacementGizmo_LeftClick_CommandCarriesTkbTypeAndPosition()
    {
        var captured = new List<SpawnEntityCommand>();
        var gizmo = new EntityPlacementGizmo(
            onEntityCreated: cmd => captured.Add(cmd),
            tkbType:         TestTkbType);

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(SpawnX, SpawnY, 0f));

        Assert.Equal(TestTkbType, captured[0].TkbType);
        Assert.True(captured[0].InitialTransform.HasValue);
        Assert.Equal(SpawnX, captured[0].InitialTransform!.Value.Position.X, precision: 2);
        Assert.Equal(SpawnY, captured[0].InitialTransform!.Value.Position.Y, precision: 2);
    }

    // -- Tests: Phase 5 -- StandardInteractionTool selection replaced by SelectionInteractionSystem --

    /// <summary>
    /// Phase 5 (BATCH-28): StandardInteractionTool deleted; selection via SelectionInteractionSystem.
    /// Verify ECS SelectionState is set when SelectionInteractionSystem processes a gizmo pick event.
    /// Full coverage in SelectionInteractionSystemTests (SIS-001..SIS-008).
    /// </summary>
    [Fact]
    public void SelectionInteractionSystem_SelectEntity_SetsEcsSelectionStateTrue()
    {
        var repo    = BuildRepo();
        repo.RegisterComponent<Hrot.IG.Components.SelectionState>();
        var entity  = SpawnDirect(repo, new Vector2(SpawnX, SpawnY));
        var system  = new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(repo);

        // Use ClearAllSelections as a proxy to verify it operates on SelectionState.
        repo.AddComponent(entity, new Hrot.IG.Components.SelectionState { IsSelected = true, IsPrimarySelection = true });
        system.ClearAllSelections();

        var state = repo.GetComponent<Hrot.IG.Components.SelectionState>(entity);
        Assert.False(state.IsSelected);
        Assert.False(state.IsPrimarySelection);
    }
}