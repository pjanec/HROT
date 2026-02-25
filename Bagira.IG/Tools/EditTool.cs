using System;
using System.Collections.Generic;
using System.Numerics;
using Bagira.IG.Components;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Abstractions;
using ModuleHost.Core.Abstractions;
using Raylib_cs;

namespace Bagira.IG.Tools;

/// <summary>
/// Specialised map tool that allows operators to drag individual vertices of a
/// polyline overlay stored in an entity's <see cref="EditablePolyline"/> component.
///
/// Workflow:
/// <list type="number">
///   <item>
///     <b>Enter</b> — loads vertex positions from <see cref="EditablePolyline"/> into
///     an in-memory <em>ghost</em> list so the operator sees a live-updated preview.
///   </item>
///   <item>
///     <b>Left-click</b> — selects the nearest vertex within
///     <see cref="EditToolConstants.VertexPickRadiusWorldUnits"/>; no-op if no
///     vertex is within range.
///   </item>
///   <item>
///     <b>Drag</b> — moves the selected vertex in the ghost list.
///   </item>
///   <item>
///     <b>Right-click</b> — commits the ghost list back to the
///     <see cref="EditablePolyline"/> component (via the
///     <see cref="OnPolylineCommitted"/> event) and pops the tool.
///   </item>
/// </list>
///
/// All threshold and visual constants come from <see cref="EditToolConstants"/>
/// (§CODE-STANDARDS §1).
///
/// No allocations in the hover / drag hot path (§CODE-STANDARDS §4);
/// the ghost list is allocated once in <see cref="OnEnter"/> and reused.
/// </summary>
public class EditTool : IMapTool
{
    /// <inheritdoc/>
    public string Name => EditToolConstants.ToolName;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly Entity          _targetEntity;
    private readonly ISimulationView _view;

    private MapCanvas?    _canvas;
    private List<Vector2> _ghostPoints = new();
    private int           _selectedVertexIndex = EditTool.NoVertexSelected;

    private const int NoVertexSelected = -1;

    // ── Public observable state ───────────────────────────────────────────────

    /// <summary>
    /// Index of the vertex currently selected for dragging, or
    /// <c>-1</c> when no vertex is selected.
    /// Exposed for unit-test assertions.
    /// </summary>
    public int SelectedVertexIndex => _selectedVertexIndex;

    /// <summary>
    /// Read-only snapshot of the in-memory ghost vertex positions.
    /// Exposed for unit-test assertions; reflects drag updates before commit.
    /// </summary>
    public IReadOnlyList<Vector2> GhostPoints => _ghostPoints;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the operator right-clicks to commit the edited polyline.
    /// The first argument is the target entity; the second is the committed
    /// vertex list (a fresh copy owned by the caller).
    ///
    /// Callers should apply this list to the ECS component and optionally
    /// publish a network update command.
    /// </summary>
    public event Action<Entity, List<Vector2>>? OnPolylineCommitted;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <param name="targetEntity">
    /// The entity whose <see cref="EditablePolyline"/> is being edited.
    /// </param>
    /// <param name="view">
    /// Simulation view used to read the current polyline at enter-time.
    /// </param>
    public EditTool(Entity targetEntity, ISimulationView view)
    {
        _targetEntity = targetEntity;
        _view         = view ?? throw new ArgumentNullException(nameof(view));
    }

    // ── IMapTool lifecycle ────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Loads the current <see cref="EditablePolyline"/> vertex list into the
    /// ghost buffer.  Clears any previous selection.
    /// </remarks>
    public void OnEnter(MapCanvas canvas)
    {
        _canvas               = canvas;
        _selectedVertexIndex  = NoVertexSelected;
        _ghostPoints          = LoadGhostPoints();
    }

    /// <inheritdoc/>
    public void OnExit()
    {
        _canvas              = null;
        _selectedVertexIndex = NoVertexSelected;
    }

    /// <inheritdoc/>
    public void Update(float dt) { /* Stateless between frames. */ }

    // ── Input handling ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HandleClick(Vector2 worldPos, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _selectedVertexIndex = FindNearestVertex(worldPos);
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
        if (_selectedVertexIndex < 0 || _selectedVertexIndex >= _ghostPoints.Count)
            return false;

        _ghostPoints[_selectedVertexIndex] = worldPos;
        return true;
    }

    /// <inheritdoc/>
    public bool HandleHover(Vector2 worldPos) => false;

    /// <inheritdoc/>
    public void Draw(RenderContext ctx)
    {
        if (_ghostPoints.Count < EditTool.NoVertexSelected + 2)
            return;

        // Draw ghost polyline edges.
        for (int i = 0; i < _ghostPoints.Count - 1; i++)
        {
            var p1 = _ghostPoints[i];
            var p2 = _ghostPoints[i + 1];
            Raylib.DrawLineEx(p1, p2, EditToolConstants.VertexHandleRadiusWorldUnits, Color.Yellow);
        }

        // Draw vertex handles.
        for (int i = 0; i < _ghostPoints.Count; i++)
        {
            var pos    = _ghostPoints[i];
            bool sel   = i == _selectedVertexIndex;
            float r    = sel
                ? EditToolConstants.SelectedHandleRadiusWorldUnits
                : EditToolConstants.VertexHandleRadiusWorldUnits;
            Color col  = sel ? Color.Red : Color.White;
            Raylib.DrawCircle((int)pos.X, (int)pos.Y, r, col);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private List<Vector2> LoadGhostPoints()
    {
        if (_view.HasManagedComponent<EditablePolyline>(_targetEntity))
        {
            var polyline = _view.GetManagedComponentRO<EditablePolyline>(_targetEntity);
            return new List<Vector2>(polyline.Points);
        }
        return new List<Vector2>();
    }

    private int FindNearestVertex(Vector2 worldPos)
    {
        float threshold = EditToolConstants.VertexPickRadiusWorldUnits;
        float minDist   = threshold;
        int   nearest   = NoVertexSelected;

        for (int i = 0; i < _ghostPoints.Count; i++)
        {
            float dist = Vector2.Distance(_ghostPoints[i], worldPos);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = i;
            }
        }

        return nearest;
    }

    private void CommitChanges()
    {
        // Fire the event so callers (application code or tests) can persist the
        // changes to ECS / network.  Pass a fresh copy so the receiver owns it.
        OnPolylineCommitted?.Invoke(_targetEntity, new List<Vector2>(_ghostPoints));
    }
}
