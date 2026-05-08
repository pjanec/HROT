using System;
using System.Collections.Generic;
using Hrot.Common.Events;
using Hrot.IG.Components;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

namespace Hrot.IG.Systems;

/// <summary>
/// Simulation-phase system that keeps <see cref="ContextMenuState"/> managed
/// components in sync with ExCon-provided menu JSON and operator right-click input.
///
/// Responsibilities:
/// <list type="number">
///   <item>
///     Consume <see cref="ContextActionsUpdate"/> managed events (sent by ExCon) and
///     update the <see cref="ContextMenuState.MenuJson"/> string on the matching entity.
///     When the update arrives for the currently active open menu, <see cref="OpenSequence"/>
///     is incremented so the rendering layer re-schedules the adapter with fresh JSON.
///   </item>
///   <item>
///     Process any pending open/close requests queued by the input layer
///     (or by <see cref="TestHook_TriggerContextMenu"/> in headless tests).
///   </item>
/// </list>
///
/// Design notes:
/// <list type="bullet">
///   <item>
///     Opening a menu for entity A does <em>not</em> lock other interactions â€”
///     the system only modifies the <see cref="ContextMenuState"/> managed component
///     and leaves all other ECS state untouched.
///   </item>
///   <item>
///     <see cref="ActiveMenuEntity"/> exposes the currently-open entity for the
///     rendering layer and for test assertions.
///   </item>
/// </list>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public class ContextMenuSystem : IEcsModuleSystem
{
    // â”€â”€ Cache-miss fallback writer (optional) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Injected after construction via SetCacheMissWriter. Null in tests and offline mode.

    private Action<Guid, int, IReadOnlyList<int>>? _contextMenuRequestWriter;
    private int                             _mapId;

    // â”€â”€ Internal pending-request state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Queued by input code (or test hooks) before Execute runs.

    private Entity _pendingOpenEntity     = Entity.Null;
    private float  _pendingScreenX;
    private float  _pendingScreenY;
    private bool   _hasPendingOpen;

    private Entity _pendingCloseEntity    = Entity.Null;
    private bool   _hasPendingClose;

    // â”€â”€ Public observable state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// The entity whose context menu is currently open, or
    /// <see cref="Entity.Null"/> when no menu is active.
    /// </summary>
    public Entity ActiveMenuEntity { get; private set; } = Entity.Null;

    /// <summary>
    /// Incremented whenever a context menu is opened OR when the menu JSON for the
    /// currently active entity is updated by a <see cref="ContextActionsUpdate"/> event.
    /// The rendering layer tracks this value to detect when to call
    /// <c>ContextMenuAdapter.Schedule</c>.
    /// </summary>
    public int OpenSequence { get; private set; }

    // â”€â”€ IEcsModuleSystem â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();

        // â”€â”€ 1. Process ExCon ContextActionsUpdate events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var updates = view.ReadManagedEvents<ContextActionsUpdate>();

        foreach (var update in updates)
        {
            // Find the matching entity via its NetworkIdentity value.
            var query = view.Query().With<NetworkIdentity>().Build();
            foreach (var entity in query)
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                if (netId.Value != update.EntityNetworkId)
                    continue;

                var updated = new ContextMenuState
                {
                    MenuJson = update.MenuJson,
                    IsOpen   = false,
                };

                if (view.HasManagedComponent<ContextMenuState>(entity))
                {
                    var existing = view.GetManagedComponentRO<ContextMenuState>(entity);
                    updated.IsOpen  = existing.IsOpen;
                    updated.ScreenX = existing.ScreenX;
                    updated.ScreenY = existing.ScreenY;
                    cmd.SetManagedComponent(entity, updated);
                }
                else
                {
                    cmd.AddManagedComponent(entity, updated);
                }

                // If the active open menu just received its JSON, increment OpenSequence
                // so the rendering layer re-schedules the adapter with the fresh content.
                if (entity == ActiveMenuEntity && updated.IsOpen && !string.IsNullOrEmpty(update.MenuJson))
                    OpenSequence++;
            }
        }

        // â”€â”€ 2. Apply pending close request â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Close is processed BEFORE open so that a same-frame close+open sequence
        // (e.g. the UI layer dismisses the current popup and the input layer queues
        // a new open for the same entity) results in the menu being re-opened rather
        // than remaining closed.  Without this ordering, the close would undo the
        // open that was applied one step earlier in the same Execute() call.
        if (_hasPendingClose)
        {
            _hasPendingClose = false;
            Entity target    = _pendingCloseEntity;
            _pendingCloseEntity = Entity.Null;

            if (view.IsAlive(target))
            {
                CloseMenu(view, cmd, target);
            }
        }

        // â”€â”€ 3. Apply pending open request â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (_hasPendingOpen)
        {
            _hasPendingOpen = false;
            Entity target   = _pendingOpenEntity;
            float  sx       = _pendingScreenX;
            float  sy       = _pendingScreenY;
            _pendingOpenEntity = Entity.Null;

            if (view.IsAlive(target))
            {
                if (ActiveMenuEntity != Entity.Null && ActiveMenuEntity != target)
                    CloseMenu(view, cmd, ActiveMenuEntity);

                ActiveMenuEntity = target;
                OpenSequence++;

                var state = new ContextMenuState
                {
                    IsOpen  = true,
                    ScreenX = sx,
                    ScreenY = sy,
                };

                if (view.HasManagedComponent<ContextMenuState>(target))
                {
                    // Preserve existing MenuJson so the popup can be shown immediately
                    // if the entity already has a cached definition.
                    var prev = view.GetManagedComponentRO<ContextMenuState>(target);
                    state.MenuJson = prev.MenuJson;
                    cmd.SetManagedComponent(target, state);
                }
                else
                {
                    cmd.AddManagedComponent(target, state);
                }

                // â”€â”€ Cache-miss fallback: if the entity has no cached menu JSON,
                // emit a ContextMenuRequest so ExCon can push back a ContextActionsUpdate.
                // This handles the right-click-without-prior-selection scenario.
                // Skip for the map-background entity (NetworkIdentity = 0) and when
                // the writer is unavailable (offline / test mode).
                if (_contextMenuRequestWriter != null && view.HasComponent<NetworkIdentity>(target))
                {
                    ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(target);
                    bool hasJson = !string.IsNullOrEmpty(state.MenuJson);

                    if (netId.Value != 0 && !hasJson)
                    {
                        _contextMenuRequestWriter?.Invoke(
                            Guid.NewGuid(), _mapId, new List<int> { (int)netId.Value });
                    }
                }
            }
        }
    }

    // â”€â”€ DDS writer wiring â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Wires up the DDS writer used to emit <see cref="ContextMenuRequest"/> messages
    /// on a cache miss (right-click without prior selection).
    /// Called by <c>IgApplication</c> after DDS initialisation completes.
    /// Passing <c>null</c> disables the fallback (offline / test mode).
    /// </summary>
    internal void SetCacheMissWriter(Action<Guid, int, IReadOnlyList<int>>? callback, int mapId)
    {
        _contextMenuRequestWriter = callback;
        _mapId                    = mapId;
    }

    // â”€â”€ Test / input hooks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Queues a context-menu open request for <paramref name="entity"/> at screen
    /// position (<paramref name="screenX"/>, <paramref name="screenY"/>).
    /// The request is processed on the next <see cref="Execute"/> call.
    ///
    /// Intended for use by the input layer and by headless unit tests (accessible
    /// via <c>InternalsVisibleTo</c>).
    /// </summary>
    internal void TestHook_TriggerContextMenu(Entity entity, float screenX, float screenY)
        => RequestOpen(entity, screenX, screenY);

    /// <summary>
    /// Queues a context-menu close request for <paramref name="entity"/>.
    /// The request is processed on the next <see cref="Execute"/> call.
    ///
    /// Intended for use by the input layer and by headless unit tests.
    /// </summary>
    internal void TestHook_CloseContextMenu(Entity entity)
        => RequestClose(entity);

    /// <summary>
    /// Queues a context-menu open request for <paramref name="entity"/> at screen
    /// position (<paramref name="screenX"/>, <paramref name="screenY"/>).
    /// The request is processed on the next <see cref="Execute"/> call.
    /// </summary>
    public void RequestOpen(Entity entity, float screenX, float screenY)
    {
        _pendingOpenEntity = entity;
        _pendingScreenX    = screenX;
        _pendingScreenY    = screenY;
        _hasPendingOpen    = true;
    }

    /// <summary>
    /// Queues a context-menu close request for <paramref name="entity"/>.
    /// The request is processed on the next <see cref="Execute"/> call.
    /// </summary>
    public void RequestClose(Entity entity)
    {
        _pendingCloseEntity = entity;
        _hasPendingClose    = true;
    }

    private void CloseMenu(ISimulationView view, IEntityCommandBuffer cmd, Entity target)
    {
        if (ActiveMenuEntity == target)
            ActiveMenuEntity = Entity.Null;

        if (view.HasManagedComponent<ContextMenuState>(target))
        {
            var prev  = view.GetManagedComponentRO<ContextMenuState>(target);
            var state = new ContextMenuState
            {
                MenuJson = prev.MenuJson,
                IsOpen   = false,
                ScreenX  = prev.ScreenX,
                ScreenY  = prev.ScreenY,
            };
            cmd.SetManagedComponent(target, state);
        }
    }
}
