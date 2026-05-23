using System;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// A host-provided renderer that draws into the canvas at a specific
/// <see cref="CanvasRenderPass"/>. Renderers are stateless regarding
/// canvas internals; they receive everything they need via
/// <see cref="ICanvasRenderContext"/>.
/// </summary>
public interface ICustomCanvasRenderer : IDisposable
{
    /// <summary>Unique identifier for this renderer (e.g., "hsm.transition_labels").</summary>
    string Id { get; }

    /// <summary>The render pass this renderer runs in.</summary>
    CanvasRenderPass Pass { get; }

    /// <summary>
    /// Called once per frame when this renderer is active.
    /// All drawing must go through <paramref name="ctx"/>.DrawList.
    /// </summary>
    void Render(ICanvasRenderContext ctx);

    /// <summary>
    /// When false, the canvas skips this renderer entirely (no Render call, no hit-test,
    /// no perf accounting). Defaults to true.
    /// </summary>
    bool IsActive => true;

    // Default no-op Dispose so simple renderers don't need boilerplate.
    void IDisposable.Dispose() { }
}

/// <summary>
/// Optional companion for <see cref="ICustomCanvasRenderer"/> implementations
/// that need their drawn content to be hit-testable (clickable/hoverable).
/// Implement on the same class as the renderer.
/// </summary>
public interface ICustomCanvasHitTester
{
    /// <summary>
    /// Test whether a canvas-coordinate point hits any element drawn by this renderer.
    /// Returns null on a miss. Keep this method fast (O(visible elements) or better).
    /// </summary>
    CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx);
}

/// <summary>
/// Optional companion for <see cref="ICustomCanvasRenderer"/> implementations
/// that want their hit-testable elements to participate in the selection model.
/// </summary>
public interface ICustomCanvasSelectable
{
    /// <summary>Called when the user selects a custom element owned by this renderer.</summary>
    void OnElementSelected(string elementKey, CustomElementHit hit);

    /// <summary>Called when the user deselects a custom element owned by this renderer.</summary>
    void OnElementDeselected(string elementKey);
}

// ── Supporting types ──────────────────────────────────────────────────────────

/// <summary>Result returned by a successful ICustomCanvasHitTester.HitTest.</summary>
/// <param name="ElementKey">Host-stable identifier for the element that was hit.</param>
/// <param name="Kind">Host-assigned kind tag (e.g., "transition_label", "region_conflict").</param>
/// <param name="Bounds">Canvas-coordinate AABB of the hit element.</param>
public readonly record struct CustomElementHit(
    string ElementKey,
    CustomElementKind Kind,
    RectF Bounds);

/// <summary>Semantic category for a custom-drawn hit element.</summary>
public enum CustomElementKind
{
    /// <summary>Decoration attached to a link (e.g., transition label).</summary>
    LinkDecoration,
    /// <summary>Adornment attached to a node (e.g., initial-state arrow target).</summary>
    NodeAdornment,
    /// <summary>Free-standing element not attached to a specific node or link.</summary>
    Standalone,
    /// <summary>Ephemeral display element (e.g., debug tooltip); usually not hit-testable.</summary>
    Tooltip,
}

/// <summary>
/// Uniquely identifies a custom-drawn element across the editor session.
/// Combines the renderer's Id with an element key scoped to that renderer.
/// </summary>
public readonly record struct CustomElementRef(string RendererId, string ElementKey);
