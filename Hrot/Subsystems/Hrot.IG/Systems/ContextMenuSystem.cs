using System;
using System.Collections.Generic;
using Hrot.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.Systems;

/// <summary>
/// Simulation-phase system that keeps <see cref="ContextMenuState"/> managed
/// components in sync with ExCon-provided action lists and operator right-click input.
///
/// Responsibilities:
/// <list type="number">
///   <item>
///     Consume <see cref="ContextActionsUpdate"/> managed events (sent by ExCon) and
///     update the <see cref="ContextMenuState.Actions"/> list on the matching entity.
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
///     Opening a menu for entity A does <em>not</em> lock other interactions —
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
    // ── Cache-miss fallback writer (optional) ────────────────────────────────
    // Injected after construction via SetCacheMissWriter. Null in tests and offline mode.

    private Action<Guid, int, IReadOnlyList<int>>? _contextMenuRequestWriter;
    private int                             _mapId;

    // ── Internal pending-request state ────────────────────────────────────────
    // Queued by input code (or test hooks) before Execute runs.

    private Entity _pendingOpenEntity     = Entity.Null;
    private float  _pendingScreenX;
    private float  _pendingScreenY;
    private bool   _hasPendingOpen;

    private Entity _pendingCloseEntity    = Entity.Null;
    private bool   _hasPendingClose;

    // ── Public observable state ───────────────────────────────────────────────

    /// <summary>
    /// The entity whose context menu is currently open, or
    /// <see cref="Entity.Null"/> when no menu is active.
    /// </summary>
    public Entity ActiveMenuEntity { get; private set; } = Entity.Null;

    /// <summary>
    /// Incremented whenever a context menu is opened. Used by the UI layer to
    /// detect a fresh open request without re-opening the popup every frame.
    /// </summary>
    public int OpenSequence { get; private set; }

    // ── IEcsModuleSystem ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();

        // ── 1. Process ExCon ContextActionsUpdate events ────────────────────────
        var updates = view.ConsumeManagedEvents<ContextActionsUpdate>();

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
                    Actions = new List<ContextAction>(update.Actions),
                    IsOpen  = false,
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
            }
        }

        // ── 2. Apply pending close request ────────────────────────────────────
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

        // ── 3. Apply pending open request ─────────────────────────────────────
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
                    // Clone the list so we never mutate the previous tick's shared reference.
                    var prev   = view.GetManagedComponentRO<ContextMenuState>(target);
                    state.Actions = new List<ContextAction>(prev.Actions);
                    cmd.SetManagedComponent(target, state);
                }
                else
                {
                    state.Actions = new List<ContextAction>();
                    cmd.AddManagedComponent(target, state);
                }

                // ── Cache-miss fallback: if the entity has no ExCon-provided actions cached,
                // emit a ContextMenuRequest so the ExCon can push back a ContextActionsUpdate.
                // This handles the right-click-without-prior-selection scenario.
                // Skip for the map-background entity (NetworkIdentity = 0) and when
                // the writer is unavailable (offline / test mode).
                if (_contextMenuRequestWriter != null && view.HasComponent<NetworkIdentity>(target))
                {
                    ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(target);
                    bool hasIosActions = state.Actions.Exists(
                        a => !a.ActionName.StartsWith("IG_", StringComparison.Ordinal));

                    if (netId.Value != 0 && !hasIosActions)
                    {
                        _contextMenuRequestWriter?.Invoke(
                            Guid.NewGuid(), _mapId, new List<int> { (int)netId.Value });
                    }
                }

                // Only inject the spatial "Center on Entity" default if the target
                // entity actually has a position in the world.  The _mapContextEntity
                // (NetworkIdentity = 0, no SimTransform) is intentionally excluded.
                if (view.HasComponent<SimTransform>(target))
                {
                    bool hasCenter = state.Actions.Exists(
                        a => a.ActionName == "IG_CenterOnEntity" || a.ActionName == "IG_Center");

                    if (!hasCenter)
                    {
                        state.Actions.Insert(0, new ContextAction
                        {
                            Label      = "Center on Entity",
                            ActionName = "IG_CenterOnEntity"
                        });
                    }
                }
            }
        }
    }

    // ── DDS writer wiring ─────────────────────────────────────────────────────

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

    // ── Test / input hooks ────────────────────────────────────────────────────

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
                Actions = prev.Actions,
                IsOpen  = false,
                ScreenX = prev.ScreenX,
                ScreenY = prev.ScreenY,
            };
            cmd.SetManagedComponent(target, state);
        }
    }
}
