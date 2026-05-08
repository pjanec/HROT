using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Tools;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.4.4: <see cref="EditTool"/>.
///
/// Validates:
/// <list type="bullet">
///   <item><see cref="EditTool.OnEnter"/> loads ghost points from the entity's
///   <see cref="EditablePolyline"/> component.</item>
///   <item>Left-click selects the nearest vertex within pick radius.</item>
///   <item>Left-click far from all vertices leaves <see cref="EditTool.SelectedVertexIndex"/>
///   at â’1 (no selection).</item>
///   <item>Drag moves the ghost point of the selected vertex.</item>
///   <item>Drag without a selected vertex returns <c>false</c>.</item>
///   <item>Right-click fires <see cref="EditTool.OnPolylineCommitted"/> with the
///   correct entity and committed points.</item>
///   <item>The committed list is a fresh copy (not the internal ghost reference).</item>
/// </list>
///
/// No DDS or Raylib window context required.  The <c>Draw()</c> method is not
/// exercised â€” it requires a Raylib window.  <see cref="EditTool.OnEnter"/> is
/// called with a <c>null</c> canvas reference (null-safe internally).
/// </summary>
public class EditToolTests
{
    // â”€â”€ Test constants (Â§CODE-STANDARDS Â§1) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // Polyline vertices
    private static readonly Vector2 Vertex0 = new Vector2(100f, 100f);
    private static readonly Vector2 Vertex1 = new Vector2(200f, 200f);
    private static readonly Vector2 Vertex2 = new Vector2(300f, 150f);

    // A click position very close to Vertex1 (within pick radius).
    private static readonly Vector2 NearVertex1 = new Vector2(
        Vertex1.X + EditToolConstants.VertexPickRadiusWorldUnits * 0.5f,
        Vertex1.Y);

    // A click position far from all vertices (outside pick radius).
    private static readonly Vector2 FarFromAll = new Vector2(700f, 700f);

    // Drag destination used in drag tests.
    private static readonly Vector2 DragTarget = new Vector2(250f, 250f);

    // â”€â”€ World factory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<EditablePolyline>();
        return repo;
    }

    /// <summary>
    /// Creates an entity with an <see cref="EditablePolyline"/> containing the
    /// three standard test vertices.
    /// </summary>
    private static Entity CreatePolylineEntity(EntityRepository repo)
    {
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new EditablePolyline
        {
            Points = new List<Vector2> { Vertex0, Vertex1, Vertex2 },
        });
        return entity;
    }

    /// <summary>
    /// Creates an <see cref="EditTool"/> for the given entity and calls OnEnter
    /// with a null canvas (null-safe via the nullable <c>MapCanvas?</c> field).
    /// </summary>
    private static EditTool CreateAndEnter(EntityRepository repo, Entity entity)
    {
        var tool = new EditTool(entity, (ISimulationView)repo);
        tool.OnEnter(null!); // canvas is unused in headless tests
        return tool;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // OnEnter â€” ghost point loading
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// After <see cref="EditTool.OnEnter"/>, the ghost point list must contain the
    /// same positions as the entity's <see cref="EditablePolyline.Points"/>.
    /// </summary>
    [Fact]
    public void OnEnter_LoadsGhostPointsFromEditablePolyline()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        Assert.Equal(3, tool.GhostPoints.Count);
        Assert.Equal(Vertex0, tool.GhostPoints[0]);
        Assert.Equal(Vertex1, tool.GhostPoints[1]);
        Assert.Equal(Vertex2, tool.GhostPoints[2]);
    }

    /// <summary>
    /// If the entity has no <see cref="EditablePolyline"/> the ghost list must be
    /// empty (not null).
    /// </summary>
    [Fact]
    public void OnEnter_NoPolyline_GhostPointsEmpty()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity(); // no EditablePolyline added
        var tool   = CreateAndEnter(repo, entity);

        Assert.NotNull(tool.GhostPoints);
        Assert.Empty(tool.GhostPoints);
    }

    /// <summary>
    /// OnEnter must reset SelectedVertexIndex to â’1 (no selection).
    /// </summary>
    [Fact]
    public void OnEnter_ResetsSelectedVertexIndex()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        Assert.Equal(-1, tool.SelectedVertexIndex);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Left-click â€” vertex selection
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// Hovering near a vertex within pick radius must select that vertex.
    /// </summary>
    [Fact]
    public void HandleHover_NearVertex1_SelectsVertex1()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleHover(NearVertex1);

        Assert.Equal(1, tool.SelectedVertexIndex);
    }

    /// <summary>
    /// Hovering far from all vertices must leave SelectedVertexIndex at â’1.
    /// </summary>
    [Fact]
    public void HandleHover_FarFromAll_NoSelection()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleHover(FarFromAll);

        Assert.Equal(-1, tool.SelectedVertexIndex);
    }

    /// <summary>
    /// Left-click must return <c>true</c> regardless of whether a vertex was hit.
    /// </summary>
    [Fact]
    public void HandleClick_LeftClick_AlwaysReturnsTrue()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        bool hit  = tool.HandleClick(NearVertex1, MapMouseButton.Left);
        bool miss = tool.HandleClick(FarFromAll,  MapMouseButton.Left);

        Assert.True(hit);
        Assert.True(miss);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Drag â€” vertex movement
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// Dragging while a vertex is selected must move the corresponding ghost point
    /// to the supplied world position.
    /// </summary>
    [Fact]
    public void HandleDrag_WithSelectedVertex_MovesGhostPoint()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleClick(NearVertex1, MapMouseButton.Left); // select vertex 1
        tool.HandleDrag(DragTarget, Vector2.Zero);

        Assert.Equal(DragTarget, tool.GhostPoints[1]);
    }

    /// <summary>
    /// Dragging with no prior hover/click must auto-select the nearest vertex within
    /// pick radius and return <c>true</c>.  This is the direct click-and-drag case.
    /// </summary>
    [Fact]
    public void HandleDrag_NoExplicitSelection_AutoSelectsNearestAndReturnsTrue()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        // DragTarget is closer to Vertex1 than to any other vertex.
        bool result = tool.HandleDrag(DragTarget, Vector2.Zero);

        Assert.True(result);
        Assert.Equal(1, tool.SelectedVertexIndex);
        Assert.Equal(DragTarget, tool.GhostPoints[1]);
    }

    /// <summary>
    /// Dragging with a selected vertex must return <c>true</c>.
    /// </summary>
    [Fact]
    public void HandleDrag_WithSelectedVertex_ReturnsTrue()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleClick(NearVertex1, MapMouseButton.Left);
        bool result = tool.HandleDrag(DragTarget, Vector2.Zero);

        Assert.True(result);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Right-click â€” commit
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// A right-click must fire <see cref="EditTool.OnPolylineCommitted"/> exactly once.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_FiresOnPolylineCommittedOnce()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        int callCount = 0;
        tool.OnPolylineCommitted += (_, _) => callCount++;

        tool.HandleClick(Vector2.Zero, MapMouseButton.Right);

        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// The <see cref="EditTool.OnPolylineCommitted"/> event must supply the correct
    /// target entity.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_CommitEventHasCorrectEntity()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        Entity? committed = null;
        tool.OnPolylineCommitted += (e, _) => committed = e;

        tool.HandleClick(Vector2.Zero, MapMouseButton.Right);

        Assert.Equal(entity, committed);
    }

    /// <summary>
    /// The committed point list must match the current ghost points (including any
    /// drag modifications applied before the right-click).
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_CommitEventHasCurrentGhostPoints()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        // Drag vertex 1 to a new position (hover first to select, then drag).
        tool.HandleHover(NearVertex1);
        tool.HandleDrag(DragTarget, Vector2.Zero);

        List<Vector2>? committed = null;
        tool.OnPolylineCommitted += (_, pts) => committed = pts;

        tool.HandleClick(Vector2.Zero, MapMouseButton.Right);

        Assert.NotNull(committed);
        Assert.Equal(DragTarget, committed![1]);
    }

    /// <summary>
    /// The committed list must be a fresh copy â€” modifying it must not alter the
    /// internal ghost state.
    /// </summary>
    [Fact]
    public void HandleClick_RightClick_CommittedListIsIndependentCopy()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        List<Vector2>? committed = null;
        tool.OnPolylineCommitted += (_, pts) => committed = pts;

        tool.HandleClick(Vector2.Zero, MapMouseButton.Right);

        Vector2 original = tool.GhostPoints[0];
        committed![0] = new Vector2(9999f, 9999f);

        // Ghost points must be unchanged.
        Assert.Equal(original, tool.GhostPoints[0]);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Vertex context menu â€” right-click on a vertex
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// Right-clicking within pick radius of a vertex must set
    /// <see cref="EditTool.PendingVertexContextMenu"/> to <c>true</c> instead of
    /// committing the polyline.
    /// </summary>
    [Fact]
    public void RightClickOnVertex_SetsPendingVertexContextMenu()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        int commitCount = 0;
        tool.OnPolylineCommitted += (_, _) => commitCount++;

        tool.HandleClick(NearVertex1, MapMouseButton.Right);

        Assert.True(tool.PendingVertexContextMenu);
        Assert.Equal(0, commitCount); // must NOT commit
    }

    /// <summary>
    /// Right-clicking AWAY from any vertex should commit immediately (existing behaviour).
    /// </summary>
    [Fact]
    public void RightClickFarFromVertices_CommitsImmediately()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        int commitCount = 0;
        tool.OnPolylineCommitted += (_, _) => commitCount++;

        tool.HandleClick(FarFromAll, MapMouseButton.Right);

        Assert.Equal(1, commitCount);
        Assert.False(tool.PendingVertexContextMenu);
    }

    /// <summary>
    /// <see cref="EditTool.ContextMenuVertexIndex"/> must equal the vertex index
    /// of the vertex that was right-clicked.
    /// </summary>
    [Fact]
    public void RightClickOnVertex_SetsContextMenuVertexIndex()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleClick(NearVertex1, MapMouseButton.Right);

        Assert.Equal(1, tool.ContextMenuVertexIndex);
    }

    // â”€â”€ Insert point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// <see cref="EditTool.InsertPointAfterSelected"/> must add one vertex, placed at the
    /// midpoint between vertex[N] and vertex[N+1], and clear PendingVertexContextMenu.
    /// </summary>
    [Fact]
    public void InsertPointAfterSelected_AddsVertexAtMidpoint()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleClick(NearVertex1, MapMouseButton.Right); // opens ctx menu for vertex 1
        int countBefore = tool.GhostPoints.Count;

        tool.InsertPointAfterSelected();

        Assert.Equal(countBefore + 1, tool.GhostPoints.Count);
        Assert.False(tool.PendingVertexContextMenu);

        // New vertex is midpoint of Vertex1 (index 1) and Vertex2 (index 2).
        var expectedMid = (Vertex1 + Vertex2) * 0.5f;
        Assert.Equal(expectedMid.X, tool.GhostPoints[2].X, precision: 2);
        Assert.Equal(expectedMid.Y, tool.GhostPoints[2].Y, precision: 2);
    }

    // â”€â”€ Delete point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// <see cref="EditTool.DeleteSelectedPoint"/> must remove the vertex at
    /// <see cref="EditTool.ContextMenuVertexIndex"/> and clear PendingVertexContextMenu.
    /// </summary>
    [Fact]
    public void DeleteSelectedPoint_RemovesVertex()
    {
        // Need 4 vertices so the polygon remains valid (â‰Ą 3) after deletion.
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new EditablePolyline
        {
            Points = new List<Vector2> { Vertex0, Vertex1, Vertex2, new Vector2(400f, 100f) },
        });
        var tool = new EditTool(entity, (ISimulationView)repo);
        tool.OnEnter(null!);

        tool.HandleClick(NearVertex1, MapMouseButton.Right); // ctx menu for vertex 1
        tool.DeleteSelectedPoint();

        Assert.Equal(3, tool.GhostPoints.Count);
        Assert.False(tool.PendingVertexContextMenu);
        // Vertex at index 1 is now Vertex2 (old index 2).
        Assert.Equal(Vertex2, tool.GhostPoints[1]);
    }

    /// <summary>
    /// Attempting to delete a vertex when only 3 remain must be a no-op
    /// (minimum viable polygon preserved).
    /// </summary>
    [Fact]
    public void DeleteSelectedPoint_WhenOnly3Points_IsNoOp()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo); // exactly 3 points
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleClick(NearVertex1, MapMouseButton.Right);
        tool.DeleteSelectedPoint();

        Assert.Equal(3, tool.GhostPoints.Count); // still 3
        Assert.False(tool.PendingVertexContextMenu);
    }

    // â”€â”€ Close context menu â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// <see cref="EditTool.CloseVertexContextMenu"/> must clear
    /// <see cref="EditTool.PendingVertexContextMenu"/> without committing.
    /// </summary>
    [Fact]
    public void CloseVertexContextMenu_ClearsPendingFlag()
    {
        var repo   = CreateRepo();
        var entity = CreatePolylineEntity(repo);
        var tool   = CreateAndEnter(repo, entity);

        tool.HandleClick(NearVertex1, MapMouseButton.Right);
        Assert.True(tool.PendingVertexContextMenu);

        int commitCount = 0;
        tool.OnPolylineCommitted += (_, _) => commitCount++;

        tool.CloseVertexContextMenu();

        Assert.False(tool.PendingVertexContextMenu);
        Assert.Equal(0, commitCount);
    }
}
