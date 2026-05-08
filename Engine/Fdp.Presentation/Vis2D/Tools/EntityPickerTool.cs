using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Tools;

/// <summary>
/// A map tool that lets the operator click to pick a single entity from the
/// canvas, filtered by domain-specific criteria supplied by an
/// <see cref="IEntityFilterFactory"/>.
///
/// <para><b>Workflow:</b>
/// <list type="number">
///   <item>Caller pushes this tool onto the <see cref="MapCanvas"/> stack.</item>
///   <item>Operator hovers over the map; a target crosshair follows the cursor.
///         The crosshair turns red when the cursor is over a pickable entity.</item>
///   <item>Left-click on a valid entity fires <see cref="OnEntityPicked"/> and
///         pops the tool from the stack.</item>
///   <item>Right-click or <c>Escape</c> fires <see cref="OnCancelled"/> and
///         pops the tool without a result.</item>
/// </list>
/// </para>
///
/// <para><b>No allocations on the 60 FPS hot path.</b>
/// The IEntityFilter is compiled exactly once in the constructor.
/// <see cref="HandleHover"/> and <see cref="Draw"/> are entirely
/// allocation-free.</para>
/// </summary>
public sealed class EntityPickerTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => "EntityPicker";

    // ── Deps ──────────────────────────────────────────────────────────────────

    private readonly IEntityFilter _filter;

    // ── State ─────────────────────────────────────────────────────────────────

    private MapCanvas? _canvas;
    private Vector2    _mouseWorldPos;
    private Entity     _hoveredEntity = Entity.Null;
    private bool       _hoveredValid;

    // ── Visual constants ──────────────────────────────────────────────────────

    private const float CrosshairHalfSize  = 12f;
    private const float CrosshairThickness = 1.5f;
    private const float CrosshairGapRadius = 4f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the operator left-clicks a valid (filter-passing) entity.
    /// The tool pops itself immediately before firing the event.
    /// </summary>
    public event Action<Entity>? OnEntityPicked;

    /// <summary>
    /// Fired when the operator cancels the pick (right-click or <c>Escape</c>).
    /// The tool pops itself immediately before firing the event.
    /// </summary>
    public event Action? OnCancelled;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an entity picker tool.
    /// </summary>
    /// <param name="filterFactory">
    /// Domain-specific factory that compiles <paramref name="filterPresets"/> into a
    /// high-performance <see cref="IEntityFilter"/>.  Injected by the hosting
    /// application; the Vis2D toolkit has no knowledge of how filters are resolved.
    /// </param>
    /// <param name="filterPresets">
    /// Array of preset names forwarded verbatim to <paramref name="filterFactory"/>.
    /// E.g. <c>["road_graphs"]</c> or <c>["units_ground","vehicles"]</c>.
    /// Null is treated as empty (match-all).
    /// </param>
    public EntityPickerTool(IEntityFilterFactory filterFactory, string[]? filterPresets = null)
    {
        ArgumentNullException.ThrowIfNull(filterFactory);
        _filter = filterFactory.CreateFilter(filterPresets ?? Array.Empty<string>());
    }

    // ── IMapTool lifecycle ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void OnEnter(MapCanvas canvas)
    {
        _canvas        = canvas;
        _hoveredEntity = Entity.Null;
        _hoveredValid  = false;
    }

    /// <inheritdoc/>
    public void OnExit()
    {
        _canvas = null;
    }

    /// <inheritdoc/>
    public void Update(float dt) { /* stateless between frames */ }

    // ── Input ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos)
    {
        _mouseWorldPos = worldPos;

        // Spatial hit-test: allocation-free, O(entity count) but terminates at first hit.
        Entity candidate = _canvas?.PickTopmostEntity(worldPos) ?? Entity.Null;

        // Apply domain filter compiled at construction time — O(1) bitwise check.
        _hoveredEntity = candidate;
        _hoveredValid  = !candidate.IsNull && _filter.IsMatch(candidate);

        return false; // Do not consume hover; camera-pan may still read mouse pos.
    }

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MapMouseButton button)
    {
        if (button == MapMouseButton.Left && _hoveredValid)
        {
            var picked = _hoveredEntity;
            _canvas?.PopTool();
            OnEntityPicked?.Invoke(picked);
            return true;
        }

        if (button == MapMouseButton.Right)
        {
            _canvas?.PopTool();
            OnCancelled?.Invoke();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;

    /// <inheritdoc/>
    public bool HandleKeyPressed(MapKeyboardKey key)
    {
        if (key == MapKeyboardKey.Escape)
        {
            _canvas?.PopTool();
            OnCancelled?.Invoke();
            return true;
        }
        return false;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <summary>
    /// Draws a target crosshair at the mouse cursor.
    /// <list type="bullet">
    ///   <item>Red <c>(255, 0, 0)</c> when the cursor is over a filter-passing entity.</item>
    ///   <item>Amber <c>(255, 161, 0)</c> otherwise (waiting for a valid pick target).</item>
    /// </list>
    /// All draw calls are allocation-free.
    /// </summary>
    public void Draw(RenderContext ctx)
    {
        float zoom = ctx.Zoom > 0 ? ctx.Zoom : 1f;
        float size  = CrosshairHalfSize  / zoom;
        float thick = CrosshairThickness / zoom;
        float gap   = CrosshairGapRadius / zoom;

        // Amber = waiting for pick; Red = valid target under cursor.
        Rgba32 drawColor = _hoveredValid
            ? new Rgba32(255, 0,   0,   255)   // red   — hovering a valid pick target
            : new Rgba32(255, 161, 0,   255);  // amber — waiting for operator to hover

        TestHook_LastUsedColor = drawColor;

        var pos = _mouseWorldPos;

        // Horizontal arm: left segment + right segment (gap in centre).
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X - size, pos.Y, 0f), new Vector3(pos.X - gap, pos.Y, 0f), drawColor, thick);
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X + gap,  pos.Y, 0f), new Vector3(pos.X + size, pos.Y, 0f), drawColor, thick);

        // Vertical arm: top segment + bottom segment.
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X, pos.Y - size, 0f), new Vector3(pos.X, pos.Y - gap, 0f), drawColor, thick);
        ctx.DrawBuilder?.DrawLine(new Vector3(pos.X, pos.Y + gap,  0f), new Vector3(pos.X, pos.Y + size, 0f), drawColor, thick);

        // Circle outline around the gap.
        ctx.DrawBuilder?.DrawSphere(new Vector3(pos.X, pos.Y, 0f), gap, drawColor);
    }

    // ── Test hooks ────────────────────────────────────────────────────────────

    /// <summary>When <c>true</c>, draw calls are skipped; <see cref="TestHook_LastUsedColor"/> is still set.</summary>
    internal bool TestHook_SkipRaylibCalls;

    /// <summary>Color used in the last <see cref="Draw"/> call. Null before first call.</summary>
    internal Rgba32? TestHook_LastUsedColor;

    /// <summary>
    /// Force <c>_hoveredValid</c> for unit-test scenarios where a real canvas with entities
    /// is not available. Set to <c>true</c> to exercise the red crosshair path.
    /// </summary>
    internal bool TestHook_ForceHoveredValid
    {
        set => _hoveredValid = value;
    }
}
