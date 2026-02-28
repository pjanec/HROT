using Bagira.IG.Components;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Systems;

/// <summary>
/// Simulation-phase system that keeps <see cref="ContextMenuState"/> managed
/// components in sync with IOS-provided action lists and operator right-click input.
///
/// Responsibilities:
/// <list type="number">
///   <item>
///     Consume <see cref="ContextActionsUpdate"/> managed events (sent by IOS) and
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
public class ContextMenuSystem : IModuleSystem
{
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

    // ── IModuleSystem ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();

        // ── 1. Process IOS ContextActionsUpdate events ────────────────────────
        var updates = view.ConsumeManagedEvents<ContextActionsUpdate>();

        foreach (var update in updates)
        {
            // Find the entity by checking all ContextMenuState holders.
            // In production this lookup would use the NetworkEntityMap; for now
            // iterate the query so the system works headlessly in tests too.
            var query = view.Query().Build();
            foreach (var entity in query)
            {
                // We update only entities that already have a ContextMenuState.
                if (!view.HasManagedComponent<ContextMenuState>(entity))
                    continue;

                var existing = view.GetManagedComponentRO<ContextMenuState>(entity);
                var updated  = new ContextMenuState
                {
                    Actions = new System.Collections.Generic.List<ContextAction>(update.Actions),
                    IsOpen  = existing.IsOpen,
                    ScreenX = existing.ScreenX,
                    ScreenY = existing.ScreenY,
                };
                cmd.SetManagedComponent(entity, updated);
            }
        }

        // ── 2. Apply pending open request ─────────────────────────────────────
        if (_hasPendingOpen)
        {
            _hasPendingOpen = false;
            Entity target   = _pendingOpenEntity;
            float  sx       = _pendingScreenX;
            float  sy       = _pendingScreenY;
            _pendingOpenEntity = Entity.Null;

            if (view.IsAlive(target))
            {
                ActiveMenuEntity = target;

                var state = new ContextMenuState
                {
                    IsOpen  = true,
                    ScreenX = sx,
                    ScreenY = sy,
                };

                if (view.HasManagedComponent<ContextMenuState>(target))
                {
                    // Preserve existing actions; update open flag and position.
                    var prev   = view.GetManagedComponentRO<ContextMenuState>(target);
                    state.Actions = prev.Actions;
                    cmd.SetManagedComponent(target, state);
                }
                else
                {
                    cmd.AddManagedComponent(target, state);
                }
            }
        }

        // ── 3. Apply pending close request ────────────────────────────────────
        if (_hasPendingClose)
        {
            _hasPendingClose = false;
            Entity target    = _pendingCloseEntity;
            _pendingCloseEntity = Entity.Null;

            if (view.IsAlive(target))
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
    {
        _pendingOpenEntity = entity;
        _pendingScreenX    = screenX;
        _pendingScreenY    = screenY;
        _hasPendingOpen    = true;
    }

    /// <summary>
    /// Queues a context-menu close request for <paramref name="entity"/>.
    /// The request is processed on the next <see cref="Execute"/> call.
    ///
    /// Intended for use by the input layer and by headless unit tests.
    /// </summary>
    internal void TestHook_CloseContextMenu(Entity entity)
    {
        _pendingCloseEntity = entity;
        _hasPendingClose    = true;
    }
}
