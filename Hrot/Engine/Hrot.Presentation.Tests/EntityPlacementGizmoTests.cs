using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="EntityPlacementGizmo"/> (EPG-001..EPG-006).
///
/// Tests exercise the gizmo directly via <see cref="IEntityStatefulGizmo"/> method calls
/// (PlacementCanvasBridge was deleted in Phase 3, BATCH-29).
/// No Raylib window context is required.
/// </summary>
public class EntityPlacementGizmoTests
{
    // -- Test constants ---------------------------------------------------

    private const long  TestTkbType = 202L;
    private const float ClickX      = 1234.5f;
    private const float ClickY      = 5678.9f;

    // -- Helper -----------------------------------------------------------

    private static (List<SpawnEntityCommand> captured, EntityPlacementGizmo gizmo)
        CreateGizmo(long tkbType = 202L, string? initialPropertiesJson = null)
    {
        var captured = new List<SpawnEntityCommand>();
        var gizmo = new EntityPlacementGizmo(
            onEntityCreated:       cmd => captured.Add(cmd),
            tkbType:               tkbType,
            initialPropertiesJson: initialPropertiesJson);
        return (captured, gizmo);
    }

    // ⭐ CE-1065 — the ghost is HALF the size it was (user, 2026-10-14: "make the entity placement … gizmo 2 times smaller"):
    //   a 7.5 m sphere (world metres — a 15 m translucent ball hid the scene in 3-D), its labels half as far below the cursor.
    [Fact]
    public void CE1065_TheGhost_IsHalfTheOldSize()
    {
        var (_, gizmo) = CreateGizmo();
        gizmo.OnDragUpdate(new Vector3(100f, 200f, 0f));
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.UpdateAndDraw(new Fdp.Core.EntityRepository(), 0f, buffer);
        float radius = -1f;
        foreach (var p in buffer.GetFrame())
            if (p.Shape == DebugPrimitiveShape.Sphere) radius = p.SphereRadius;
        Assert.Equal(3f, radius);   // CE-1337
    }

    // ⭐ CE-259ax G2 — the resolver was accepted and DROPPED (`_ = _nameResolver`). It now rides on the command as an EntityInfo.
    [Fact]
    public void CE259ax_G2_TheNameResolver_IsHonoured_NotDropped()
    {
        var captured = new List<SpawnEntityCommand>();
        var gizmo = new EntityPlacementGizmo(cmd => captured.Add(cmd), TestTkbType, nameResolver: () => "Bravo-3");

        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(1f, 2f, 0f));

        var cmd = Assert.Single(captured);
        var info = Assert.Single(cmd.InitialComponents!.OfType<Fdp.Core.EntityInfo>());
        Assert.Equal("Bravo-3", info.Name.ToString());
    }

    // Non-vacuity: without a resolver the gizmo adds nothing (the adapter seeds the TKB-default name).
    [Fact]
    public void CE259ax_G2_WithoutAResolver_TheGizmoAddsNoEntityInfo()
    {
        var (captured, gizmo) = CreateGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(1f, 2f, 0f));
        Assert.True(Assert.Single(captured).InitialComponents == null);
    }

    // -- EPG-001: left-click publishes exactly one command ----------------

    [Fact]
    public void LeftClick_WritesExactlyOneCommand()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Single(captured);
    }

    // -- EPG-002: left-click command carries correct TKB type -------------

    [Fact]
    public void LeftClick_CommandHasCorrectTkbType()
    {
        var (captured, gizmo) = CreateGizmo(tkbType: TestTkbType);

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Equal(TestTkbType, captured[0].TkbType);
    }

    // -- EPG-003: left-click command carries click position ---------------

    [Fact]
    public void LeftClick_CommandHasInitialTransformMatchingClickPosition()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.True(captured[0].InitialTransform.HasValue);
        Assert.Equal(ClickX, captured[0].InitialTransform!.Value.Position.X, precision: 2);
        Assert.Equal(ClickY, captured[0].InitialTransform!.Value.Position.Y, precision: 2);
    }

    // -- EPG-004: left-click command has non-empty RequestId --------------

    [Fact]
    public void LeftClick_CommandHasNonEmptyRequestId()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.NotEqual(Guid.Empty, captured[0].RequestId);
    }

    // -- EPG-005: right-click does not publish any command ----------------

    [Fact]
    public void RightClick_DoesNotPublish()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Right, isPressed: true, new Vector3(ClickX, ClickY, 0f));

        Assert.Empty(captured);
    }

    // -- EPG-006: initialAttributesJson is forwarded verbatim -------------

    [Fact]
    public void InitialAttributesJson_ForwardedVerbatim()
    {
        const string json = "{\"affiliation\":\"FORCE_FRIENDLY\"}";
        var (captured, gizmo) = CreateGizmo(initialPropertiesJson: json);

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Equal(json, captured[0].InitialAttributesJson);
    }

    // -- CE-1017 S2 (Add Entity, the UI half of the placement tool) ---------

    [Fact]
    public void CE1017_APlacedEntity_FacesNorth()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        // yaw +90° about Z turns the transform's forward (+X, east) to +Y (north)
        var forward = Vector3.Transform(Vector3.UnitX, captured[0].InitialTransform!.Value.Rotation);
        Assert.Equal(0f, forward.X, precision: 4);
        Assert.Equal(1f, forward.Y, precision: 4);
    }

    [Fact]
    public void CE1017_APlacedEntity_AsksForTheGroundLevel()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Equal(Fdp.Toolkit.NetworkSpawning.SpawnHeight.OnGround, captured[0].SpawnHeight);
    }

    [Fact]
    public void CE1017_AShiftClick_PlacesLikeAClick()
    {
        var (captured, gizmo) = CreateGizmo();

        gizmo.OnMouseEvent(MapMouseButton.Left | MapMouseButton.ShiftMask, isPressed: false, new Vector3(ClickX, ClickY, 0f));

        Assert.Single(captured);
    }

    [Fact]
    public void CE1017_AlwaysMulti_EachClickPlaces_AndOnlyRightClickOrEscEnds()
    {
        var captured = new List<SpawnEntityCommand>();
        int removed = 0;
        var gizmo = new EntityPlacementGizmo(cmd => captured.Add(cmd), TestTkbType,
            autoPopOnPlace: false, onRemove: () => removed++);

        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(1, 1, 0));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(2, 2, 0));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(3, 3, 0));
        Assert.Equal(3, captured.Count);
        Assert.Equal(0, removed);

        gizmo.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Escape, isPressed: true);
        Assert.Equal(1, removed);
    }

    // ═══════════════ ⭐ CE-4309 (Map Interaction P3, DESIGN_Map_Interaction.md D16 / Q3 / Q4) — airborne by type, the 2-D level, the heading drag ═══════════════

    private sealed class StackedFloors : Fdp.Toolkit.World.IWorldQuery
    {
        // three surfaces above the ground under x < 0 (a 3-storey house), only the ground elsewhere
        public float GroundHeightAt(float x, float y) => 0f;
        public float SurfaceZ(float x, float y, float zHint) => 0f;
        public IReadOnlyList<float> SurfacesAt(float x, float y, out int groundIndex) { groundIndex = 0; return x < 0 ? new[] { 0f, 3f, 6f, 9f } : new[] { 0f }; }
        public float ResolveLevel(float x, float y, int level) => level * 3f;
        public bool TryStandAt(float x, float y, float zHint, out float z) { z = 0f; return true; }
        public bool SightBlocked(Vector3 from, Vector3 to) => false;
        public void Trace(Vector3 from, Vector3 to, Fdp.Toolkit.World.TracePurpose purpose, List<Fdp.Toolkit.World.TraceCrossing> into) { }
    }

    /// <summary>⭐ CE-4309 — a type that FLIES is placed in the air: AboveLevel with its height (the A-10C's 300 m, CE-4272 measured it flies on from there); Shift places it
    /// on the ground; a ground type stays on the ground. ✅ Red-proof: <c>Resolve</c> ignoring <see cref="PlacementSession.AirborneHeight"/> ⇒ OnGround ⇒ red.</summary>
    [Fact]
    public void CE4309_AFlyingType_IsPlacedInTheAir_ShiftPlacesItOnTheGround()
    {
        var (captured, gizmo) = CreateGizmo();
        gizmo.Session.SetType(300f);
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(10f, 20f, 0f));
        var air = captured.Single();
        Assert.Equal(new Fdp.Toolkit.NetworkSpawning.SpawnHeight(Fdp.Toolkit.NetworkSpawning.SpawnHeightMode.AboveLevel, 0), air.SpawnHeight);
        Assert.Equal(300f, air.InitialTransform!.Value.Position.Z);
        Assert.Equal(300f, air.SpawnHeight!.Value.Apply(0f, air.InitialTransform!.Value.Position.Z));   // born 300 m over the ground

        gizmo.OnMouseEvent(MapMouseButton.Left | MapMouseButton.ShiftMask, false, new Vector3(10f, 20f, 0f));
        Assert.Equal(Fdp.Toolkit.NetworkSpawning.SpawnHeight.OnGround, captured[1].SpawnHeight);       // parked

        var (ground, tank) = CreateGizmo();
        tank.OnMouseEvent(MapMouseButton.Left, false, new Vector3(10f, 20f, 0f));
        Assert.Equal(Fdp.Toolkit.NetworkSpawning.SpawnHeight.OnGround, ground.Single().SpawnHeight);
        gizmo.Session.SetType(300f, 150f);
        Assert.Equal(("airborne · 300 m over the ground · 150 m/s", "Shift: parked on the ground"), gizmo.Session.Hint("click: place"));   // the mockup's words (D)
    }

    /// <summary>⭐ CE-4309 — in 2-D, where several surfaces are stacked under the cursor, PgUp / PgDn choose the level and the hint says so; elsewhere there is no choice.
    /// ✅ Red-proof: <c>Key</c> not stepping on PageUp ⇒ level 0 ⇒ red.</summary>
    [Fact]
    public void CE4309_OnAStackedPlace_PgUpPgDn_ChooseTheLevel()
    {
        var s = new PlacementSession();
        var house = new StackedFloors();
        s.Hover(house, new Vector3(-5f, 0f, 0f));
        Assert.Equal(4, s.LevelCount);
        Assert.Equal(("level 0 of 3   PgUp / PgDn", "drag: heading · right-click / Esc: done"), s.Hint("click: place"));   // the mockup's words (C)
        Assert.True(s.Key(PlacementSession.KeyPageUp)); s.Key(PlacementSession.KeyPageUp);
        Assert.Equal(2, s.Level);
        var (_, height, _) = s.Resolve(new Vector3(-5f, 0f, 0f), shift: false, levelOf3D: null);
        Assert.Equal(new Fdp.Toolkit.NetworkSpawning.SpawnHeight(Fdp.Toolkit.NetworkSpawning.SpawnHeightMode.OnLevel, 2), height);
        s.Key(PlacementSession.KeyPageUp); s.Key(PlacementSession.KeyPageUp);
        Assert.Equal(3, s.Level);                                                                        // clamped at the top
        s.Hover(house, new Vector3(5f, 0f, 0f));
        Assert.Equal((1, 0), (s.LevelCount, s.Level));                                                    // open ground: no choice
        Assert.Equal(("click: place", (string?)null), s.Hint("click: place"));
        var (_, threeD, _) = s.Resolve(new Vector3(-5f, 0f, 6f), shift: false, levelOf3D: 2);
        Assert.Equal(2, threeD.Level);                                                                    // a 3-D click carries its own level
    }

    /// <summary>⭐ CE-4309 — press, drag, release: the entity stands at the PRESS point facing the drag; a plain click faces north. ✅ Red-proof: the drag ignored ⇒ north ⇒ red.</summary>
    [Fact]
    public void CE4309_ADrag_SetsTheHeading_AClickFacesNorth()
    {
        var (captured, gizmo) = CreateGizmo();
        gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(100f, 100f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(100f, 120f, 0f));     // dragged 20 m north… 
        gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(0f, 0f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(30f, 0f, 0f));        // …and 30 m east
        var north = captured[0].InitialTransform!.Value; var east = captured[1].InitialTransform!.Value;
        Assert.Equal(new Vector2(100f, 100f), new Vector2(north.Position.X, north.Position.Y));                  // at the press point
        Assert.True(Quaternion.Dot(PlacementSession.FacingNorth, north.Rotation) > 0.999f);
        Assert.True(Math.Abs(Quaternion.Dot(Quaternion.Identity, east.Rotation)) > 0.999f, "a drag east faces east (yaw 0)");
        gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(5f, 5f, 0f));
        gizmo.OnMouseEvent(MapMouseButton.Left, false, new Vector3(6f, 5f, 0f));         // 1 m: a click
        Assert.True(Quaternion.Dot(PlacementSession.FacingNorth, captured[2].InitialTransform!.Value.Rotation) > 0.999f);
        Assert.Equal(6f, captured[2].InitialTransform!.Value.Position.X);
    }
}
