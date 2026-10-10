using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
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
    private readonly List<float>       _heights = new();   // ⭐ CE-1033 M22 — each point's level height (0 = the ground)
    private Vector3                    _currentPos;
    private Fdp.Toolkit.World.IWorldQuery? _world;

    /// <summary>
    /// ⭐ CE-1033 S3 (docs/DESIGN_Map_3D_Mode.md M22 step 1) — the height of the level the sequence is drawn on: the FIRST point's
    /// surface height when it was picked on a roof or an upper floor (a 3-D click, <see cref="Fdp.Toolkit.World.Levels"/> ≥ 1),
    /// else 0 = on the ground, which is DRAPED (M18) so a ground area follows relief instead of floating at one height (R-248).
    /// A 2-D click has no height, so in 2-D this is always 0 — byte-identical to before.
    /// </summary>
    public float AreaHeight => _heights.Count > 0 ? _heights[0] : 0f;

    // Raylib Color.Blue = R:0, G:121, B:241. SkyBlue = R:102, G:191, B:255.
    private static readonly Rgba32 Blue    = new Rgba32(0,   121, 241, 255);
    private static readonly Rgba32 SkyBlue = new Rgba32(102, 191, 255, 255);

    private const float PointRadius  = 4.0f;
    private const float CursorRadius = 5.0f;
    private const float LineWidth    = 2.0f;
    private const float ElasticWidth = 1.0f;

    /// <inheritdoc/>
    public bool RequiresExclusiveFocus => true;

    /// <summary>
    /// ⭐⭐⭐ <b>TRUE, and it is not optional for an exclusive-focus gizmo.</b> 📄
    /// <c>docs/designs/gizmos-1/gizmo-input-focus-design.md</c> §5.1 · <c>§6.2c</c> · <c>CE-259k</c>.
    ///
    /// <para>🔴 <b>The defect this closes, measured <c>2026-09-09</c> by running the editor:</b> this gizmo
    /// implements its ENTIRE behaviour in <see cref="OnMouseEvent"/> / <see cref="OnKeyEvent"/> — the RAW
    /// handlers — while <c>OnInteractionStarted</c> / <c>OnCommit</c> are empty stubs. With
    /// <c>WantsRawInput</c> left at its <see langword="false"/> default the terminal is told the opposite,
    /// and the gizmo is DEAF ON BOTH PATHS: <c>DebugGizmoLayer.cs:126</c> withholds raw events, and
    /// <c>:428</c> suppresses spatial hit-testing for everything not anchored to the capture token because
    /// <c>RequiresExclusiveFocus</c> is set. ⇒ it DRAWS and ignores every click, and Escape too.</para>
    ///
    /// <para>🔒 <b>Design basis:</b> §5.1 defines <c>InputCaptureBinding</c> as <i>"a meta-primitive
    /// declaring that the bound token wants raw hardware events streamed to it"</i>, with ONE flag —
    /// <c>ConditionMask: 1 = Exclusive, 0 = Shared</c>. ⛔ There is no <c>wantsRawInput</c> bit in the
    /// design at all; emitting the binding WAS the request. The second bit is an as-built divergence, and
    /// reconciling it is <c>CE-259l</c> — until then every exclusive gizmo must say this explicitly.</para>
    /// </summary>
    public bool WantsRawInput => true;

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
    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        _world = Fdp.Toolkit.World.WorldQuery.Of(view);
        // Draw captured points and connecting lines.
        if (_points.Count > 0)
        {
            // Lines connecting points.
            for (int i = 0; i < _points.Count - 1; i++)
            {
                draw.DrawLine(
                    new Vector3(_points[i].X,     _points[i].Y,     _heights[i]),
                    new Vector3(_points[i + 1].X, _points[i + 1].Y, _heights[i + 1]),
                    Blue, LineWidth);
            }

            // Sphere at each collected point.
            for (int i = 0; i < _points.Count; i++)
            {
                draw.DrawSphere(new Vector3(_points[i].X, _points[i].Y, _heights[i]), PointRadius, Blue);
            }

            // Elastic line from last point to current cursor.
            draw.DrawLine(
                new Vector3(_points[^1].X,  _points[^1].Y,  _heights[^1]),
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
            // ⭐ CE-1033 M22 — a point picked on a roof keeps the roof's height; one on the ground keeps 0 (draped).
            bool onLevel = worldPos.Z != 0f && _world != null && Fdp.Toolkit.World.Levels.LevelOf(_world, worldPos) >= 1;
            _heights.Add(onLevel ? worldPos.Z : 0f);
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
