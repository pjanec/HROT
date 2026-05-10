using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.ScenarioEditor.Systems;

/// <summary>
/// ECS system that translates gizmo interaction events into <see cref="SelectionState"/>
/// component mutations.
///
/// Consumes (non-destructive read -- safe to share with DataDrivenGizmoSystem):
///   <see cref="GizmoInteractionStartedEvent"/> -- entity click: select; empty space: start rubber-band
///   <see cref="GizmoDragUpdateEvent"/>          -- rubber-band update
///   <see cref="GizmoInteractionCommitEvent"/>   -- rubber-band commit or click confirm
///   <see cref="GizmoInteractionCancelEvent"/>   -- rubber-band cancel
///   <see cref="GizmoKeyEvent"/>                 -- Delete key: destroy all selected entities
///
/// Replaces the selection logic formerly in
/// <c>Hrot.ScenarioEditor.Tools.StandardInteractionTool</c> (Phase 5 eradication).
/// </summary>
public sealed class SelectionInteractionSystem
{
    private readonly EntityRepository _world;
    private readonly FdpEventBus _interactionBus;
    private readonly RubberBandState? _rubberBandState;

    // Rubber-band selection tracking.
    private bool    _isBoxSelecting;
    private Vector2 _boxStart;
    private Vector2 _boxCurrent;

    /// <summary>
    /// Optional callback fired after selection changes. Subscribe to publish network
    /// selection-change events (e.g. SelectionChangedEventDto) without coupling
    /// this system to network infrastructure.
    /// Receives (selectedEntity, worldPos). selectedEntity == Entity.Null means
    /// empty-space click (deselect all).
    /// </summary>
    public Action<Entity, System.Numerics.Vector3>? OnSelectionChanged;

    public SelectionInteractionSystem(
        EntityRepository world,
        FdpEventBus interactionBus,
        RubberBandState? rubberBandState = null)
    {
        _world           = world          ?? throw new ArgumentNullException(nameof(world));
        _interactionBus  = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
        _rubberBandState = rubberBandState;
    }

    public void Tick(float dt)
    {
        // Selection from gizmo entity clicks / rubber-band start.
        foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionStartedEvent>())
        {
            var entity = evt.Token.Target;

            if (entity.IsNull)
            {
                // Empty-space press: begin rubber-band selection.
                _isBoxSelecting = true;
                _boxStart   = new Vector2(evt.WorldPos.X, evt.WorldPos.Y);
                _boxCurrent = _boxStart;
                if (_rubberBandState != null)
                {
                    _rubberBandState.IsActive = false;
                    _rubberBandState.Start    = _boxStart;
                    _rubberBandState.Current  = _boxStart;
                }
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

        // Rubber-band position update.
        foreach (ref readonly var evt in _interactionBus.Read<GizmoDragUpdateEvent>())
        {
            if (!_isBoxSelecting) continue;
            _boxCurrent = new Vector2(evt.WorldPos.X, evt.WorldPos.Y);
            if (_rubberBandState != null)
            {
                _rubberBandState.IsActive = true;
                _rubberBandState.Current  = _boxCurrent;
            }
        }

        // Commit: finalise rubber-band selection (or treat tiny drag as deselect).
        foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionCommitEvent>())
        {
            if (!_isBoxSelecting) continue;
            _isBoxSelecting = false;
            if (_rubberBandState != null) _rubberBandState.IsActive = false;
            ExecuteBoxSelection();
        }

        // Cancel: abort rubber-band.
        foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionCancelEvent>())
        {
            if (!_isBoxSelecting) continue;
            _isBoxSelecting = false;
            if (_rubberBandState != null) _rubberBandState.IsActive = false;
        }

        // Delete key: destroy all currently selected entities.
        foreach (ref readonly var key in _interactionBus.Read<GizmoKeyEvent>())
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

    /// <summary>
    /// Finalises a rubber-band selection. Selects all entities with
    /// <see cref="NetworkIdentity"/> whose <see cref="SimTransform"/> position lies within
    /// the drag rectangle. A drag smaller than 2 world units in both axes is treated as
    /// a click-on-empty-space (deselect all).
    /// </summary>
    private void ExecuteBoxSelection()
    {
        float dx = Math.Abs(_boxCurrent.X - _boxStart.X);
        float dy = Math.Abs(_boxCurrent.Y - _boxStart.Y);
        if (dx < 2f && dy < 2f)
        {
            // Tiny drag: treat as deselect-all click.
            ClearAllSelections();
            OnSelectionChanged?.Invoke(Entity.Null, new Vector3(_boxStart.X, _boxStart.Y, 0f));
            return;
        }

        float minX = Math.Min(_boxStart.X, _boxCurrent.X);
        float maxX = Math.Max(_boxStart.X, _boxCurrent.X);
        float minY = Math.Min(_boxStart.Y, _boxCurrent.Y);
        float maxY = Math.Max(_boxStart.Y, _boxCurrent.Y);

        ClearAllSelections();
        bool anySelected = false;

        var q = _world.Query().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build();
        foreach (var e in q)
        {
            if (!_world.IsAlive(e)) continue;
            if (!_world.HasComponent<NetworkIdentity>(e)) continue;
            ref readonly var tf = ref _world.GetComponentRO<SimTransform>(e);
            float px = tf.Position.X;
            float py = tf.Position.Y;
            if (px >= minX && px <= maxX && py >= minY && py <= maxY)
            {
                SetSelected(e, isPrimary: !anySelected);
                anySelected = true;
            }
        }
    }
}

