using System;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// Stateful gizmo that lets the operator click any point on the canvas to return a
/// world-space location.
///
/// <para>
/// Fires <paramref name="onPicked"/> with the raw world-space <see cref="Vector2"/> on
/// left-click release, then calls <paramref name="onRemove"/> to unregister itself.
/// Right-click or <c>Escape</c> cancels without firing <paramref name="onPicked"/>.
/// </para>
///
/// Replaces <c>LocationPickerTool</c> (gizmo migration).
/// Exercised via <see cref="Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager"/>
/// which forwards canvas events into this gizmo.
/// </summary>
public sealed class FdpLocationPickerGizmo : IEntityStatefulGizmo
{
    private const float CrosshairHalfSize  = 14f;
    private const float CrosshairThickness = 1.5f;
    private const float CrosshairGapRadius = 5f;

    private readonly Action<Vector2> _onPicked;
    private readonly Action          _onRemove;

    private Vector3 _cursorWorld;

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

    /// <param name="onPicked">
    /// Callback fired with the raw world-space position (X, Y) on left-click release.
    /// Called before <paramref name="onRemove"/>.
    /// </param>
    /// <param name="onRemove">
    /// Invoked when the gizmo wants to exit. Typically calls
    /// <c>GlobalGizmoManager.Unregister</c> to remove the gizmo from the manager.
    /// </param>
    public FdpLocationPickerGizmo(Action<Vector2> onPicked, Action onRemove)
    {
        _onPicked = onPicked ?? throw new ArgumentNullException(nameof(onPicked));
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
    }

    // ---- IEntityStatefulGizmo -- draw ----------------------------------------

    /// <inheritdoc/>
    /// <remarks>Draws a sky-blue crosshair at the current cursor world position.</remarks>
    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        // Sky-blue crosshair (Raylib Color.SkyBlue = R:102, G:191, B:255).
        var drawColor = new Rgba32(102, 191, 255, 255);
        var pos = _cursorWorld;

        draw.DrawLine(new Vector3(pos.X - CrosshairHalfSize, pos.Y, 0f), new Vector3(pos.X - CrosshairGapRadius, pos.Y, 0f), drawColor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X + CrosshairGapRadius, pos.Y, 0f), new Vector3(pos.X + CrosshairHalfSize, pos.Y, 0f), drawColor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X, pos.Y - CrosshairHalfSize, 0f), new Vector3(pos.X, pos.Y - CrosshairGapRadius, 0f), drawColor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X, pos.Y + CrosshairGapRadius, 0f), new Vector3(pos.X, pos.Y + CrosshairHalfSize, 0f), drawColor, CrosshairThickness);
        draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), CrosshairGapRadius, drawColor);
    }

    // ---- IEntityStatefulGizmo -- interaction ---------------------------------

    /// <inheritdoc/>
    public void OnDragUpdate(Vector3 worldPos)
    {
        _cursorWorld = worldPos;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left released: fire <c>onPicked</c> with the world XY position, then <c>onRemove</c>.
    /// Right pressed: cancel and call <c>onRemove</c> only.
    /// </remarks>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && !isPressed)
        {
            _onPicked(new Vector2(worldPos.X, worldPos.Y));
            _onRemove();
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onRemove();
        }
    }

    /// <inheritdoc/>
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
