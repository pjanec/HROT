using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// Stateful gizmo for collecting a sequence of 2D world points (a path or trajectory).
/// Replaces the deleted <c>PointSequenceTool</c>.
///
/// Workflow:
/// <list type="number">
///   <item>Caller constructs the gizmo and registers it with <c>GlobalGizmoManager</c>.</item>
///   <item>Left-click (release) appends a world point to the sequence.</item>
///   <item>Right-click (press) calls <c>onFinish</c> with the collected points, then
///         calls <c>onRemove</c>.</item>
///   <item>ESC calls <c>onRemove</c> only — no finish callback (cancels the session).</item>
/// </list>
/// </summary>
public sealed class PointSequenceGizmo : IEntityStatefulGizmo
{
    private readonly Action<Vector2[]> _onFinish;
    private readonly Action            _onRemove;
    private readonly List<Vector2>     _points = new();
    private Vector3                    _currentPos;

    // Raylib Color.Blue = R:0, G:121, B:241. SkyBlue = R:102, G:191, B:255.
    private static readonly Rgba32 Blue    = new Rgba32(0,   121, 241, 255);
    private static readonly Rgba32 SkyBlue = new Rgba32(102, 191, 255, 255);

    private const float PointRadius  = 4.0f;
    private const float CursorRadius = 5.0f;
    private const float LineWidth    = 2.0f;
    private const float ElasticWidth = 1.0f;

    /// <inheritdoc/>
    public bool RequiresExclusiveFocus => true;

    /// <inheritdoc/>
    public bool IsFocused { get; private set; }

    /// <inheritdoc/>
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <param name="onFinish">
    /// Invoked with the collected point array when the operator right-clicks to commit
    /// the sequence.  Called before <paramref name="onRemove"/>.
    /// </param>
    /// <param name="onRemove">
    /// Invoked when the gizmo wants to exit — after <paramref name="onFinish"/> on a
    /// right-click commit, or directly on ESC (cancel, no finish callback).
    /// Typically calls <c>GlobalGizmoManager.Unregister</c>.
    /// </param>
    public PointSequenceGizmo(Action<Vector2[]> onFinish, Action onRemove)
    {
        _onFinish = onFinish ?? throw new ArgumentNullException(nameof(onFinish));
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
    }

    // ---- IEntityStatefulGizmo — draw ----------------------------------------

    /// <inheritdoc/>
    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        // Draw captured points and connecting lines.
        if (_points.Count > 0)
        {
            // Lines connecting points.
            for (int i = 0; i < _points.Count - 1; i++)
            {
                draw.DrawLine(
                    new Vector3(_points[i].X,     _points[i].Y,     0f),
                    new Vector3(_points[i + 1].X, _points[i + 1].Y, 0f),
                    Blue, LineWidth);
            }

            // Sphere at each collected point.
            foreach (var p in _points)
            {
                draw.DrawSphere(new Vector3(p.X, p.Y, 0f), PointRadius, Blue);
            }

            // Elastic line from last point to current cursor.
            draw.DrawLine(
                new Vector3(_points[^1].X,  _points[^1].Y,  0f),
                _currentPos,
                SkyBlue, ElasticWidth);
        }

        // Cursor indicator.
        draw.DrawSphere(_currentPos, CursorRadius, Blue);
    }

    // ---- IEntityStatefulGizmo — interaction ---------------------------------

    /// <inheritdoc/>
    public void OnDragUpdate(Vector3 worldPos)
    {
        _currentPos = worldPos;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left released: append <c>(worldPos.X, worldPos.Y)</c> to the point list.
    /// Right pressed: call <c>onFinish</c> with the collected points, then <c>onRemove</c>.
    /// </remarks>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && !isPressed)
        {
            _points.Add(new Vector2(worldPos.X, worldPos.Y));
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onFinish(_points.ToArray());
            _onRemove();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ESC pressed: call <c>onRemove</c> only — the accumulated points are discarded.
    /// </remarks>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (key == MapKeyboardKey.Escape && isPressed)
            _onRemove();
    }

    // ---- Unused IGizmoInteractionHandler methods -----------------------------

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
