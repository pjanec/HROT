using System.Numerics;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D.Abstractions;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Rendering;

/// <summary>
/// Map layer that renders <see cref="RoutePlan"/> waypoints for all
/// <c>TacGraphic_Route</c> entities on the <c>road_graphs</c> layer (bit 4).
///
/// <para>
/// Each route is drawn as a sequence of line segments between consecutive waypoints,
/// with a filled circle handle at every vertex. Normal routes are drawn in blue
/// (<c>#4488FF</c>); the selected route is highlighted in yellow (<c>#FFD700</c>).
/// For looping routes, an additional segment connects the last waypoint back to the first.
/// </para>
///
/// <para>No heap allocations in the hot path â€” plain <c>for</c> loops with pre-built queries.</para>
/// </summary>
public class RouteRenderLayer : IMapLayer
{
    // â”€â”€ Constants (Â§CODE-STANDARDS Â§1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Display name shown in the layer-visibility UI.</summary>
    public const string LayerName = "Routes";

    /// <summary>Bit-index 4 â†’ <c>road_graphs</c> layer slot (same bit as TkbType predicate).</summary>
    public const int RoadGraphsLayerBitIndex = 4;

    private const float LineThickness = 2f;
    private const float VertexRadius  = 5f;

    private static readonly Color NormalColor   = new(0x44, 0x88, 0xFF, 0xFF); // #4488FF
    private static readonly Color SelectedColor = new(0xFF, 0xD7, 0x00, 0xFF); // #FFD700

    // â”€â”€ IMapLayer â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public string Name => LayerName;

    /// <inheritdoc/>
    public int LayerBitIndex => RoadGraphsLayerBitIndex;

    // â”€â”€ Fields â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly ISimulationView   _view;
    private readonly EntityQuery       _query;
    private readonly IInspectorContext? _inspector;

    // â”€â”€ Test hooks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// When <c>true</c>, Raylib draw calls are skipped. Counter fields are still updated.
    /// Set by unit tests so rendering can be asserted headlessly.
    /// </summary>
    public bool TestHook_SkipRaylibCalls { get; set; }

    /// <summary>Total line-segment draw calls made in the last <see cref="Draw"/> pass.</summary>
    public int TestHook_LineDrawCount { get; private set; }

    /// <summary>Total vertex-circle draw calls made in the last <see cref="Draw"/> pass.</summary>
    public int TestHook_CircleDrawCount { get; private set; }

    // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <param name="view">Simulation view used to read <see cref="RoutePlan"/> components.</param>
    /// <param name="query">
    /// Pre-built query returning route entities (should include
    /// <c>With&lt;TkbIdentity&gt;()</c> and <c>WithManaged&lt;RoutePlan&gt;()</c>).
    /// </param>
    /// <param name="inspector">
    /// Optional inspector state used to determine which route entity is selected so
    /// the layer can apply the highlight colour. Pass <c>null</c> in unit tests that
    /// do not need selection colouring.
    /// </param>
    public RouteRenderLayer(
        ISimulationView    view,
        EntityQuery        query,
        IInspectorContext? inspector = null)
    {
        _view      = view  ?? throw new System.ArgumentNullException(nameof(view));
        _query     = query ?? throw new System.ArgumentNullException(nameof(query));
        _inspector = inspector;
    }

    // â”€â”€ IMapLayer methods â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public void Update(float dt) { }

    /// <inheritdoc/>
    /// <remarks>
    /// Only renders when the <c>road_graphs</c> layer bit is set in
    /// <see cref="RenderContext.VisibleLayersMask"/>.<br/>
    /// Waypoint positions in <see cref="RoutePlan"/> are absolute Cartesian world-space
    /// <see cref="System.Numerics.Vector3"/>s; only
    /// the XZ plane is used since the 2D canvas works in XZ (X = east, Z = north = canvas Y).
    /// </remarks>
    public void Draw(RenderContext ctx)
    {
        TestHook_LineDrawCount   = 0;
        TestHook_CircleDrawCount = 0;

        // Respect layer visibility toggle.
        uint roadGraphsBit = 1u << RoadGraphsLayerBitIndex;
        if ((ctx.VisibleLayersMask & roadGraphsBit) == 0)
            return;

        var selectedEntity = _inspector?.SelectedEntity;

        foreach (var entity in _query)
        {
            if (!_view.HasComponent<TkbIdentity>(entity))
                continue;

            ref readonly var tkb = ref _view.GetComponentRO<TkbIdentity>(entity);
            if (tkb.TkbType != TkbEntityTypes.TacGraphic_Route)
                continue;

            if (!_view.HasManagedComponent<RoutePlan>(entity))
                continue;

            var plan  = _view.GetManagedComponentRO<RoutePlan>(entity);
            if ((plan.Waypoints?.Count ?? 0) == 0)
                continue;

            bool isSelected = selectedEntity.HasValue && selectedEntity.Value == entity;
            var  color      = isSelected ? SelectedColor : NormalColor;

            DrawRoute(plan, color);
        }
    }

    /// <inheritdoc/>
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;

    /// <summary>
    /// Returns the route entity whose polyline passes within <see cref="PickRadius"/> of
    /// <paramref name="worldPos"/>, or <c>null</c> when no route is close enough.
    /// </summary>
    /// <remarks>
    /// Coordinate convention: <paramref name="worldPos"/> is in canvas space
    /// (<c>X</c> = east, <c>Y</c> = north = world Z), matching the waypoint
    /// representation produced by <see cref="ToCanvas"/>.
    /// </remarks>
    public Entity? PickEntity(Vector2 worldPos)
    {
        foreach (var entity in _query)
        {
            if (!_view.HasComponent<TkbIdentity>(entity))
                continue;

            ref readonly var tkb = ref _view.GetComponentRO<TkbIdentity>(entity);
            if (tkb.TkbType != TkbEntityTypes.TacGraphic_Route)
                continue;

            if (!_view.HasManagedComponent<RoutePlan>(entity))
                continue;

            var plan = _view.GetManagedComponentRO<RoutePlan>(entity);
            var waypoints = plan.Waypoints;
            if (waypoints == null || waypoints.Count == 0)
                continue;

            int n        = waypoints.Count;
            int segCount = plan.IsLoop ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                var a = ToCanvas(waypoints[i].Position);
                var b = ToCanvas(waypoints[(i + 1) % n].Position);

                if (DistanceToSegment(worldPos, a, b) < PickRadius)
                    return entity;
            }
        }

        return null;
    }

    // â”€â”€ Private draw helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Pick radius in world units â€” used by <see cref="PickEntity"/>.</summary>
    private const float PickRadius = 7.0f;

    /// <summary>
    /// Returns the minimum distance from point <paramref name="p"/> to the line
    /// segment <paramref name="a"/>â€“<paramref name="b"/>.
    /// </summary>
    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab    = b - a;
        var ap    = p - a;
        float lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 1e-10f)
            return Vector2.Distance(p, a);
        float t    = Math.Clamp(Vector2.Dot(ap, ab) / lenSq, 0f, 1f);
        var closest = a + t * ab;
        return Vector2.Distance(p, closest);
    }

    private void DrawRoute(RoutePlan plan, Color color)
    {
        int n = plan.Waypoints.Count;

        // Draw line segments between consecutive waypoints.
        int segCount = plan.IsLoop ? n : n - 1;
        for (int i = 0; i < segCount; i++)
        {
            var a = ToCanvas(plan.Waypoints[i].Position);
            var b = ToCanvas(plan.Waypoints[(i + 1) % n].Position);

            if (!TestHook_SkipRaylibCalls)
                Raylib.DrawLineEx(a, b, LineThickness, color);
            TestHook_LineDrawCount++;
        }

        // Draw vertex handle circles.
        for (int i = 0; i < n; i++)
        {
            var pos = ToCanvas(plan.Waypoints[i].Position);

            if (!TestHook_SkipRaylibCalls)
                Raylib.DrawCircleV(pos, VertexRadius, color);
            TestHook_CircleDrawCount++;
        }
    }

    /// <summary>
    /// Converts an absolute Cartesian ECS world position to 2D canvas space.
    /// The IG 2D canvas uses X (east) and Y (= world Z, north) convention.
    /// </summary>
    private static Vector2 ToCanvas(System.Numerics.Vector3 pos)
        => new Vector2(pos.X, pos.Z);
}
