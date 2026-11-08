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
/// click places a circle of <see cref="AreaShapeHandles.DefaultRadius"/> m at the clicked point. <b>Enter</b>, a click on the <b>Done</b> button (drawn beside the shape) or the menu's <i>Finish</i> commits the shape
/// (<c>onDone</c>); <b>Esc</b> or the menu's <i>Cancel</i> cancels (<c>onCancelled</c>); <b>right-click</b> is the MENU and ends nothing. ⭐ B-17 P2 (D11): the
/// convention is <see cref="ShapeEditSession"/>'s — one point (the centre) is the shape's "points", the centre handle is its <c>MoveAll</c>, Finish with no centre
/// placed says so and stays armed. The geometry is <see cref="AreaShapeHandles"/> (pure); this class
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

    /// <summary>⭐ B-17 P2 — THE drawing core (the shape's centre is its one point; Finish / Cancel / the menu are its convention).</summary>
    public ShapeEditSession Session { get; } = new(minPoints: 1, closed: true, noun: "a shape");

    /// <inheritdoc/>
    public string? ToolMenuJson => Session.Menu?.Json;

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
        if (start.IsShape) Session.Add(new Vector2(start.Center.X, start.Center.Y));
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
        ShapeDoneMark.Draw(draw, AreaShapeHandles.DonePosition(_shape), AreaShapeHandles.DoneHalfSize(_shape));   // the Done affordance (the one ✓)
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
        {
            var before = _shape;
            _shape = AreaShapeHandles.Drag(_shape, kind, _cursor);
            // the centre handle moves the WHOLE shape (ShapeEditSession.MoveAll); the other handles reshape it around the centre
            if (kind == AreaHandleKind.Center)
                Session.MoveAll(new Vector2(_shape.Center.X - before.Center.X, _shape.Center.Y - before.Center.Y));
        }
    }

    /// <inheritdoc/>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        var at = new Vector2(worldPos.X, worldPos.Y);
        _cursor = at;
        // ⭐ B-17 P2 — right-click is the MENU (DebugGizmoLayer opens ToolMenuJson); it ends nothing here.
        if (button != MapMouseButton.Left) return;
        if (isPressed)
        {
            if (_shape.IsShape && AreaShapeHandles.HitDone(_shape, at)) { Resolve(Session.Finish()); return; }   // the Done affordance
            if (!_shape.IsShape) { PlaceAt(at); return; }   // the first click places the centre
            _grabbed = AreaShapeHandles.HitHandle(_shape, at, AreaShapeHandles.HitRadiusFor(_shape))?.Kind;
        }
        else _grabbed = null;
    }

    /// <inheritdoc/>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (!isPressed) return;
        bool had = Session.Points.Count > 0;
        var outcome = Session.OnKey(key, isPressed);
        if (had && Session.Points.Count == 0) _shape = default;   // Backspace un-places: back to "click to place"
        Resolve(outcome);
    }

    /// <inheritdoc/>
    public void OnMenuAction(int actionId)
    {
        bool had = Session.Points.Count > 0;
        var outcome = Session.ApplyMenuAction(actionId);
        if (had && Session.Points.Count == 0) _shape = default;
        Resolve(outcome);
    }

    private void PlaceAt(Vector2 at)
    {
        _shape = AreaShapeHandles.NewAt(at);
        Session.Add(at);
    }

    // ⭐ ONE tail for every finish door (Enter, the Done button, the menu) and every cancel door (Esc, the menu).
    private void Resolve(ShapeOutcome outcome)
    {
        if (outcome == ShapeOutcome.Finished)
        {
            _onRemove();   // remove FIRST, then complete (see FdpLocationPickerGizmo)
            _onDone(_shape);
        }
        else if (outcome == ShapeOutcome.Cancelled)
        {
            _onRemove();
            _onCancelled?.Invoke();
        }
        // TooFew: nothing placed yet — the reason is on the session; the gizmo stays armed.
    }

    /// <inheritdoc/>
    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCommit(Vector3 worldPos) { }
    /// <inheritdoc/>
    public void OnCancel() { }
    /// <inheritdoc/>
    public void Dispose() { }
}
