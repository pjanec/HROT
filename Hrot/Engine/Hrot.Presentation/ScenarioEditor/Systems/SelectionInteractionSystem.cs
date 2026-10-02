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
// ⚠ Aliased, not imported: Fdp.Toolkit.Vis2D.Abstractions also declares MapKeyboardKey, which
//   would make the Delete-key check ambiguous with the gizmo interaction one already in use.
using SelectionChangeRequest = Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest;

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

    /// ⭐⭐ UXI-11 S-1 -- the component writes live in ONE place now.
    /// ⛔ This system used to carry its own SetSelected/ClearAllSelections; EcsSelectionState carries
    ///   the same two and is the view every host reads through, so keeping both would be two
    ///   implementations of "what selected looks like on the component" (ruling 9).
    /// ⭐ Constructing our own is correct, not a silent default: the view is a read-through HANDLE
    ///   with no store behind it, so an instance made here and one made by the host cannot disagree.
    private readonly Hrot.ScenarioEditor.Selection.EcsSelectionState _selection;

    /// <summary>
    /// ⭐⭐ Isolates the BUTTON from <see cref="MapMouseButton"/>'s modifier masks. ⚠ The enum is
    /// <c>[Flags]</c> and packs <c>ShiftMask</c>/<c>CtrlMask</c>/<c>AltMask</c> into bits 28-30, so
    /// <c>Button == MapMouseButton.Right</c> would be FALSE for a shift-right-click — ⛔ a bug that
    /// would appear only once someone held a modifier, which is the worst kind to ship.
    /// </summary>
    private const MapMouseButton ButtonMask = (MapMouseButton)0xFF;

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
        RubberBandState? rubberBandState = null,
        // ⭐⭐⭐ UXI-11 — the PACK's view. ⚠ Optional only for direct test construction; ⛔ in
        //   production MapInteractionPack always passes the one it built, so this system and the
        //   host write through the same object rather than two handles over one world.
        Hrot.ScenarioEditor.Selection.EcsSelectionState? selection = null)
    {
        _world           = world          ?? throw new ArgumentNullException(nameof(world));
        _interactionBus  = interactionBus ?? throw new ArgumentNullException(nameof(interactionBus));
        _rubberBandState = rubberBandState;
        _selection       = selection ?? new Hrot.ScenarioEditor.Selection.EcsSelectionState(_world);
    }

    public void Tick(float dt)
    {
        // Selection from gizmo entity clicks / rubber-band start.
        foreach (ref readonly var evt in _interactionBus.Read<GizmoInteractionStartedEvent>())
        {
            // ⭐⭐⭐ §6.7 (DESIGN_Gizmo_Anchor_Identity.md) — the token carries the anchor's NETWORK id;
            //   this system holds the world, so this is where it becomes a handle.
            //   ⛔ It used to read `evt.Token.Target` — an ECS handle the terminal had forwarded as a
            //     token payload so that no lookup was needed here. See GizmoPickToken.cs for why that
            //     payload is gone.
            //   ⚠ Entity.Null still means "empty space" and still starts a rubber band: an AnchorId of
            //     0 or -1 (the canvas sentinel) resolves to nothing, which is the same signal.
            var entity = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(
                _world, evt.Token.AnchorId);

            // ⭐⭐⭐ UXI-11 S-4b — §2.3's rows are BUTTON-SPECIFIC, so this is where the two gestures
            //   part company. ⛔ Until the terminal tagged the button, a right-release and a left-press
            //   arrived as the same event and every press took the left branch below.
            bool isRight = (evt.Button & ButtonMask) == MapMouseButton.Right;

            if (entity.IsNull)
            {
                if (isRight)
                {
                    // ⭐⭐ §2.3 row 3 — right-click on empty space CLEARS. ⛔ It must not start a rubber
                    //   band: a band is a left-drag gesture, and arming one on a right-release would
                    //   leave _isBoxSelecting set with no matching commit.
                    Request(SelectionChangeRequest.ClearAll("Map.RightClick.EmptySpace"));
                    continue;
                }

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
                // ⭐⭐⭐ UXI-11 S-4b — §2.3 row 1: a right-click INSIDE the selection leaves it alone.
                // 🔒 Ruled 2026-08-12, and it is load-bearing rather than cosmetic: the 2026-09-10
                //    fan-out ruling ("a menu opened on a selection affects all selected") is impossible
                //    if the gesture that opens the menu destroys the multi-selection.
                // ⛔ NOT applied to a left-press, and that asymmetry is the whole reason the button had
                //    to be carried: a left-click on a member of a five-selection must still narrow it to
                //    that one. §2.3 exempts the right-click only.
                // ⚠ Right-click on an entity OUTSIDE the selection still replaces — it deselects the
                //   others, exactly as a left-click would. Only the inside case is exempt.
                if (isRight && _selection.IsSelected(entity))
                {
                    OnSelectionChanged?.Invoke(entity, evt.WorldPos);
                    continue;
                }

                // ⭐⭐⭐ UXI-11 S-4 — A MAP CLICK IS A REQUEST NOW, like every other surface.
                // 🔴 This closes a defect S-3 introduced: the hand-syncs that pointed each host's
                //    IInspectorContext at a map click were deleted in favour of the NOTIFICATION —
                //    but this system wrote the component DIRECTLY and announced nothing, so the
                //    details pane stopped following map clicks on every host. Requesting fixes it
                //    for all of them at once, because the request system is what announces.
                // ⚠ The callback still fires IMMEDIATELY: it means "the operator clicked this
                //   entity", which is true now — ⛔ not "the selection is X", which is true next frame.
                // TODO(P2): read the modifier masks below for multi-select (Shift => Add, Ctrl =>
                //   toggle). MapMouseButton already carries them; nothing else is missing.
                Request(SelectionChangeRequest.ReplaceWith(entity,
                    isRight ? "Map.RightClick" : "Map.Click"));
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
                Request(SelectionChangeRequest.ClearAll("Map.DeleteKey"));
        }
    }

    /// <summary>
    /// Clears all ECS SelectionState components. Call before a world reset.
    ///
    /// <para>⭐⭐ <b>Deliberately IMMEDIATE, unlike every gesture above.</b> A world reset cannot wait a
    /// frame for a request to be served — the world it would apply to is the one being torn down.
    /// ⛔ It is also the only member of this class with no production caller: it exists for the reset
    /// path and the rails, which is why it is safe for it to bypass the request.</para>
    /// </summary>
    public void ClearAllSelections() => _selection.ClearCore();

    /// <summary>
    /// ⭐⭐⭐ Every gesture goes through here — 🔒 §2.7.3 rule 1, <c>SelectionRequestSystem</c> is the
    /// only writer. ⚠ One frame later than the direct write it replaces; §2.5 rules that structural.
    /// </summary>
    private void Request(SelectionChangeRequest request)
        => _world.Bus.PublishManaged(request);

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
            Request(SelectionChangeRequest.ClearAll("Map.EmptyClick"));
            OnSelectionChanged?.Invoke(Entity.Null, new Vector3(_boxStart.X, _boxStart.Y, 0f));
            return;
        }

        float minX = Math.Min(_boxStart.X, _boxCurrent.X);
        float maxX = Math.Max(_boxStart.X, _boxCurrent.X);
        float minY = Math.Min(_boxStart.Y, _boxCurrent.Y);
        float maxY = Math.Max(_boxStart.Y, _boxCurrent.Y);

        // ⭐⭐⭐ UXI-11 S-4 — ONE request carrying the whole set, rather than a clear plus N writes.
        // ⭐ This is what SelectionChangeMode was for: a rubber band is a single Replace, so the ring,
        //   the panels and the announcement all see the finished selection instead of N intermediate
        //   ones. ⛔ The old loop published nothing at all.
        var inBox = new List<Entity>();

        var q = _world.Query().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build();
        foreach (var e in q)
        {
            if (!_world.IsAlive(e)) continue;
            if (!_world.HasComponent<NetworkIdentity>(e)) continue;
            ref readonly var tf = ref _world.GetComponentRO<SimTransform>(e);
            float px = tf.Position.X;
            float py = tf.Position.Y;
            if (px >= minX && px <= maxX && py >= minY && py <= maxY)
                inBox.Add(e);
        }

        Request(SelectionChangeRequest.ReplaceWith(inBox, "Map.RubberBand"));
    }
}

