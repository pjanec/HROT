using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.Map.Common.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Bagira.IG.Tools;

/// <summary>
/// Specialised map tool for editing the waypoints of a <see cref="RoutePlan"/> component
/// (ROUTES1-T012).
///
/// <para>
/// On enter, the tool copies <see cref="RoutePlan.Waypoints"/> into an in-memory
/// <em>ghost</em> list. All edits mutate the ghost. On right-click commit, the ghost is
/// handed back to the caller via the <see cref="OnRouteCommitted"/> event.
/// </para>
///
/// <para>
/// Vertex insertion: when a left-click lands outside every vertex's pick radius, the tool
/// performs a point-to-segment distance check. If the click is within the pick radius of
/// a segment [i, i+1], a new waypoint is inserted at index i+1, inheriting the
/// <c>TargetSpeed</c> and <c>ExtensionJson</c> from waypoint i.
/// </para>
/// </summary>
public class RouteEditTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => RouteEditToolConstants.ToolName;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly Entity _routeEntity;
    private readonly RoutePlan _plan;
    private readonly Action<Entity, List<RouteWaypoint>> _onCommit;

    private MapCanvas? _canvas;

    private List<RouteWaypoint> _ghost = new();
    private int _selectedVertexIndex = NoVertex;

    private const int NoVertex = -1;

    // ── Public observable state (for WaypointEditorPanel and tests) ───────────

    /// <summary>
    /// Index of the currently selected ghost waypoint, or <c>-1</c> when none.
    /// </summary>
    public int SelectedVertexIndex => _selectedVertexIndex;

    /// <summary>Read-only view of the ghost waypoint list for assertions.</summary>
    public IReadOnlyList<RouteWaypoint> GhostWaypoints => _ghost;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the operator right-clicks to commit. The caller should persist the
    /// waypoints to the ECS component and publish a network update descriptor.
    /// </summary>
    public event Action<Entity, List<RouteWaypoint>>? OnRouteCommitted;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <param name="routeEntity">Entity whose <see cref="RoutePlan"/> is being edited.</param>
    /// <param name="plan">The live <see cref="RoutePlan"/> component — only used at enter-time to seed the ghost.</param>
    /// <param name="onCommit">Callback invoked with the edited waypoint list on right-click commit.</param>
    public RouteEditTool(
        Entity routeEntity,
        RoutePlan plan,
        Action<Entity, List<RouteWaypoint>> onCommit)
    {
        _routeEntity = routeEntity;
        _plan        = plan   ?? throw new ArgumentNullException(nameof(plan));
        _onCommit    = onCommit ?? throw new ArgumentNullException(nameof(onCommit));
    }

    // ── IMapTool lifecycle ────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>Copies current waypoints into the ghost list and resets selection.</remarks>
    public void OnEnter(MapCanvas canvas)
    {
        _canvas              = canvas;
        _selectedVertexIndex = NoVertex;
        _ghost               = new List<RouteWaypoint>(_plan.Waypoints.Count);
        for (int i = 0; i < _plan.Waypoints.Count; i++)
            _ghost.Add(_plan.Waypoints[i]);
    }

    /// <inheritdoc/>
    public void OnExit()
    {
        _canvas              = null;
        _selectedVertexIndex = NoVertex;
    }

    /// <inheritdoc/>
    public void Update(float dt) { }

    // ── Input ──────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos)
    {
        // Don't reset selection while actively dragging.
        if (_canvas?.Input.IsMouseButtonDown(MouseButton.Left) == true)
            return false;

        _selectedVertexIndex = FindNearestVertex(worldPos);
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Left-click: select a vertex or insert on a segment.
    /// Right-click: commit ghost and signal pop.
    /// </remarks>
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            int nearest = FindNearestVertex(worldPos);
            if (nearest >= 0)
            {
                _selectedVertexIndex = nearest;
            }
            else
            {
                // No vertex in range — check for segment insertion.
                int segIdx = FindNearestSegment(worldPos);
                if (segIdx >= 0)
                {
                    var inherited = _ghost[segIdx];
                    var inserted  = new RouteWaypoint
                    {
                        Position      = new Vector3(worldPos.X, 0f, worldPos.Y),
                        TargetSpeed   = inherited.TargetSpeed,
                        ExtensionJson = inherited.ExtensionJson,
                    };
                    _ghost.Insert(segIdx + 1, inserted);
                    _selectedVertexIndex = segIdx + 1;
                }
            }
            return true;
        }

        if (button == MouseButton.Right)
        {
            CommitChanges();
            _canvas?.PopTool();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool HandleDrag(Vector2 worldPos, Vector2 delta)
    {
        if (_canvas != null && !_canvas.Input.IsMouseButtonDown(MouseButton.Left))
            return false;

        if (_selectedVertexIndex < 0)
            _selectedVertexIndex = FindGloballyNearestVertex(worldPos);

        if (_selectedVertexIndex < 0 || _selectedVertexIndex >= _ghost.Count)
            return false;

        var wp = _ghost[_selectedVertexIndex];
        wp.Position = new Vector3(
            wp.Position.X + delta.X,
            wp.Position.Y,
            wp.Position.Z + delta.Y);
        _ghost[_selectedVertexIndex] = wp;
        return true;
    }

    /// <inheritdoc/>
    public bool HandleKeyPressed(KeyboardKey key)
    {
        if (key == KeyboardKey.Delete)
        {
            if (_selectedVertexIndex >= 0 && _selectedVertexIndex < _ghost.Count)
            {
                _ghost.RemoveAt(_selectedVertexIndex);
                _selectedVertexIndex = System.Math.Clamp(_selectedVertexIndex, NoVertex, _ghost.Count - 1);
            }
            return true;
        }

        if (key == KeyboardKey.Escape)
        {
            // Cancel without committing.
            _canvas?.PopTool();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        if (_ghost.Count == 0) return;

        // Draw ghost polyline.
        for (int i = 0; i < _ghost.Count - 1; i++)
        {
            var a = ToCanvas(_ghost[i].Position);
            var b = ToCanvas(_ghost[i + 1].Position);
            Raylib.DrawLineEx(a, b, 2f, Color.Yellow);
        }

        // Draw vertex handles.
        for (int i = 0; i < _ghost.Count; i++)
        {
            var  pos = ToCanvas(_ghost[i].Position);
            bool sel = i == _selectedVertexIndex;
            Raylib.DrawCircleV(
                pos,
                sel ? RouteEditToolConstants.SelectedHandleRadius : RouteEditToolConstants.HandleRadius,
                sel ? Color.Red : Color.White);
        }
    }

    // ── Public accessor for WaypointEditorPanel ───────────────────────────────

    /// <summary>
    /// Returns a reference to the selected waypoint in the ghost list so that
    /// <c>WaypointEditorPanel</c> can edit <c>TargetSpeed</c> and <c>ExtensionJson</c>
    /// in-place without copying.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="SelectedVertexIndex"/> is <c>-1</c>.
    /// </exception>
    public ref RouteWaypoint GetSelectedWaypointRef()
    {
        if (_selectedVertexIndex < 0 || _selectedVertexIndex >= _ghost.Count)
            throw new InvalidOperationException("No waypoint is currently selected.");
        return ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_ghost)[_selectedVertexIndex];
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private int FindNearestVertex(Vector2 worldPos)
    {
        float threshold = RouteEditToolConstants.VertexPickRadius;
        float minDist   = threshold;
        int   nearest   = NoVertex;

        for (int i = 0; i < _ghost.Count; i++)
        {
            float dist = Vector2.Distance(ToCanvas(_ghost[i].Position), worldPos);
            if (dist < minDist) { minDist = dist; nearest = i; }
        }

        return nearest;
    }

    private int FindGloballyNearestVertex(Vector2 worldPos)
    {
        float minDist = float.MaxValue;
        int   nearest = NoVertex;

        for (int i = 0; i < _ghost.Count; i++)
        {
            float dist = Vector2.Distance(ToCanvas(_ghost[i].Position), worldPos);
            if (dist < minDist) { minDist = dist; nearest = i; }
        }

        return nearest;
    }

    /// <summary>
    /// Returns the index of the first segment [i, i+1] whose perpendicular distance to
    /// <paramref name="worldPos"/> is within <see cref="RouteEditToolConstants.VertexPickRadius"/>.
    /// Returns <c>-1</c> when no segment qualifies.
    /// </summary>
    private int FindNearestSegment(Vector2 worldPos)
    {
        float threshold = RouteEditToolConstants.VertexPickRadius;

        for (int i = 0; i < _ghost.Count - 1; i++)
        {
            var a = ToCanvas(_ghost[i].Position);
            var b = ToCanvas(_ghost[i + 1].Position);

            if (PointToSegmentDistance(worldPos, a, b) < threshold)
                return i;
        }

        return -1;
    }

    private static float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lenSq = Vector2.Dot(ab, ab);
        if (lenSq < float.Epsilon)
            return Vector2.Distance(p, a);

        float t = System.Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        return Vector2.Distance(p, a + t * ab);
    }

    private void CommitChanges()
    {
        _onCommit?.Invoke(_routeEntity, new List<RouteWaypoint>(_ghost));
        OnRouteCommitted?.Invoke(_routeEntity, new List<RouteWaypoint>(_ghost));
    }

    /// <summary>Converts a Cartesian ECS world position to 2D canvas (XZ plane).</summary>
    private static Vector2 ToCanvas(Vector3 pos) => new Vector2(pos.X, pos.Z);
}
