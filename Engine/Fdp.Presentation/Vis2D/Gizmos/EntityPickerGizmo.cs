using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// Stateful gizmo that lets the operator click to pick a single entity from the
/// canvas, filtered by domain-specific criteria supplied by an
/// <see cref="IEntityFilter"/>.
///
/// <para><b>Workflow:</b>
/// <list type="number">
///   <item>Caller constructs the gizmo and registers it with <c>GlobalGizmoManager</c>.</item>
///   <item>Operator hovers; a target crosshair follows the cursor.
///         The crosshair turns red when over a pickable entity.</item>
///   <item>Left-click release on a valid entity fires <c>onPicked</c> then
///         <c>onRemove</c>.</item>
///   <item>Right-click or <c>Escape</c> fires <c>onCancelled</c> then
///         <c>onRemove</c> without a result.</item>
/// </list>
/// </para>
///
/// <para><b>No allocations on the 60 FPS hot path.</b>
/// The <see cref="IEntityFilter"/> is compiled by the caller.
/// <see cref="OnDragUpdate"/> and <see cref="UpdateAndDraw"/> are entirely
/// allocation-free.</para>
///
/// Replaces <c>EntityPickerTool</c> (gizmo migration).
/// </summary>
public sealed class EntityPickerGizmo : IEntityStatefulGizmo
{
    private const float CrosshairHalfSize  = 12f;
    private const float CrosshairThickness = 1.5f;
    private const float CrosshairGapRadius = 4f;

    private readonly Func<Vector2, Entity>              _hitTest;
    private readonly Fdp.Toolkit.Vis2D.Abstractions.IEntityFilter _filter;
    private readonly Action<Entity>        _onPicked;
    private readonly Action                _onCancelled;
    private readonly Action                _onRemove;

    private Vector2 _mouseWorldPos;
    private Entity  _hoveredEntity = Entity.Null;
    private bool    _hoveredValid;

    /// <inheritdoc/>
    public bool RequiresExclusiveFocus => true;

    /// <inheritdoc/>
    public bool IsFocused { get; private set; }

    /// <inheritdoc/>
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <param name="hitTest">
    /// Delegate that maps a world-space 2D position to the topmost entity at that
    /// position, or <see cref="Entity.Null"/> if no entity is found.
    /// </param>
    /// <param name="filter">Domain filter compiled by the caller; determines which entities are pickable.</param>
    /// <param name="onPicked">Callback fired with the picked entity on a valid left-click release.</param>
    /// <param name="onCancelled">Callback fired on right-click or Escape (cancel, no entity picked).</param>
    /// <param name="onRemove">
    /// Invoked when the gizmo wants to exit. Typically calls
    /// <c>GlobalGizmoManager.Unregister</c>.
    /// </param>
    public EntityPickerGizmo(
        Func<Vector2, Entity> hitTest,
        Fdp.Toolkit.Vis2D.Abstractions.IEntityFilter filter,
        Action<Entity>        onPicked,
        Action                onCancelled,
        Action                onRemove)
    {
        _hitTest     = hitTest     ?? throw new ArgumentNullException(nameof(hitTest));
        _filter      = filter      ?? throw new ArgumentNullException(nameof(filter));
        _onPicked    = onPicked    ?? throw new ArgumentNullException(nameof(onPicked));
        _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));
        _onRemove    = onRemove    ?? throw new ArgumentNullException(nameof(onRemove));
    }

    // ---- IEntityStatefulGizmo -- draw ----------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Draws a target crosshair at the mouse cursor.
    /// <list type="bullet">
    ///   <item>Red <c>(255, 0, 0)</c> when the cursor is over a filter-passing entity.</item>
    ///   <item>Amber <c>(255, 161, 0)</c> otherwise (waiting for a valid pick target).</item>
    /// </list>
    /// All draw calls are allocation-free.
    /// </remarks>
    public void UpdateAndDraw(float deltaTime, IDebugDrawBuilder draw)
    {
        // Amber = waiting for pick; Red = valid target under cursor.
        Rgba32 drawColor = _hoveredValid
            ? new Rgba32(255, 0,   0,   255)   // red   -- hovering a valid pick target
            : new Rgba32(255, 161, 0,   255);  // amber -- waiting for operator to hover

        TestHook_LastDrawColor = drawColor;

        var pos = _mouseWorldPos;

        // Horizontal arm: left segment + right segment (gap in centre).
        draw.DrawLine(new Vector3(pos.X - CrosshairHalfSize, pos.Y, 0f), new Vector3(pos.X - CrosshairGapRadius, pos.Y, 0f), drawColor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X + CrosshairGapRadius, pos.Y, 0f), new Vector3(pos.X + CrosshairHalfSize, pos.Y, 0f), drawColor, CrosshairThickness);

        // Vertical arm: top segment + bottom segment.
        draw.DrawLine(new Vector3(pos.X, pos.Y - CrosshairHalfSize, 0f), new Vector3(pos.X, pos.Y - CrosshairGapRadius, 0f), drawColor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X, pos.Y + CrosshairGapRadius, 0f), new Vector3(pos.X, pos.Y + CrosshairHalfSize, 0f), drawColor, CrosshairThickness);

        // Circle outline around the gap.
        draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), CrosshairGapRadius, drawColor);
    }

    // ---- IEntityStatefulGizmo -- interaction ---------------------------------

    /// <inheritdoc/>
    public void OnDragUpdate(Vector3 worldPos)
    {
        _mouseWorldPos = new Vector2(worldPos.X, worldPos.Y);
        _hoveredEntity = _hitTest(_mouseWorldPos);
        _hoveredValid  = !_hoveredEntity.IsNull && _filter.IsMatch(_hoveredEntity);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left released on valid target: fire <c>onPicked</c> then <c>onRemove</c>.
    /// Left released with no valid target: do nothing (wait for a valid target).
    /// Right pressed: fire <c>onCancelled</c> then <c>onRemove</c>.
    /// </remarks>
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && !isPressed)
        {
            if (_hoveredValid)
            {
                _onPicked(_hoveredEntity);
                _onRemove();
            }
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onCancelled();
            _onRemove();
        }
    }

    /// <inheritdoc/>
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (key == MapKeyboardKey.Escape && isPressed)
        {
            _onCancelled();
            _onRemove();
        }
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

    // ---- Test hooks ---------------------------------------------------------

    /// <summary>Color used in the last <see cref="UpdateAndDraw"/> call. Null before first call.</summary>
    internal Rgba32? TestHook_LastDrawColor;

    /// <summary>
    /// Force <c>_hoveredValid</c> for unit-test scenarios where a real hit-test
    /// delegate is not available. Set to <c>true</c> to exercise the red crosshair path.
    /// </summary>
    internal bool TestHook_ForceHoveredValid { set => _hoveredValid = value; }
}
