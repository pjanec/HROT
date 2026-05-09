using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ECS system that translates gizmo interaction events into <see cref="SelectionState"/>
/// component mutations.
///
/// Consumes (non-destructive read -- safe to share with DataDrivenGizmoSystem):
///   <see cref="GizmoInteractionStartedEvent"/> -- entity click: select / deselect
///   <see cref="GizmoKeyEvent"/> -- Delete key: destroy all selected entities
///
/// Replaces the selection logic formerly in
/// <c>Hrot.ScenarioEditor.Tools.StandardInteractionTool</c> (Phase 5 eradication).
/// </summary>
public sealed class SelectionInteractionSystem
{
    private readonly EntityRepository _world;

    /// <summary>
    /// Optional callback fired after selection changes. Subscribe to publish network
    /// selection-change events (e.g. SelectionChangedEventDto) without coupling
    /// this system to network infrastructure.
    /// Receives (selectedEntity, worldPos). selectedEntity == Entity.Null means
    /// empty-space click (deselect all).
    /// </summary>
    public Action<Entity, System.Numerics.Vector3>? OnSelectionChanged;

    public SelectionInteractionSystem(EntityRepository world)
    {
        _world = world;
    }

    public void Tick(float dt)
    {
        // Selection from gizmo entity clicks.
        foreach (ref readonly var evt in _world.Bus.Read<GizmoInteractionStartedEvent>())
        {
            var entity = evt.Token.Target;

            if (entity.IsNull)
            {
                // Click on empty space: deselect all.
                ClearAllSelections();
                OnSelectionChanged?.Invoke(Entity.Null, evt.WorldPos);
            }
            else if (_world.IsAlive(entity))
            {
                // TODO(P2): read Raylib shift/ctrl state for multi-select.
                // Phase 5 implements single-select only.
                ClearAllSelections();
                SetSelected(entity, isPrimary: true);
                OnSelectionChanged?.Invoke(entity, evt.WorldPos);
            }
        }

        // Delete key: destroy all currently selected entities.
        foreach (ref readonly var key in _world.Bus.Read<GizmoKeyEvent>())
        {
            if (key.Key != MapKeyboardKey.Delete || key.IsPressed) continue;

            var toDestroy = new List<Entity>();
            var q = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
            foreach (var e in q)
            {
                if (!_world.IsAlive(e)) continue;
                var s = _world.GetComponent<SelectionState>(e);
                if (!s.IsSelected && !s.IsPrimarySelection) continue;
                toDestroy.Add(e);
            }

            foreach (var e in toDestroy)
            {
                if (!_world.IsAlive(e)) continue;
                if (_world.HasComponent<NetworkIdentity>(e))
                {
                    ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(e);
                    _world.Bus.PublishManaged(new DestroyEntityCommand
                    {
                        NetworkId = netId.Value,
                        Reason    = "user-deleted",
                    });
                }
                else
                {
                    _world.DestroyEntity(e);
                }
            }

            if (toDestroy.Count > 0)
                ClearAllSelections();
        }
    }

    /// <summary>
    /// Clears all ECS SelectionState components. Call before a world reset.
    /// </summary>
    public void ClearAllSelections()
    {
        var q = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
        foreach (var e in q)
        {
            if (_world.IsAlive(e))
                _world.SetComponent(e, new SelectionState { IsSelected = false, IsPrimarySelection = false });
        }
    }

    private void SetSelected(Entity entity, bool isPrimary)
    {
        if (!_world.HasComponent<SelectionState>(entity))
            _world.AddComponent(entity, new SelectionState());
        _world.SetComponent(entity, new SelectionState
        {
            IsSelected         = true,
            IsPrimarySelection = isPrimary,
        });
    }
}
