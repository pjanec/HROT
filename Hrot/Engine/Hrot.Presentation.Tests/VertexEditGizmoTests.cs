using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="VertexEditGizmo"/> interaction state machine (GIZMOS1-T010).
/// </summary>
public class VertexEditGizmoTests : IDisposable
{
    // -- No-op IDebugDrawBuilder stub --
    private sealed class NullDraw : Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder
    {
        public void DrawLine(Vector3 s, Vector3 e, Rgba32 c, float t = 1f,
            SizeMode m = SizeMode.ScreenPixels,
            PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawLineGradient(Vector3 s, Vector3 e, Rgba32 sc, Rgba32 ec, float t = 1f,
            SizeMode m = SizeMode.ScreenPixels,
            PipelineTarget tg = PipelineTarget.All, byte l = 0, LineStyle style = LineStyle.Solid) { }
        public void DrawSphere(Vector3 c, float r, Rgba32 col,
            float thickness = 0f, SizeMode sm2 = SizeMode.WorldMeters,
            PipelineTarget tg = PipelineTarget.All, byte l = 0,
            Rgba32 fillColor = default, LineStyle style = LineStyle.Solid) { }
        public void DrawArrow(Vector3 f, Vector3 t, Rgba32 c, float h = 1f, byte l = 0) { }
        public void DrawText(float x, float y, Fdp.Core.FixedString32 t, Rgba32 c,
            CoordinateSpace sp = CoordinateSpace.World, byte l = 0) { }
        public void DrawTextLong(float x, float y, string t, Rgba32 c,
            CoordinateSpace sp = CoordinateSpace.World, byte l = 0) { }
        public void DrawEntityBadge(Entity e, Fdp.Core.FixedString32 rt,
            PipelineTarget tg = PipelineTarget.All) { }
        public void DrawEntityLocal(Entity a, Vector3 ls, Vector3 le,
            Rgba32 c, float t = 1f, byte l = 0) { }
        public void DrawEntityLocalInteractive(Entity a, Vector3 ls, Vector3 le,
            Rgba32 c, ushort sid, float t = 1f, byte l = 0) { }
    }

    private readonly EntityRepository _repo;
    private readonly Entity           _entity;
    private const long NetworkId = 42L;

    public VertexEditGizmoTests()
    {
        _repo = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_repo);
        _repo.RegisterManagedComponent<EditablePolyline>();

        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, default(SimTransform));
        _repo.AddComponent(_entity, new NetworkIdentity { Value = NetworkId });

        var poly = new EditablePolyline
        {
            Points = new List<Vector2>
            {
                new Vector2(10f, 10f),
                new Vector2(20f, 20f),
                new Vector2(30f, 10f),
            }
        };
        _repo.SetManagedComponent(_entity, poly);
    }

    public void Dispose() { }

    private VertexEditGizmo CreateGizmo()
        => new VertexEditGizmo(_repo, _entity, NetworkId, onRemove: () => { });

    private static GizmoPickToken Token(uint subElementId)
        => new GizmoPickToken { AnchorId = NetworkId, SubElementId = subElementId };

    // -- VEG-001 --

    /// <summary>
    /// OnInteractionStarted with SubElementId=2 selects vertex at index 1;
    /// a subsequent OnDragUpdate moves that vertex.
    /// </summary>
    [Fact]
    public void OnInteractionStarted_SetsActiveVertex()
    {
        using var gizmo = CreateGizmo();

        // SubElementId=2 -> vertex index 1.
        gizmo.OnInteractionStarted(Token(2), Vector3.Zero);
        gizmo.OnDragUpdate(new Vector3(99f, 77f, 0f));
        gizmo.OnCommit(Vector3.Zero);

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);

        // Index 1 was moved; indices 0 and 2 are unchanged.
        Assert.Equal(new Vector2(10f, 10f), poly.Points[0]);
        Assert.Equal(new Vector2(99f, 77f), poly.Points[1]);
        Assert.Equal(new Vector2(30f, 10f), poly.Points[2]);
    }

    // -- VEG-002 --

    /// <summary>
    /// After drag + OnCommit, the EditablePolyline in the ECS repo reflects the moved vertex.
    /// </summary>
    [Fact]
    public void OnCommit_WritesBackToEcs()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnDragUpdate(new Vector3(55f, 55f, 0f));
        gizmo.OnCommit(Vector3.Zero);

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(new Vector2(55f, 55f), poly.Points[0]);
        Assert.Equal(3, poly.Points.Count);
    }

    // -- VEG-003 --

    /// <summary>
    /// After drag + OnCancel, the EditablePolyline Points are unchanged from initial.
    /// </summary>
    [Fact]
    public void OnCancel_RevertsVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(2), Vector3.Zero); // vertex 1
        gizmo.OnDragUpdate(new Vector3(999f, 999f, 0f));
        gizmo.OnCancel();

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        // Points[1] must still be (20, 20).
        Assert.Equal(new Vector2(20f, 20f), poly.Points[1]);
    }

    // -- VEG-004 --

    /// <summary>
    /// OnMenuAction(1) after selecting vertex 0 inserts a midpoint; Points.Count increases by 1.
    /// </summary>
    [Fact]
    public void OnMenuAction_InsertAfter_AddsVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnMenuAction(1); // insert after

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(4, poly.Points.Count);
    }

    // -- VEG-005 --

    /// <summary>
    /// OnMenuAction(2) after selecting vertex 0 removes that vertex; Points.Count decreases by 1.
    /// </summary>
    [Fact]
    public void OnMenuAction_Delete_RemovesVertex()
    {
        using var gizmo = CreateGizmo();

        gizmo.OnInteractionStarted(Token(1), Vector3.Zero); // vertex 0
        gizmo.OnMenuAction(2); // delete

        var poly = ((ISimulationView)_repo).GetManagedComponentRO<EditablePolyline>(_entity);
        Assert.Equal(2, poly.Points.Count);
    }
}
