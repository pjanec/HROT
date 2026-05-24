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
