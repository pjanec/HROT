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

    // ⭐ B-17 P7 ① — the ghost is no longer a ball (CE-1065 / CE-1337 made the sphere 3 m, then P7 removed it): it is the type's FOOTPRINT. With no TKB database the rule's fallback (5 m × 2.5 m) stands.
    [Fact]
    public void CE1065_TheGhost_HasNoBall_ItIsTheFallbackFootprint_WhenTheTypeIsUnknown()
    {
        var (_, gizmo) = CreateGizmo();
        gizmo.OnDragUpdate(new Vector3(100f, 200f, 0f));
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.UpdateAndDraw(new Fdp.Core.EntityRepository(), 0f, buffer);
        Assert.DoesNotContain(buffer.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Sphere);
        var box = Assert.Single(buffer.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Box2D);
        Assert.Equal(EntityFootprint.DefaultLengthMetres * 0.5f, box.BoxExtentX);
        Assert.Equal(EntityFootprint.DefaultLengthMetres * EntityFootprint.DefaultWidthFactor * 0.5f, box.BoxExtentY);
    }

    private static (Fdp.Core.EntityRepository Repo, Fdp.Interfaces.TkbTemplate Tank, Fdp.Interfaces.TkbTemplate Jet) RepoWithTemplates()
    {
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        var tank = new Fdp.Interfaces.TkbTemplate("Tank", 777);
        tank.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.Tank, Length = 8f, Width = 3.4f });
        var jet = new Fdp.Interfaces.TkbTemplate("Jet", 778);
        jet.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.VehicleParametersDto { VehicleClass = global::CarKinem.Core.VehicleClass.PersonalCar, Length = 15f, Width = 17f });
        jet.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.FlightParamsDto { HoverHeightM = 300f, CruiseSpeedMps = 200f });
        db.Register(tank); db.Register(jet);
        var repo = new Fdp.Core.EntityRepository();
        repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        return (repo, tank, jet);
    }

    /// <summary>
    /// ⭐ B-17 P7 ① — the ghost is the type's FOOTPRINT, by <see cref="EntityFootprint"/>'s ONE rule read from the template (the same length × width the 2-D symbol and the pick area use): a translucent (α100), DASHED-outline Box2D in world metres, turned to the heading
    /// (north until a drag), plus ONE small notch triangle at the front. A heading drag turns the box and moves the notch. 🔴 Red-proof (compiles): <c>DrawFootprint</c> not called ⇒ no box ⇒ red; the dims hard-coded ⇒ the 8 × 3.4 assert red.
    /// </summary>
    [Fact]
    public void P7_TheGhost_IsTheTemplatesFootprint_TurnedToTheHeading_WithANotch()
    {
        var (repo, tank, _) = RepoWithTemplates();
        using var _r = repo;
        EntityFootprint.Dimensions(tank, out float len, out float wid);
        Assert.Equal((8f, 3.4f), (len, wid));   // the rule: the template's authored size over the class preset

        var (_, gizmo) = CreateGizmo(777L);
        gizmo.OnDragUpdate(new Vector3(100f, 200f, 0f));
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.UpdateAndDraw(repo, 0f, buffer);
        var frame = buffer.GetFrame().ToArray();
        Assert.DoesNotContain(frame, p => p.Shape == DebugPrimitiveShape.Sphere);
        var box = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.Box2D);
        Assert.Equal((100f, 200f), (box.BoxCenterX, box.BoxCenterY));
        Assert.Equal((4f, 1.7f), (box.BoxExtentX, box.BoxExtentY));
        Assert.Equal(SizeMode.WorldMeters, box.SizeMode);   // metres: it scales with the zoom like the symbol
        Assert.Equal(90f, box.BoxAngleDeg, 2);              // north until a drag sets the heading
        Assert.Equal(LineStyle.Dashed, box.LineStyle);
        Assert.Equal(100, box.FillColor.A);
        // ⭐ P7-C — ONE outline per view: the U-8 wire box is 3-D only (the 2-D renderer skips a primitive without Map2D) and is turned to the SAME heading as the footprint
        var wire = frame.Where(p => p.Shape == DebugPrimitiveShape.Line).ToArray();
        Assert.NotEmpty(wire);
        Assert.True(MathF.Abs(wire[0].LineEnd.X - wire[0].LineStart.X) < 1e-3f, "north: the box's length edge runs north (turned like the footprint), not east");
        Assert.All(wire, p => Assert.Equal(PipelineTarget.Viewport3D, p.TargetView));
        Assert.All(frame.Where(p => p.Shape != DebugPrimitiveShape.Line && p.Shape != DebugPrimitiveShape.Text), p => Assert.NotEqual(0, (int)(p.TargetView & PipelineTarget.Map2D)));
        var notch = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.FilledTriangle);
        Assert.True(notch.TriC.Y > 200f + 4f, "the notch tip is beyond the front (north) edge");

        // a heading drag east: the press point is where it lands, the box turns, the notch moves to the east edge
        gizmo.OnMouseEvent(MapMouseButton.Left, true, new Vector3(100f, 200f, 0f));
        gizmo.OnDragUpdate(new Vector3(150f, 200f, 0f));
        buffer.Clear();
        gizmo.UpdateAndDraw(repo, 0f, buffer);
        frame = buffer.GetFrame().ToArray();
        box = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.Box2D);
        Assert.Equal(0f, box.BoxAngleDeg, 2);
        notch = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.FilledTriangle);
        Assert.True(notch.TriC.X > 100f + 4f, "the notch tip is beyond the east edge");
    }

    /// <summary>
    /// ⭐ B-17 P7 ① — an AIRCRAFT ghost (the type carries a flight descriptor, 300 m): a dashed drop line from the ghost at altitude down to the ground under it, and a ground ring — an outline Sphere at z = 0 the size of the footprint (its shadow).
    /// A ground type draws neither. 🔴 Red-proof (compiles): <c>DrawAircraftShadow</c> not called ⇒ no drop line ⇒ red.
    /// </summary>
    [Fact]
    public void P7_AnAircraftGhost_HasADropLineAndAGroundRing_AGroundTypeHasNeither()
    {
        var (repo, _, _) = RepoWithTemplates();
        using var _r = repo;
        Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive[] Frame(long type)
        {
            var (_, g) = CreateGizmo(type);
            g.OnDragUpdate(new Vector3(100f, 200f, 0f));
            var b = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            g.UpdateAndDraw(repo, 0f, b);
            return b.GetFrame().ToArray();
        }
        var jet = Frame(778L);
        var drop = Assert.Single(jet, p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Dashed);
        Assert.Equal((100f, 200f, 300f), (drop.LineStart.X, drop.LineStart.Y, drop.LineStart.Z));
        Assert.Equal((100f, 200f, 0f), (drop.LineEnd.X, drop.LineEnd.Y, drop.LineEnd.Z));
        var ring = Assert.Single(jet, p => p.Shape == DebugPrimitiveShape.Sphere);
        Assert.Equal(0f, ring.SphereCenter.Z);
        Assert.Equal(8.5f, ring.SphereRadius, 3);
        Assert.True(ring.ThicknessU16 > 0, "a ring, not a disc");

        var tank = Frame(777L);
        Assert.DoesNotContain(tank, p => p.Shape == DebugPrimitiveShape.Sphere);
        Assert.DoesNotContain(tank, p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Dashed);
    }

    /// <summary>⭐ B-17 P7 ① — HOT PATH: a steady ghost frame (a ground type and an aircraft) allocates 0 B. 🔴 Red-proof: a per-frame <c>EntityFootprint.Dimensions(template, …)</c> lookup is the shape of regression it guards.</summary>
    [Fact]
    public void P7_ASteadyGhostFrame_Allocates0B()
    {
        var (repo, _, _) = RepoWithTemplates();
        using var _r = repo;
        foreach (long type in new[] { 777L, 778L })
        {
            var (_, g) = CreateGizmo(type);
            g.OnDragUpdate(new Vector3(100f, 200f, 0f));
            var b = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            g.UpdateAndDraw(repo, 0f, b); b.Clear(); g.UpdateAndDraw(repo, 0f, b); b.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            g.UpdateAndDraw(repo, 0f, b);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(delta == 0, $"a steady ghost frame (type {type}) allocates {delta} B");
        }
    }

    /// <summary>⭐ U-8 (CE-4339) — while a heading drag is down the placement tool draws a DIRECTION LINE from the press point toward the cursor (a main line and an arrow head), and its ghost stands at the press point (where the entity will land); button up: none.
    /// 🔴 Red-proof (compiles): the tool not calling <c>PlacementDirectionLine.Draw</c> ⇒ no amber line ⇒ red.</summary>
    [Fact]
    public void U8_TheHeadingDrag_DrawsADirectionLine_FromThePressPoint_TheGhostStandsAtThePress()
    {
        var (_, gizmo) = CreateGizmo();
        var world = new Fdp.Core.EntityRepository();
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        int Lines(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer b, out System.Numerics.Vector3 start, out System.Numerics.Vector3 end, out System.Numerics.Vector3 ghost)
        {
            int n = 0; start = end = ghost = default;
            foreach (var p in b.GetFrame())
            {
                if (p.Shape == DebugPrimitiveShape.Line && p.Color.R == Hrot.ScenarioEditor.Gizmos.PlacementDirectionLine.Colour.R && p.Color.G == Hrot.ScenarioEditor.Gizmos.PlacementDirectionLine.Colour.G)
                { if (n == 0) { start = p.LineStart; end = p.LineEnd; } n++; }
                if (p.Shape == DebugPrimitiveShape.Box2D) ghost = new System.Numerics.Vector3(p.BoxCenterX, p.BoxCenterY, 0f);   // ⭐ B-17 P7 ① — the ghost is the footprint box now (no sphere)
            }
            return n;
        }

        gizmo.OnDragUpdate(new System.Numerics.Vector3(100f, 200f, 0f));
        gizmo.UpdateAndDraw(world, 0f, buffer);
        Assert.Equal(0, Lines(buffer, out _, out _, out var ghost0));            // button up: no direction line
        Assert.Equal(new System.Numerics.Vector3(100f, 200f, 0f), ghost0);        // the ghost follows the cursor

        gizmo.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, true, new System.Numerics.Vector3(100f, 200f, 0f));
        gizmo.OnDragUpdate(new System.Numerics.Vector3(100f, 100f, 0f));          // dragged 100 m south
        buffer.Clear();
        gizmo.UpdateAndDraw(world, 0f, buffer);
        Assert.Equal(3, Lines(buffer, out var start, out var end, out var ghost));   // the line and the two strokes of its head
        Assert.Equal(new System.Numerics.Vector3(100f, 200f, 0f), start);          // from the press (the placement centre)...
        Assert.Equal(new System.Numerics.Vector3(100f, 100f, 0f), end);            // ...toward the cursor
        Assert.Equal(new System.Numerics.Vector3(100f, 200f, 0f), ghost);          // and the ghost stands at the press, where the entity lands

        buffer.Clear();
        gizmo.OnDragUpdate(new System.Numerics.Vector3(101f, 199f, 0f));           // inside the 3 m click threshold: still a click, no heading, no line
        gizmo.UpdateAndDraw(world, 0f, buffer);
        Assert.Equal(0, Lines(buffer, out _, out _, out _));
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
