using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Systems;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="SelectionInteractionSystem"/> (SIS-001..SIS-008).
/// </summary>
public class SelectionInteractionSystemTests
{
    private readonly EntityRepository _world;
    private readonly SelectionInteractionSystem _system;

    public SelectionInteractionSystemTests()
    {
        _world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_world);
        _world.RegisterComponent<SelectionState>();
        _world.RegisterComponent<VehicleState>();
        _system = new SelectionInteractionSystem(_world, _world.Bus);
    }

    private Entity CreateSelectableEntity()
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, default(SimTransform));
        _world.AddComponent(e, new NetworkIdentity { Value = 1L });
        _world.AddComponent(e, new SelectionState());
        return e;
    }

    private void PublishStartedEvent(Entity target, Vector3 worldPos = default)
    {
        _world.Bus.Publish(new GizmoInteractionStartedEvent
        {
            Token    = new PickToken { Target = target },
            WorldPos = worldPos,
        });
        _world.Bus.SwapBuffers();
    }

    private void PublishKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        _world.Bus.Publish(new GizmoKeyEvent
        {
            Key       = key,
            IsPressed = isPressed,
        });
        _world.Bus.SwapBuffers();
    }

    // SIS-001: GizmoInteractionStartedEvent with valid entity selects it.
    [Fact]
    public void GizmoInteractionStartedEvent_WithValidEntity_SelectsIt()
    {
        var entity = CreateSelectableEntity();
        PublishStartedEvent(entity);

        _system.Tick(0f);

        var state = _world.GetComponent<SelectionState>(entity);
        Assert.True(state.IsSelected);
        Assert.True(state.IsPrimarySelection);
    }

    // SIS-002: After null-entity GizmoInteractionStartedEvent, selection is NOT cleared immediately.
    // A tiny-drag commit (GizmoInteractionCommitEvent without intervening GizmoDragUpdateEvent) clears all.
    [Fact]
    public void GizmoInteractionStartedEvent_WithNullEntity_StartsRubberBand_NotImmediateClear()
    {
        var entity = CreateSelectableEntity();
        _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        // Step 1: null entity click -> rubber-band starts, selection NOT yet cleared
        PublishStartedEvent(Entity.Null);
        _system.Tick(0f);
        // Still selected (rubber-band in progress, no commit yet)
        Assert.True(_world.GetComponent<SelectionState>(entity).IsSelected);

        // Step 2: commit without any drag event -> tiny drag path -> clears selection
        _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = new PickToken { Target = Entity.Null } });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);

        var state = _world.GetComponent<SelectionState>(entity);
        Assert.False(state.IsSelected);
    }

    // SIS-003: Second click clears previous selection (single-select).
    [Fact]
    public void SecondClick_ClearsPreviousSelection()
    {
        var entity1 = CreateSelectableEntity();
        var entity2 = _world.CreateEntity();
        _world.AddComponent(entity2, default(SimTransform));
        _world.AddComponent(entity2, new NetworkIdentity { Value = 2L });
        _world.AddComponent(entity2, new SelectionState());

        PublishStartedEvent(entity1);
        _system.Tick(0f);
        Assert.True(_world.GetComponent<SelectionState>(entity1).IsSelected);

        PublishStartedEvent(entity2);
        _system.Tick(0f);

        Assert.False(_world.GetComponent<SelectionState>(entity1).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(entity2).IsSelected);
    }

    // SIS-004: GizmoKeyEvent(Delete, isPressed=false) on selected entity publishes DestroyEntityCommand.
    [Fact]
    public void GizmoKeyEvent_Delete_Released_OnSelectedEntity_PublishesDestroyCommand()
    {
        var entity = CreateSelectableEntity();
        _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        PublishKeyEvent(MapKeyboardKey.Delete, isPressed: false);
        _system.Tick(0f);
        _world.Bus.SwapBuffers(); // make commands published during Tick readable

        var commands = new List<DestroyEntityCommand>();
        foreach (var cmd in _world.Bus.ReadManaged<DestroyEntityCommand>())
            commands.Add(cmd);
        Assert.Single(commands);
        Assert.Equal(1L, commands[0].NetworkId);
    }

    // SIS-005: GizmoKeyEvent(Delete, isPressed=true) is ignored.
    [Fact]
    public void GizmoKeyEvent_Delete_Pressed_IsIgnored()
    {
        var entity = CreateSelectableEntity();
        _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        PublishKeyEvent(MapKeyboardKey.Delete, isPressed: true);
        _system.Tick(0f);

        // Entity should still be alive and selected.
        Assert.True(_world.IsAlive(entity));
        Assert.True(_world.GetComponent<SelectionState>(entity).IsSelected);
    }

    // SIS-006: ClearAllSelections() deselects all live entities.
    [Fact]
    public void ClearAllSelections_DeselectsAllLiveEntities()
    {
        var entity1 = CreateSelectableEntity();
        var entity2 = CreateSelectableEntity();
        _world.SetComponent(entity1, new SelectionState { IsSelected = true, IsPrimarySelection = true });
        _world.SetComponent(entity2, new SelectionState { IsSelected = true, IsPrimarySelection = false });

        _system.ClearAllSelections();

        Assert.False(_world.GetComponent<SelectionState>(entity1).IsSelected);
        Assert.False(_world.GetComponent<SelectionState>(entity2).IsSelected);
    }

    // SIS-007: OnSelectionChanged callback fires on entity click.
    [Fact]
    public void OnSelectionChanged_FiresOnEntityClick()
    {
        var entity = CreateSelectableEntity();
        Entity? callbackEntity = null;
        _system.OnSelectionChanged += (e, _) => callbackEntity = e;

        PublishStartedEvent(entity);
        _system.Tick(0f);

        Assert.Equal(entity, callbackEntity);
    }

    // SIS-008: OnSelectionChanged fires with Entity.Null on tiny-drag commit (empty-space rubber-band commit).
    [Fact]
    public void OnSelectionChanged_FiresWithNull_AfterTinyDragCommit()
    {
        Entity? callbackEntity = null;
        _system.OnSelectionChanged += (e, _) => callbackEntity = e;

        // Start rubber-band on empty space (null entity)
        PublishStartedEvent(Entity.Null);
        _system.Tick(0f);
        Assert.Null(callbackEntity); // not yet fired

        // Commit without drag = tiny drag = deselect all
        _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = new PickToken { Target = Entity.Null } });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);

        Assert.Equal(Entity.Null, callbackEntity);
    }
}
