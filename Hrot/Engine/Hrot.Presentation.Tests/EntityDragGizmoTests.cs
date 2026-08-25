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

/// <summary>
/// ⭐⭐⭐ <b><c>AX-007</c> — the drag COMMITS through the same write router the rotate gizmo uses.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11 · ruling 32 *(drag belongs on this path)*.</para>
///
/// <para>⭐⭐ <b>These rails use a FAKE writer, and that is not a shortcut.</b> The real
/// <c>AttributeEntityComponentWriter</c> lives in <c>Hrot.Network.NED</c>, which this assembly ⛔ must not
/// reference — 📌 that constraint is precisely why <c>IEntityComponentWriter</c> moved to
/// <c>Fdp.Toolkits</c>. ⭐ The seam is what the gizmo depends on, so the seam is what it is railed against;
/// the router's own behaviour is railed where it lives *(<c>TheGateCannotBeForgottenTests</c>)* and the two
/// meet on a real cluster in <c>AttributeChangeRequestRoundTripTests</c>.</para>
/// </summary>
public class TheDragCommitsThroughTheWriteRouterTests
{
    private readonly EntityRepository _repo;
    private readonly Entity           _entity;

    public TheDragCommitsThroughTheWriteRouterTests()
    {
        _repo = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_repo);
        _repo.RegisterComponent<VehicleState>();

        _entity = _repo.CreateEntity();
        _repo.AddComponent(_entity, new SimTransform { Position = new Vector3(10f, 20f, 5f) });
        _repo.AddComponent(_entity, new NetworkIdentity { Value = 55L });

        var geo = new Fdp.Modules.Geographic.Transforms.WGS84Transform();
        geo.SetOrigin(52.520, 13.405, 0.0);
        _repo.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
    }

    /// <summary>⭐ Records what the gizmo asked for, and answers a route the test chooses.</summary>
    private sealed class FakeWriter : Fdp.Toolkit.Replication.Patching.IEntityComponentWriter
    {
        private readonly Fdp.Toolkit.Replication.Patching.EntityWriteRoute _route;
        public FakeWriter(Fdp.Toolkit.Replication.Patching.EntityWriteRoute route) => _route = route;

        public int Calls { get; private set; }
        public System.Collections.Generic.List<Fdp.Toolkit.Replication.Patching.EntityAttributeChange> Last { get; }
            = new();

        public Fdp.Toolkit.Replication.Patching.EntityWriteRoute Write(Entity entity, ushort attributeId, double value)
            => Write(entity, new[] { Fdp.Toolkit.Replication.Patching.EntityAttributeChange.Double(attributeId, value) });

        public Fdp.Toolkit.Replication.Patching.EntityWriteRoute Write(
            Entity entity,
            System.Collections.Generic.IReadOnlyList<Fdp.Toolkit.Replication.Patching.EntityAttributeChange> changes)
        {
            Calls++;
            Last.Clear();
            Last.AddRange(changes);
            return _route;
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>ONE call, carrying BOTH coordinates.</b>
    ///
    /// <para>⛔ Two single-attribute writes would reach the owner as two requests applied a round trip
    /// apart, landing the entity on a latitude the operator chose and a longitude they did not — see the
    /// interface's remarks. ⭐ Asserted as a COUNT plus the pair, because either half alone would pass a
    /// weaker rail.</para>
    /// </summary>
    [Fact]
    public void ACommitSendsGeoLatAndGeoLonAsOneChange()
    {
        var writer = new FakeWriter(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested);
        var gizmo  = new EntityDragGizmo(_repo, _entity, writer);

        gizmo.OnInteractionStarted(default, new Vector3(10f, 20f, 0f));
        gizmo.OnDragUpdate(new Vector3(210f, 170f, 0f));
        gizmo.OnCommit(new Vector3(210f, 170f, 0f));

        Assert.Equal(1, writer.Calls);
        Assert.Equal(2, writer.Last.Count);
        Assert.Contains(writer.Last, c => c.AttributeId == Fdp.Toolkit.Replication.Patching.AttributeIds.GeoLat);
        Assert.Contains(writer.Last, c => c.AttributeId == Fdp.Toolkit.Replication.Patching.AttributeIds.GeoLon);
    }

    /// <summary>
    /// ⭐⭐ <b>The LIVE drag is NOT routed</b> — it is a local preview, and one request per mouse-move is
    /// exactly what the design forbids. ⛔ A rail on the commit alone would not catch a future edit that
    /// moved the routing into <c>OnDragUpdate</c>.
    /// </summary>
    [Fact]
    public void TheLivePreviewNeverPublishesARequest()
    {
        var writer = new FakeWriter(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested);
        var gizmo  = new EntityDragGizmo(_repo, _entity, writer);

        gizmo.OnInteractionStarted(default, new Vector3(10f, 20f, 0f));
        gizmo.OnDragUpdate(new Vector3(60f, 70f, 0f));
        gizmo.OnDragUpdate(new Vector3(80f, 90f, 0f));
        gizmo.OnDragUpdate(new Vector3(99f, 99f, 0f));

        Assert.Equal(0, writer.Calls);

        // ⭐ …and the preview DID move the entity locally, so "no request" is not "nothing happened".
        Assert.NotEqual(new Vector3(10f, 20f, 5f), _repo.GetComponent<SimTransform>(_entity).Position);
    }

    /// <summary>
    /// ⭐⭐ <b>A REFUSED write puts the preview back.</b> ⛔ Leaving the dragged position on screen after
    /// nobody accepted it shows the operator a move that never happened — the same *"accepted and silently
    /// discarded"* shape this programme keeps finding.
    /// </summary>
    [Fact]
    public void ARefusedCommitRestoresTheOriginalPosition()
    {
        var writer = new FakeWriter(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused);
        var gizmo  = new EntityDragGizmo(_repo, _entity, writer);

        var original = _repo.GetComponent<SimTransform>(_entity).Position;

        gizmo.OnInteractionStarted(default, new Vector3(10f, 20f, 0f));
        gizmo.OnDragUpdate(new Vector3(210f, 170f, 0f));
        gizmo.OnCommit(new Vector3(210f, 170f, 0f));

        var after = _repo.GetComponent<SimTransform>(_entity).Position;
        Assert.Equal(original.X, after.X, 3);
        Assert.Equal(original.Y, after.Y, 3);
    }

    /// <summary>
    /// ⭐⭐ <b>The Z coordinate survives a 2D drag.</b> 📐 The gizmo commits geodetic lat/lon derived from a
    /// point whose altitude it takes from the CURRENT transform — ⛔ taking it from the drag would silently
    /// flatten every airborne entity to the map plane.
    /// </summary>
    [Fact]
    public void AHorizontalDragDoesNotFlattenAltitude()
    {
        var writer = new FakeWriter(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested);
        var geo    = _repo.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()!;
        var gizmo  = new EntityDragGizmo(_repo, _entity, writer);

        gizmo.OnInteractionStarted(default, new Vector3(10f, 20f, 0f));
        gizmo.OnDragUpdate(new Vector3(210f, 170f, 0f));
        gizmo.OnCommit(new Vector3(210f, 170f, 0f));

        // ⭐ Re-derive what the gizmo must have converted: the drag XY at the entity's own altitude.
        var expected = geo.ToGeodetic(new Vector3(210f, 170f, 5f));

        double lat = 0, lon = 0;
        foreach (var c in writer.Last)
        {
            if (c.AttributeId == Fdp.Toolkit.Replication.Patching.AttributeIds.GeoLat) lat = c.Value.DoubleValue;
            if (c.AttributeId == Fdp.Toolkit.Replication.Patching.AttributeIds.GeoLon) lon = c.Value.DoubleValue;
        }

        Assert.Equal(expected.Item1, lat, 9);
        Assert.Equal(expected.Item2, lon, 9);
    }

    /// <summary>
    /// ⚠ <b>With NO writer the gizmo keeps the direct write it always had</b> — the single-node editor
    /// shape. ⭐ Railed so the fallback is a stated contract rather than an accident of a null check.
    /// </summary>
    [Fact]
    public void WithNoWriterTheCommitStillWritesLocally()
    {
        var gizmo = new EntityDragGizmo(_repo, _entity);

        gizmo.OnInteractionStarted(default, new Vector3(10f, 20f, 0f));
        gizmo.OnDragUpdate(new Vector3(210f, 170f, 0f));
        gizmo.OnCommit(new Vector3(210f, 170f, 0f));

        var after = _repo.GetComponent<SimTransform>(_entity).Position;
        Assert.Equal(210f, after.X, 3);
        Assert.Equal(170f, after.Y, 3);
    }
}
