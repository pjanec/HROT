using System;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// ⭐⭐ CE-4206 R2 (DESIGN_Map_Picking_Unification §6.3 F6) — edits an <see cref="AreaLink"/> SHAPE on the map. A circle: drag the centre, drag the radius handle on the
/// rim. A rectangle: drag the centre, the two edge handles (width, length) and the heading knob beyond the front edge. With no start shape the first left
/// click places a circle of <see cref="AreaShapeHandles.DefaultRadius"/> m at the clicked point. <b>Enter</b> or a click on the <b>Done</b> button (drawn beside the shape) commits the shape
/// (<c>onDone</c>); <b>right-click</b> or <b>Esc</b> cancels (<c>onCancelled</c>) — exactly as its sibling <see cref="FdpLocationPickerGizmo"/> cancels. The geometry is <see cref="AreaShapeHandles"/> (pure); this class
/// is the input state machine and the drawing. Armed through the pick arbiter like <see cref="FdpLocationPickerGizmo"/>.
/// </summary>
public sealed class AreaShapeGizmo : IEntityStatefulGizmo
{
    private readonly Action<AreaLink> _onDone;
    private readonly Action _onRemove;
    private readonly Action? _onCancelled;

    private AreaLink _shape;
    private AreaHandleKind? _grabbed;
    private Vector2 _cursor;

    /// <summary>The shape being edited now (a rail reads it).</summary>
    public AreaLink Shape => _shape;

    /// <summary>The handle held by the mouse, if any.</summary>
    public AreaHandleKind? Grabbed => _grabbed;

    /// <param name="start">The shape to edit; anything that is not a circle / rectangle starts the gizmo in "place" mode (first click = centre).</param>
    /// <param name="onDone">Fired with the edited shape on Enter / right-click, after <paramref name="onRemove"/>.</param>
    /// <param name="onRemove">Ends the interaction (unregisters the gizmo).</param>
    /// <param name="onCancelled">Fired on Esc, after <paramref name="onRemove"/>.</param>
    public AreaShapeGizmo(AreaLink start, Action<AreaLink> onDone, Action onRemove, Action? onCancelled = null)
    {
        _shape = start;
        _onDone = onDone ?? throw new ArgumentNullException(nameof(onDone));
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
        _onCancelled = onCancelled;
    }

    /// <inheritdoc/>
    public bool RequiresExclusiveFocus => true;

    /// <summary>TRUE, as for every exclusive-focus gizmo that implements its behaviour in the raw handlers (see <see cref="FdpLocationPickerGizmo.WantsRawInput"/>).</summary>
    public bool WantsRawInput => true;

    /// <inheritdoc/>
    public bool IsFocused { get; private set; }

    /// <inheritdoc/>
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <inheritdoc/>
    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        var outline = new Rgba32(102, 191, 255, 255);
        var handle = new Rgba32(255, 210, 60, 255);
        var hot = new Rgba32(255, 120, 60, 255);

        if (!_shape.IsShape)
        {
            // place mode: a faint preview of the default circle under the cursor
            DrawPolyline(draw, AreaShapeHandles.Outline(AreaShapeHandles.NewAt(_cursor)), new Rgba32(102, 191, 255, 120));
            return;
        }

        DrawPolyline(draw, AreaShapeHandles.Outline(_shape), outline);
        // the Done affordance: a small green square with a tick, beside the shape
        var done = AreaShapeHandles.DonePosition(_shape);
        var green = new Rgba32(90, 220, 110, 255);
        float d = AreaShapeHandles.DoneHalfSize(_shape);
        draw.DrawLine(new Vector3(done.X - d, done.Y - d, 0f), new Vector3(done.X + d, done.Y - d, 0f), green, 2f);
        draw.DrawLine(new Vector3(done.X + d, done.Y - d, 0f), new Vector3(done.X + d, done.Y + d, 0f), green, 2f);
        draw.DrawLine(new Vector3(done.X + d, done.Y + d, 0f), new Vector3(done.X - d, done.Y + d, 0f), green, 2f);
        draw.DrawLine(new Vector3(done.X - d, done.Y + d, 0f), new Vector3(done.X - d, done.Y - d, 0f), green, 2f);
        draw.DrawLine(new Vector3(done.X - d * 0.6f, done.Y, 0f), new Vector3(done.X - d * 0.1f, done.Y - d * 0.6f, 0f), green, 2f);
        draw.DrawLine(new Vector3(done.X - d * 0.1f, done.Y - d * 0.6f, 0f), new Vector3(done.X + d * 0.6f, done.Y + d * 0.6f, 0f), green, 2f);
        var handles = AreaShapeHandles.HandlesOf(_shape);
        float r = MathF.Max(0.6f, AreaShapeHandles.HitRadiusFor(_shape) * 0.35f);
        foreach (var h in handles)
        {
            bool active = _grabbed == h.Kind;
            draw.DrawSphere(new Vector3(h.Position, 0f), r, active ? hot : handle);
            if (h.Kind == AreaHandleKind.Heading)   // the knob hangs off the front edge by a stem
            {
                var front = AreaShapeHandles.HandlesOf(_shape);
                foreach (var f in front)
                    if (f.Kind == AreaHandleKind.Length)
                        draw.DrawLine(new Vector3(f.Position, 0f), new Vector3(h.Position, 0f), handle, 1.5f);
            }
        }
    }

    private static void DrawPolyline(IDebugDrawBuilder draw, System.Collections.Generic.IReadOnlyList<Vector2> pts, Rgba32 colour)
    {
        for (int i = 0; i < pts.Count; i++)
            draw.DrawLine(new Vector3(pts[i], 0f), new Vector3(pts[(i + 1) % pts.Count], 0f), colour, 2f);
    }

    /// <inheritdoc/>
    public void OnDragUpdate(Vector3 worldPos)
    {
        _cursor = new Vector2(worldPos.X, worldPos.Y);
        if (_grabbed is { } kind && _shape.IsShape)
            _shape = AreaShapeHandles.Drag(_shape, kind, _cursor);
    }

    /// <inheritdoc/>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        var at = new Vector2(worldPos.X, worldPos.Y);
        _cursor = at;
        if (button == MapMouseButton.Left)
        {
            if (isPressed)
            {
                if (_shape.IsShape && AreaShapeHandles.HitDone(_shape, at)) { Finish(); return; }   // the Done affordance
                if (!_shape.IsShape) { _shape = AreaShapeHandles.NewAt(at); return; }   // the first click places the centre
                _grabbed = AreaShapeHandles.HitHandle(_shape, at, AreaShapeHandles.HitRadiusFor(_shape))?.Kind;
            }
            else _grabbed = null;
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onRemove();   // right-click CANCELS, as FdpLocationPickerGizmo does
            _onCancelled?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (!isPressed) return;
        if (key == MapKeyboardKey.Enter) Finish();
        else if (key == MapKeyboardKey.Escape)
        {
            _onRemove();
            _onCancelled?.Invoke();
        }
    }

    private void Finish()
    {
        if (!_shape.IsShape) { _onRemove(); _onCancelled?.Invoke(); return; }   // nothing was placed
        _onRemove();   // remove FIRST, then complete (see FdpLocationPickerGizmo)
        _onDone(_shape);
    }

    /// <inheritdoc/>
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCommit(Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCancel() { }
    /// <inheritdoc/>
    public void OnMenuAction(int actionId) { }
    /// <inheritdoc/>
    public void Dispose() { }
}
