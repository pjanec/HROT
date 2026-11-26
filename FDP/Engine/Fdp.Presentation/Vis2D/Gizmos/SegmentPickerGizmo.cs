using System;
using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>⭐ CE-6500 — the live label of a two-point pick for (A, the cursor): its words and whether they are good news (green) or a warning (red).</summary>
public readonly record struct SegmentHint(string Text, bool Ok);

/// <summary>
/// ⭐ CE-6500 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13; mockup UX/bridge-laying-ui-visual.svg) — TWO points in one gesture: the first left click places A,
/// then a line follows the cursor from A with the optional <see cref="SegmentHint"/> label, and the second click places B (<paramref name="onPicked"/>(A, B)).
/// Right-click / Escape cancels. The sibling of <see cref="FdpLocationPickerGizmo"/> (same crosshair, same raw-input contract, remove-then-complete).
/// The hint is asked only when the cursor moved ≥ <see cref="HintStepMetres"/> (U-4) — a steady frame asks nothing and allocates nothing.
/// </summary>
public sealed class SegmentPickerGizmo : IEntityStatefulGizmo
{
    private const float CrosshairHalfSize = 3.5f, CrosshairThickness = 1.5f, CrosshairGapRadius = 1.25f;
    /// <summary>How far the cursor moves before the hint is asked again (m).</summary>
    public const float HintStepMetres = 0.25f;

    private static readonly Rgba32 Cursor = new(102, 191, 255, 255);
    private static readonly Rgba32 Plain = new(230, 230, 236, 255);
    private static readonly Rgba32 Good = new(91, 208, 106, 255);
    private static readonly Rgba32 Bad = new(230, 85, 85, 255);

    private readonly Action<Vector2, Vector2> _onPicked;
    private readonly Action _onRemove;
    private readonly Action? _onCancelled;
    private readonly Func<Vector2, Vector2, SegmentHint>? _hint;
    private Vector3 _cursorWorld;
    private Vector2? _a;
    private Vector2 _hintAt = new(float.NaN, float.NaN);
    private SegmentHint _label;

    /// <param name="onPicked">Fired with (A, B) on the second left-click release, after <paramref name="onRemove"/>.</param>
    /// <param name="onRemove">Unregisters the gizmo.</param>
    /// <param name="onCancelled">Fired on right-click / Escape, after <paramref name="onRemove"/>.</param>
    /// <param name="hint">The live label for (A, cursor), or null — the line then shows its length.</param>
    public SegmentPickerGizmo(Action<Vector2, Vector2> onPicked, Action onRemove, Action? onCancelled = null,
                              Func<Vector2, Vector2, SegmentHint>? hint = null)
    {
        _onPicked = onPicked ?? throw new ArgumentNullException(nameof(onPicked));
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
        _onCancelled = onCancelled;
        _hint = hint;
    }

    /// <summary>A, once placed (rails).</summary>
    public Vector2? First => _a;
    /// <summary>The label drawn now (rails).</summary>
    public SegmentHint Label => _label;

    public bool RequiresExclusiveFocus => true;
    /// <summary>TRUE, as <see cref="FdpLocationPickerGizmo.WantsRawInput"/> explains: the whole behaviour is in the raw handlers.</summary>
    public bool WantsRawInput => true;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        var pos = _cursorWorld;
        draw.DrawLine(new Vector3(pos.X - CrosshairHalfSize, pos.Y, 0f), new Vector3(pos.X - CrosshairGapRadius, pos.Y, 0f), Cursor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X + CrosshairGapRadius, pos.Y, 0f), new Vector3(pos.X + CrosshairHalfSize, pos.Y, 0f), Cursor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X, pos.Y - CrosshairHalfSize, 0f), new Vector3(pos.X, pos.Y - CrosshairGapRadius, 0f), Cursor, CrosshairThickness);
        draw.DrawLine(new Vector3(pos.X, pos.Y + CrosshairGapRadius, 0f), new Vector3(pos.X, pos.Y + CrosshairHalfSize, 0f), Cursor, CrosshairThickness);
        if (_a is not { } a) { draw.DrawSphere(new Vector3(pos.X, pos.Y, 0f), CrosshairGapRadius, Cursor); return; }

        var b = new Vector2(pos.X, pos.Y);
        Refresh(a, b);
        var colour = _hint == null ? Plain : _label.Ok ? Good : Bad;
        draw.DrawSphere(new Vector3(a.X, a.Y, 0f), CrosshairGapRadius, colour);
        draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), colour, 2f);
        if (_label.Text is { Length: > 0 } text)
            draw.DrawTextLong((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f, text, colour);
    }

    /// <summary>Asks the hint for (A, B) when B moved far enough since the last ask (U-4); without a hint, the length in metres.</summary>
    private void Refresh(Vector2 a, Vector2 b)
    {
        if (!float.IsNaN(_hintAt.X) && Vector2.DistanceSquared(_hintAt, b) < HintStepMetres * HintStepMetres) return;
        _hintAt = b;
        _label = _hint != null ? _hint(a, b)
               : new SegmentHint(Vector2.Distance(a, b).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m", true);
    }

    public void OnDragUpdate(Vector3 worldPos) => _cursorWorld = worldPos;

    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
    {
        if (button == MapMouseButton.Left && !isPressed)
        {
            var p = new Vector2(worldPos.X, worldPos.Y);
            if (_a is not { } a) { _a = p; _hintAt = new Vector2(float.NaN, float.NaN); return; }
            _onRemove();   // remove FIRST, then complete — FdpLocationPickerGizmo's reason (the tool underneath is restored before anyone sees the result)
            _onPicked(a, p);
        }
        else if (button == MapMouseButton.Right && isPressed)
        {
            _onRemove();
            _onCancelled?.Invoke();
        }
    }

    public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        if (key == MapKeyboardKey.Escape && isPressed)
        {
            _onRemove();
            _onCancelled?.Invoke();
        }
    }

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMenuAction(int actionId) { }
    public void Dispose() { }
}
