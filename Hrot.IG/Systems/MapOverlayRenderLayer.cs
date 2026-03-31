using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace Hrot.IG.Systems;

/// <summary>
/// Map layer that renders <see cref="EditablePolyline"/> entities that carry a
/// <see cref="MapOverlayStyle"/> component (i.e. tactical graphic area overlays).
///
/// <para><b>Rendering contract</b></para>
/// <list type="bullet">
///   <item>
///     Fill — rendered as a triangle fan radiating from vertex 0, using
///     <see cref="MapOverlayStyle.FillColor"/>.
///   </item>
///   <item>
///     Border — each adjacent pair of vertices is drawn with
///     <see cref="Raylib.DrawLineEx"/>, thickness from
///     <see cref="MapOverlayStyle.LineThickness"/>.
///     When <see cref="MapOverlayStyle.IsClosed"/> is <c>true</c> the last
///     segment connects back to vertex 0.
///   </item>
/// </list>
///
/// <para>Zero heap allocations in <see cref="Draw"/> — plain <c>for</c> loops
/// over the pre-built query (§CODE-STANDARDS §4).  The
/// <see cref="PickEntity"/> ray-cast uses a standard even-odd winding test.</para>
/// </summary>
public class MapOverlayRenderLayer : IMapLayer
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Display name shown in the layer-visibility UI.</summary>
    public const string LayerName = "MapOverlays";

    /// <summary>Bit-index 3 → "tactical_graphics" layer slot.</summary>
    public const int TacticalGraphicsLayerBitIndex = 3;

    // ── IMapLayer ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Name => LayerName;

    /// <inheritdoc/>
    public int LayerBitIndex => TacticalGraphicsLayerBitIndex;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ISimulationView _view;
    private readonly EntityQuery     _query;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="view">
    /// Simulation view used to read <see cref="EditablePolyline"/> and
    /// <see cref="MapOverlayStyle"/> components.
    /// </param>
    /// <param name="query">
    /// Pre-built query returning overlay entities.
    /// Should at minimum include <c>WithManaged&lt;EditablePolyline&gt;()</c>
    /// and <c>With&lt;MapOverlayStyle&gt;()</c>.
    /// </param>
    public MapOverlayRenderLayer(ISimulationView view, EntityQuery query)
    {
        _view  = view  ?? throw new System.ArgumentNullException(nameof(view));
        _query = query ?? throw new System.ArgumentNullException(nameof(query));
    }

    // ── IMapLayer methods ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Update(float dt) { /* State is driven by ECS; nothing to tick here. */ }

    /// <inheritdoc/>
    /// <remarks>
    /// Called inside <c>MapCanvas.Draw()</c> → <c>BeginMode2D</c>.
    /// All coordinates are in world space; the camera applies zoom and pan.
    /// <para>
    /// <b>Coordinate convention:</b> <see cref="EditablePolyline.Points"/> are stored as
    /// <em>relative</em> Cartesian offsets from <see cref="SimTransform.Position"/>.
    /// The absolute screen position is computed inline here as:
    /// <c>absPos[i] = simPos.Xy + polyline.Points[i]</c>.
    /// No extra ECS system or per-frame recalculation is needed outside of this render pass.
    /// </para>
    /// </remarks>
    public void Draw(RenderContext ctx)
    {
        foreach (var entity in _query)
        {
            if (!_view.HasManagedComponent<EditablePolyline>(entity))
                continue;

            if (!_view.HasComponent<MapOverlayStyle>(entity))
                continue;

            if (!_view.HasComponent<SimTransform>(entity))
                continue;

            var polyline = _view.GetManagedComponentRO<EditablePolyline>(entity);
            if (polyline.Points == null || polyline.Points.Count < 3)
                continue;

            ref readonly var style  = ref _view.GetComponentRO<MapOverlayStyle>(entity);
            ref readonly var simTr  = ref _view.GetComponentRO<SimTransform>(entity);

            var origin = new Vector2(simTr.Position.X, simTr.Position.Y);

            var fillColor   = new Color(style.FillR,   style.FillG,   style.FillB,   style.FillA);
            var borderColor = new Color(style.BorderR,  style.BorderG, style.BorderB, style.BorderA);

            DrawFill(polyline.Points,   origin, fillColor);
            DrawBorder(polyline.Points, origin, borderColor, style.LineThickness, style.IsClosed);

            // Draw reference-point symbol: a small yellow diamond at the entity's SimTransform position.
            DrawReferencePointSymbol(origin);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Overlay layers do not consume mouse input; picking is done via <see cref="PickEntity"/>.</remarks>
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the first entity whose polygon contains <paramref name="worldPos"/>
    /// using an even-odd winding (ray-casting) test.
    /// Polygon points are offset by the entity's <see cref="SimTransform.Position"/>
    /// before testing, matching the render output.
    /// </remarks>
    public Entity? PickEntity(Vector2 worldPos)
    {
        foreach (var entity in _query)
        {
            if (!_view.HasManagedComponent<EditablePolyline>(entity))
                continue;

            if (!_view.HasComponent<SimTransform>(entity))
                continue;

            var polyline = _view.GetManagedComponentRO<EditablePolyline>(entity);
            if (polyline.Points == null || polyline.Points.Count < 3)
                continue;

            ref readonly var simTr = ref _view.GetComponentRO<SimTransform>(entity);
            var origin = new Vector2(simTr.Position.X, simTr.Position.Y);

            // Shift worldPos into the polygon's local frame for the containment test.
            if (IsPointInPolygon(worldPos - origin, polyline.Points))
                return entity;
        }

        return null;
    }

    // ── Private draw helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Renders a filled polygon as a triangle fan from vertex 0.
    /// <paramref name="origin"/> is added to every point before rendering.
    /// Requires at least 3 vertices.
    /// </summary>
    private static void DrawFill(IReadOnlyList<Vector2> pts, Vector2 origin, Color fill)
    {
        // Triangle fan: (pts[0], pts[i], pts[i+1]) for i = 1 … n-2
        for (int i = 1; i < pts.Count - 1; i++)
        {
            Raylib.DrawTriangle(origin + pts[0], origin + pts[i], origin + pts[i + 1], fill);
        }
    }

    /// <summary>
    /// Renders the polygon outline as individual line segments.
    /// <paramref name="origin"/> is added to every point before rendering.
    /// </summary>
    private static void DrawBorder(
        IReadOnlyList<Vector2> pts,
        Vector2 origin,
        Color border,
        float thickness,
        bool closed)
    {
        int n            = pts.Count;
        int segmentCount = closed ? n : n - 1;

        for (int i = 0; i < segmentCount; i++)
        {
            var a = origin + pts[i];
            var b = origin + pts[(i + 1) % n];
            Raylib.DrawLineEx(a, b, thickness, border);
        }
    }

    /// <summary>
    /// Draws a small yellow diamond symbol at <paramref name="pos"/> to mark the
    /// entity's reference point (centroid / <see cref="SimTransform"/> position).
    /// </summary>
    private static void DrawReferencePointSymbol(Vector2 pos)
    {
        const float HalfSize = 6f;
        var yellow = new Color(255, 220, 0, 220);  // semi-transparent yellow

        // Diamond = four triangles meeting at centre
        var top    = new Vector2(pos.X,             pos.Y - HalfSize);
        var right  = new Vector2(pos.X + HalfSize,  pos.Y);
        var bottom = new Vector2(pos.X,             pos.Y + HalfSize);
        var left   = new Vector2(pos.X - HalfSize,  pos.Y);

        Raylib.DrawTriangle(pos, right,  top,    yellow);
        Raylib.DrawTriangle(pos, bottom, right,  yellow);
        Raylib.DrawTriangle(pos, left,   bottom, yellow);
        Raylib.DrawTriangle(pos, top,    left,   yellow);
    }

    // ── Private geometry helpers ──────────────────────────────────────────────

    /// <summary>
    /// Point-in-polygon test using the even-odd (ray-casting) algorithm.
    /// O(n) in the number of polygon vertices; no heap allocation.
    /// </summary>
    private static bool IsPointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;
        int  j      = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];

            if ((pi.Y > point.Y) != (pj.Y > point.Y) &&
                point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }
}
