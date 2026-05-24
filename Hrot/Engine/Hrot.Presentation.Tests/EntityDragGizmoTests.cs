using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="EntityDragGizmo"/> (EDG-001..EDG-006).
/// </summary>
public class EntityDragGizmoTests
{
    private readonly EntityRepository _repo;
    private readonly Entity           _entity;

    public EntityDragGizmoTests()
    {
        _repo = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_repo);
        _repo.RegisterComponent<VehicleState>();

        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
        _repo.AddComponent(_entity, new NetworkIdentity { Value = 55L });
    }

    // EDG-001: UpdateAndDraw emits a Box2D primitive with valid entity pick token.
    [Fact]
    public void UpdateAndDraw_EmitsSphereWithValidPickToken()
    {
        var gizmo   = new EntityDragGizmo(_repo, _entity);
        var buffer  = new DebugPrimitiveBuffer(capacity: 16);

        gizmo.UpdateAndDraw(new EntityRepository(), 0f, buffer);

        var frame = buffer.GetFrame();
        Assert.True(frame.Length >= 1);

        // Find the Box2D primitive with entity anchor (the pick hitbox).
        bool found = false;
        foreach (var prim in frame)
        {
            if (prim.Shape != DebugPrimitiveShape.Box2D) continue;
            var token = prim.GetPickToken();
            if (!token.IsValid) continue;
            Assert.Equal(_entity, token.Target);
            found = true;
            break;
        }
        Assert.True(found, "No Box2D with valid entity pick token found.");
    }

    // EDG-002: OnDragUpdate writes to SimTransform.Position.
    [Fact]
    public void OnDragUpdate_WritesToSimTransformPosition()
    {
        var gizmo = new EntityDragGizmo(_repo, _entity);
        gizmo.OnInteractionStarted(default, Vector3.Zero);

        var newPos = new Vector3(50f, 60f, 0f);
        gizmo.OnDragUpdate(newPos);

        var tf = _repo.GetComponent<SimTransform>(_entity);
        Assert.Equal(50f, tf.Position.X, precision: 3);
        Assert.Equal(60f, tf.Position.Y, precision: 3);
    }

    // EDG-003: OnCommit writes final position and fires OnDragCommitted.
    [Fact]
    public void OnCommit_WritesFinalPositionAndFiresCallback()
    {
        Entity? cbEntity = null;
        Vector2 cbPos    = default;

        var gizmo = new EntityDragGizmo(_repo, _entity);
        gizmo.OnDragCommitted += (e, p) => { cbEntity = e; cbPos = p; };
        gizmo.OnInteractionStarted(default, Vector3.Zero);

        var finalPos = new Vector3(100f, 200f, 0f);
        gizmo.OnCommit(finalPos);

        var tf = _repo.GetComponent<SimTransform>(_entity);
        Assert.Equal(100f, tf.Position.X, precision: 3);
        Assert.Equal(200f, tf.Position.Y, precision: 3);
        Assert.Equal(_entity, cbEntity);
        Assert.Equal(new Vector2(100f, 200f), cbPos);
    }

    // EDG-004: OnCancel restores original position.
    [Fact]
    public void OnCancel_RestoresOriginalPosition()
    {
        var gizmo = new EntityDragGizmo(_repo, _entity);
        gizmo.OnInteractionStarted(default, Vector3.Zero);

        gizmo.OnDragUpdate(new Vector3(999f, 999f, 0f));
        gizmo.OnCancel();

        var tf = _repo.GetComponent<SimTransform>(_entity);
        Assert.Equal(10f, tf.Position.X, precision: 3);
        Assert.Equal(20f, tf.Position.Y, precision: 3);
    }

    // EDG-005: OnDragUpdate on dead entity is a no-op (no crash).
    [Fact]
    public void OnDragUpdate_OnDeadEntity_IsNoOp()
    {
        var gizmo = new EntityDragGizmo(_repo, _entity);
        gizmo.OnInteractionStarted(default, Vector3.Zero);
        _repo.DestroyEntity(_entity);

        // Should not throw.
        gizmo.OnDragUpdate(new Vector3(50f, 50f, 0f));
    }

    // EDG-006: OnCommit resets VehicleState.Speed when component present.
    [Fact]
    public void OnDragUpdate_ResetsVehicleStateSpeed()
    {
        _repo.AddComponent(_entity, new VehicleState { Speed = 15f });
        var gizmo = new EntityDragGizmo(_repo, _entity);
        gizmo.OnInteractionStarted(default, Vector3.Zero);

        gizmo.OnDragUpdate(new Vector3(50f, 60f, 0f));

        var vs = _repo.GetComponent<VehicleState>(_entity);
        Assert.Equal(0f, vs.Speed);
    }

    // SC-GZ064-5: EntityDragGizmoDefinition.GizmoTypeId is non-zero and stable.
    [Fact]
    public void SC_GZ064_5_EntityDragGizmoDefinition_GizmoTypeId_NonZeroAndStable()
    {
        var def1 = new EntityDragGizmoDefinition();
        var def2 = new EntityDragGizmoDefinition();
        Assert.NotEqual(0u, def1.GizmoTypeId);
        Assert.Equal(def1.GizmoTypeId, def2.GizmoTypeId);
    }
}
