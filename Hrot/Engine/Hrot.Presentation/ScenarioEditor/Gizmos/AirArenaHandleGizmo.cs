using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Vis2D.Gizmos;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ CE-4647 (AC-15 step 6, the drag) — the arena's four HANDLES on the map, armed by the Edit Shape tool on a selected <see cref="AirArena"/> (the VertexEditGizmo shape): each handle is a pick box (<c>SubElementId</c> = its <see cref="ArenaHandle"/>), a press records the
/// arena as it is, a drag changes a LOCAL preview (the component is untouched, so the preview draws beside the old ring), and the RELEASE commits it as ONE undoable edit through <see cref="AirArenaOps.SetShape"/> (a press that moved nothing is no edit).
/// Escape / cancel drops the preview. The drag maths is <see cref="AirArenaGizmo.Handles.Drag"/> (relative to the grab). ⭐ HOT PATH: no allocation in <see cref="UpdateAndDraw"/>.
/// </summary>
public sealed class AirArenaHandleGizmo : IEntityStatefulGizmo
{
    private readonly EntityRepository _repo;
    private readonly Entity _entity;
    private readonly int _networkId;
    private readonly Action _onRemove;

    private bool _dragging;
    private ArenaHandle _handle;
    private ArenaShape _start, _preview;
    private Vector2 _grab;

    public bool RequiresExclusiveFocus => true;
    public bool WantsRawInput => true;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <summary>The handle being dragged (rails).</summary>
    public ArenaHandle Active => _dragging ? _handle : ArenaHandle.None;
    /// <summary>The preview shape while dragging, else null (rails).</summary>
    public ArenaShape? Preview => _dragging ? _preview : null;

    public AirArenaHandleGizmo(ISimulationView view, Entity entity, int networkId, Action onRemove)
    {
        _repo = view as EntityRepository ?? throw new ArgumentException("AirArenaHandleGizmo requires EntityRepository access.", nameof(view));
        _entity = entity;
        _networkId = networkId;
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
    }

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        if (!AirArenaOps.Is(_repo, _entity)) return;
        var current = AirArenaOps.ShapeOf(_repo.GetComponentRO<AirArena>(_entity));
        var shape = _dragging ? _preview : current;
        float ground = Fdp.Toolkit.World.WorldQuery.Of(view)?.GroundHeightAt(shape.Centre.X, shape.Centre.Y) ?? 0f;
        if (_dragging) AirArenaGizmo.Draw(draw, in shape, ground, selected: false);   // the preview ring beside the (still drawn) old one
        if (!IsFocused) return;   // handles only while this gizmo holds the focus (§4.7i)
        for (var h = ArenaHandle.Centre; h <= ArenaHandle.Ceiling; h++)
        {
            var p = AirArenaGizmo.Handles.Position(in shape, h, ground);
            var at = new Vector2(p.X, p.Y);
            if (h == ArenaHandle.Centre) GizmoEditStyle.Centre(draw, at, _networkId, (ushort)h, _dragging && _handle == h ? HandleState.Grabbed : HandleState.Idle);
            else GizmoEditStyle.Handle(draw, at, ShapeKind.Area, _dragging && _handle == h ? HandleState.Grabbed : HandleState.Idle, _networkId, (ushort)h);
        }
    }

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
    {
        var h = (ArenaHandle)token.SubElementId;
        if (h < ArenaHandle.Centre || h > ArenaHandle.Ceiling || !AirArenaOps.Is(_repo, _entity)) return;
        _handle = h;
        _start = _preview = AirArenaOps.ShapeOf(_repo.GetComponentRO<AirArena>(_entity));
        _grab = new Vector2(worldPos.X, worldPos.Y);
        _dragging = true;
    }

    public void OnDragUpdate(Vector3 worldPos)
    {
        if (!_dragging) return;
        _preview = AirArenaGizmo.Handles.Drag(in _start, _handle, _grab, new Vector2(worldPos.X, worldPos.Y));
    }

    public void OnCommit(Vector3 worldPos)
    {
        if (!_dragging) return;
        OnDragUpdate(worldPos);
        var shape = _preview;
        _dragging = false;
        AirArenaOps.SetShape(_repo, _entity, in shape);   // ONE step; nothing moved ⇒ no step
    }

    public void OnCancel() => _dragging = false;

    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }   // the drag is the pick token's (press · drag · release); a right-click is the menu (none here)

    /// <summary>Esc drops a drag in progress and ends the tool; Enter ends it (the edit was committed on each release).</summary>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (!isPressed) return;
        if (key == MapKeyboardKey.Escape) { OnCancel(); _onRemove(); }
        else if (key == MapKeyboardKey.Enter) _onRemove();
    }

    public void OnMenuAction(int actionId) { }

    public void Dispose() { _dragging = false; }
}
