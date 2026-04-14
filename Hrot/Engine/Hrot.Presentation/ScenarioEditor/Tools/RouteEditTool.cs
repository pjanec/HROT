using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Map.Common.Components;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Hrot.ScenarioEditor.Tools;

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

    // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly Entity _routeEntity;
    private readonly RoutePlan _plan;
    private readonly Action<Entity, List<RouteWaypoint>> _onCommit;

    private MapCanvas? _canvas;

    private List<RouteWaypoint> _ghost = new();
    private int _selectedVertexIndex = NoVertex;

    private const int NoVertex = -1;

    // â”€â”€ Public observable state (for WaypointEditorPanel and tests) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Index of the currently selected ghost waypoint, or <c>-1</c> when none.
    /// </summary>
    public int SelectedVertexIndex => _selectedVertexIndex;

    /// <summary>Read-only view of the ghost waypoint list for assertions.</summary>
    public IReadOnlyList<RouteWaypoint> GhostWaypoints => _ghost;

    // â”€â”€ Vertex context menu state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// When <c>true</c>, a right-click landed on a vertex and the application should
    /// display a context menu for \"Insert Point\" / \"Delete Point\".
    /// Reset by <see cref="CloseVertexContextMenu"/>.
    /// </summary>
    public bool PendingVertexContextMenu { get; private set; }

    /// <summary>
    /// Index of the vertex that triggered the context menu.
    /// Only valid when <see cref="PendingVertexContextMenu"/> is <c>true</c>.
    /// </summary>
    public int ContextMenuVertexIndex { get; private set; }

    // â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Raised when the operator right-clicks to commit. The caller should persist the
    /// waypoints to the ECS component and publish a network update descriptor.
    /// </summary>
    public event Action<Entity, List<RouteWaypoint>>? OnRouteCommitted;

    // â”€â”€ Construction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <param name="routeEntity">Entity whose <see cref="RoutePlan"/> is being edited.</param>
    /// <param name="plan">The live <see cref="RoutePlan"/> component â€” only used at enter-time to seed the ghost.</param>
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

    // â”€â”€ IMapTool lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Input â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                // No vertex in range â€” check for segment insertion.
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
            int nearestVtx = FindNearestVertex(worldPos);
            if (nearestVtx >= 0)
            {
                // Right-click on a vertex â†’ open vertex context menu (insert/delete).
                _selectedVertexIndex   = nearestVtx;
                PendingVertexContextMenu = true;
                ContextMenuVertexIndex  = nearestVtx;
                return true;
            }

            // Right-click away from vertices â†’ commit and close.
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

    // â”€â”€ Public accessor for WaypointEditorPanel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Vertex context menu actions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Closes the vertex context menu without performing any action.</summary>
    public void CloseVertexContextMenu()
    {
        PendingVertexContextMenu = false;
    }

    /// <summary>
    /// Inserts a new waypoint after <see cref="ContextMenuVertexIndex"/>, inheriting
    /// <c>TargetSpeed</c> and <c>ExtensionJson</c> from the selected waypoint.
    /// The new waypoint is placed at the midpoint between the selected vertex and its successor.
    /// Clears <see cref="PendingVertexContextMenu"/> on completion.
    /// </summary>
    public void InsertWaypointAfterSelected()
    {
        int idx = ContextMenuVertexIndex;
        if (idx < 0 || idx >= _ghost.Count)
        {
            PendingVertexContextMenu = false;
            return;
        }

        int nextIdx = (idx + 1) % _ghost.Count;
        var a = ToCanvas(_ghost[idx].Position);
        var b = ToCanvas(_ghost[nextIdx].Position);
        var midCanvas = (a + b) * 0.5f;

        var inherited = _ghost[idx];
        var inserted  = new RouteWaypoint
        {
            Position      = new System.Numerics.Vector3(midCanvas.X, 0f, midCanvas.Y),
            TargetSpeed   = inherited.TargetSpeed,
            ExtensionJson = inherited.ExtensionJson,
        };
        _ghost.Insert(idx + 1, inserted);
        _selectedVertexIndex    = idx + 1;
        PendingVertexContextMenu = false;
    }

    /// <summary>
    /// Deletes the waypoint at <see cref="ContextMenuVertexIndex"/>.
    /// No-op when fewer than 3 waypoints remain (minimum viable route = 2, but keeping â‰Ą 2
    /// is enforced at authoring; editing retains at least 2).
    /// Clears <see cref="PendingVertexContextMenu"/> on completion.
    /// </summary>
    public void DeleteSelectedWaypoint()
    {
        int idx = ContextMenuVertexIndex;
        if (idx < 0 || idx >= _ghost.Count || _ghost.Count <= 2)
        {
            PendingVertexContextMenu = false;
            return;
        }

        _ghost.RemoveAt(idx);
        _selectedVertexIndex    = System.Math.Clamp(idx - 1, NoVertex, _ghost.Count - 1);
        PendingVertexContextMenu = false;
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
