using System.Numerics;
using Fdp.Toolkit.Combat.Components;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1033 S1 — the 3-D map's window-free rails: the family classifier over the whole built-in catalog, the shape kits and
/// poses, the camera's screen ↔ ground math and the 2-D ↔ 3-D switch. 📄 docs/DESIGN_Map_3D_Mode.md §3.2, §3.5, M2, M7, M12.
/// </summary>
public sealed class Map3DTests
{
    internal static TkbDatabase BuiltIn()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        UrbanCombatTkbCatalog.RegisterAll(db);
        return db;
    }

    /// <summary>The icon fallback exactly as it was before the extraction — the oracle that the extraction changed nothing.</summary>
    private static string? OldFallbackIconName(TkbTemplate t)
    {
        if (t.GetDescriptor<TkbCompositionDef>() is not null) return "_unit";
        var d = t.DisType;
        if (d.Kind == 3) return "_person";
        if (d.Kind == 1 && d.Domain == 1)
            return d.Category switch { 1 => "_tank", 2 => "_afv", 3 or 6 or 7 or 81 => "_wheeled", _ => null };
        return null;
    }

    // ── CE-1044 — the free camera behaves like Unity's scene view ───────────────────────────────────────────────────────────

    private sealed class FakeInput : Fdp.Toolkit.Vis2D.Abstractions.IInputProvider
    {
        public Vector2 MousePosition { get; set; }
        public Vector2 MouseDelta => Vector2.Zero;
        public float MouseWheelMove { get; set; }
        public bool RightDown { get; set; }
        public HashSet<int> Keys { get; } = new();
        public bool IsMouseButtonPressed(Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton b) => false;
        public bool IsMouseButtonDown(Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton b) => b == Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton.Right && RightDown;
        public bool IsMouseButtonReleased(Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton b) => false;
        public bool IsKeyPressed(Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey k) => false;
        public bool IsKeyDown(Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey k) => Keys.Contains((int)k);
        public bool IsKeyReleased(Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey k) => false;
        public int GetKeyPressed() => 0;
        public bool IsMouseCaptured => false;
        public bool IsKeyboardCaptured => false;
    }

    private static MapCamera3D FlyCamera() => new()
    {
        ViewportOverride = new Vector2(1280, 720),
        GroundHeight = (_, _) => 0f,                       // the relief follow is ON — that is what used to fight the free camera
        Pose = new CameraPose(new Vector3(0, 0, 0), 100f, MapCamera3D.NorthUpYaw, -0.5f),
    };

    private static void Frame(MapCamera3D cam, FakeInput input, float dt = 0.016f)
    {
        cam.Update(dt);
        cam.HandleInput(input);
    }

    /// <summary>
    /// ⭐⭐⭐ CE-1044 — right-drag LOOKS around the eye; it does not move the eye. 🔒 User, 2026-10-10: "when i press right button and
    /// move mouse up/down the camera is not just changing the pitch but travel up/down". 🔴 The per-frame ground pin on the look-at
    /// height moved the EYE every time a pitch change moved the look-at.
    /// </summary>
    [Fact]
    public void CE1044_RightDragUpDown_ChangesThePitch_AndTheEyeStaysWhereItIs()
    {
        var cam = FlyCamera();
        var input = new FakeInput { RightDown = true, MousePosition = new Vector2(600, 400) };
        Frame(cam, input);                                  // the press frame: arms the look
        var eye = cam.Eye;
        float pitch = cam.Pitch;

        for (int i = 1; i <= 20; i++)
        {
            input.MousePosition = new Vector2(600, 400 - i * 6);   // mouse UP
            Frame(cam, input);
        }

        Assert.NotEqual(pitch, cam.Pitch, 3);               // it looked
        Assert.True(Vector3.Distance(eye, cam.Eye) < 0.05f, $"the eye travelled {Vector3.Distance(eye, cam.Eye):F2} m while only looking");
    }

    /// <summary>⭐⭐⭐ CE-1044 — W flies along the view direction, vertical part included (Unity). Used to fly level: the ground pin ate the vertical part.</summary>
    [Fact]
    public void CE1044_W_FliesToWhereTheCameraIsLooking()
    {
        var cam = FlyCamera();
        var input = new FakeInput { RightDown = true, MousePosition = new Vector2(600, 400) };
        input.Keys.Add((int)Raylib_cs.KeyboardKey.W);
        Frame(cam, input);
        var eye = cam.Eye;
        var forward = cam.Forward;

        for (int i = 0; i < 30; i++) Frame(cam, input);

        var travelled = cam.Eye - eye;
        Assert.True(travelled.Length() > 2f, "W did not move the camera");
        Assert.True(Vector3.Dot(Vector3.Normalize(travelled), forward) > 0.999f,
            $"W flew {Vector3.Normalize(travelled)} but the camera looks along {forward}");
        Assert.True(travelled.Z < -1f, "looking down, forward must descend");
    }

    /// <summary>⭐ CE-1044 — the relief follow still works for what it was for: a middle-drag pan keeps the look-at point on the ground.</summary>
    [Fact]
    public void CE1044_ThePan_StillKeepsTheLookAtOnTheRelief()
    {
        var cam = FlyCamera();
        cam.GroundHeight = (x, _) => x * 0.1f;              // a slope
        cam.Update(0.016f);
        cam.LookAt += new Vector3(100, 0, 0);               // what a pan does: sideways only
        cam.Update(0.016f);
        Assert.Equal(10f, cam.LookAt.Z, 3);
    }

    /// <summary>⭐ CE-1044 — plain WASD is fine-grained (a tenth of the distance per second); Shift or Ctrl is 10× that; both 30×.</summary>
    [Theory]
    [InlineData(false, false, 10f)]
    [InlineData(true,  false, 100f)]
    [InlineData(false, true,  100f)]
    [InlineData(true,  true,  300f)]
    public void CE1044_FlySpeed_IsATenthOfTheDistancePerSecond_BoostedByShiftAndCtrl(bool shift, bool ctrl, float metresPerSecond)
    {
        var cam = FlyCamera();                              // distance 100 ⇒ 10 m/s plain
        var input = new FakeInput { RightDown = true, MousePosition = new Vector2(600, 400) };
        input.Keys.Add((int)Raylib_cs.KeyboardKey.W);
        if (shift) input.Keys.Add((int)Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey.LeftShift);
        if (ctrl)  input.Keys.Add((int)Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey.LeftControl);
        Frame(cam, input, 0.1f);                            // the press frame
        var eye = cam.Eye;

        Frame(cam, input, 0.1f);

        Assert.Equal(metresPerSecond * 0.1f, Vector3.Distance(eye, cam.Eye), 1);
    }

    [Fact]
    public void S1_TheIconFallback_IsUnchangedByTheExtraction_ForEveryBuiltInType()
    {
        foreach (var t in BuiltIn().GetAll())
            Assert.Equal(OldFallbackIconName(t), EntityTypeCatalog.FallbackIconName(t));
    }

    [Fact]
    public void S1_EveryPlaceableBuiltInType_HasAFamily_UnitsExcepted()
    {
        var db = BuiltIn();
        var missing = new List<string>();
        foreach (var e in EntityTypeCatalog.Build(db).Where(e => e.HasSide))
        {
            var family = VisualFamilies.Of(db.GetByType(e.TkbType));
            if (family == VisualFamily.Unknown) missing.Add($"{e.Name} ({e.DisText})");
        }
        Assert.True(missing.Count == 0, "types drawn as an unknown box: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData(TkbEntityTypes.Tank_M1Abrams, VisualFamily.Tank)]
    [InlineData(TkbEntityTypes.Tank_T72, VisualFamily.Tank)]
    [InlineData(TkbEntityTypes.IFV_Bradley, VisualFamily.Afv)]
    [InlineData(UrbanCombatTkbCatalog.TkbMilitaryApc, VisualFamily.Afv)]
    [InlineData(TkbEntityTypes.Truck_HMMWV, VisualFamily.WheeledUtility)]
    [InlineData(UrbanCombatTkbCatalog.TkbCivilianCar, VisualFamily.WheeledCar)]
    [InlineData(TkbEntityTypes.Infantry_Rifleman, VisualFamily.Person)]
    [InlineData(UrbanCombatTkbCatalog.TkbCivilianPedestrian, VisualFamily.Person)]
    [InlineData(TkbEntityTypes.Unit_TankPlatoon, VisualFamily.Unit)]
    public void S1_BuiltInTypes_MapToTheirKit(long type, VisualFamily expected)
        => Assert.Equal(expected, VisualFamilies.Of(BuiltIn().GetByType(type)));

    [Fact]
    public void S1_ASoldierCarriesARifle_ACivilianDoesNot()
    {
        var db = BuiltIn();
        Assert.True(EntityBodyLayer3D.Resolve(db.GetByType(TkbEntityTypes.Infantry_Rifleman)).Soldier);
        Assert.False(EntityBodyLayer3D.Resolve(db.GetByType(UrbanCombatTkbCatalog.TkbCivilianPedestrian)).Soldier);
        Assert.Equal(BlockFigure.Parts(StanceId.Standing, false).Length + 1, BlockFigure.Parts(StanceId.Standing, true).Length);
    }

    private static float Top(KitPart[] parts, Vector3 size) => parts.Max(p => Vector3.Transform(Vector3.Zero, p.BodyTransform(size)).Z
                                                                           + p.Extent.Z * size.Z / 2f);

    [Fact]
    public void S1_TheThreeStances_AreThreeHeights()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        float standing = Top(BlockFigure.Parts(StanceId.Standing, true), size);
        float crouched = Top(BlockFigure.Parts(StanceId.Crouched, true), size);
        float prone = Top(BlockFigure.Parts(StanceId.Prone, true), size);
        Assert.InRange(standing, 1.65f, 1.85f);
        Assert.InRange(crouched, 1.0f, 1.35f);
        Assert.InRange(prone, 0.2f, 0.45f);
    }

    [Theory]
    [InlineData(VisualFamily.Tank)]
    [InlineData(VisualFamily.Afv)]
    [InlineData(VisualFamily.WheeledCar)]
    [InlineData(VisualFamily.WheeledUtility)]
    [InlineData(VisualFamily.Helicopter)]
    [InlineData(VisualFamily.Jet)]
    [InlineData(VisualFamily.CargoPlane)]
    public void S1_EveryVehiclePart_SitsInsideTheTypesBox_AndTheKitStandsOnTheGround(VisualFamily family)
    {
        var size = ShapeKits.DefaultSize(family);
        float lowest = float.MaxValue;
        foreach (var part in ShapeKits.For(family).Parts)
        {
            var m = part.BodyTransform(size);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, part.Shape != PartShape.Box ? ((i & 4) == 0 ? 0f : 1f) : ((i & 4) == 0 ? -0.5f : 0.5f));
                var p = Vector3.Transform(corner, m);
                Assert.InRange(p.X, -size.X * 0.75f, size.X * 0.75f);   // a barrel may overhang the hull
                Assert.InRange(p.Y, -size.Y * 0.55f, size.Y * 0.55f);
                Assert.InRange(p.Z, -0.05f, size.Z * 1.02f);
                lowest = MathF.Min(lowest, p.Z);
            }
        }
        Assert.InRange(lowest, -0.05f, 0.05f);
    }

    [Fact]
    public void S1_TheTankBarrel_PointsForward()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Tank);
        var barrel = ShapeKits.Tank.Parts.Single(p => p.Shape == PartShape.Cylinder);
        var m = barrel.BodyTransform(size);
        var a = Vector3.Transform(new Vector3(0, 0, 0), m);
        var b = Vector3.Transform(new Vector3(0, 0, 1), m);
        Assert.True(MathF.Abs(b.X - a.X) > 3f && MathF.Abs(b.Y - a.Y) < 1e-3f && MathF.Abs(b.Z - a.Z) < 1e-3f, $"barrel axis {a} → {b}");
        Assert.True(MathF.Max(a.X, b.X) > size.X / 2f, "the barrel reaches past the hull front");
    }

    // ── aircraft (SISO-REF-010 platform / air categories) ──

    [Theory]
    [InlineData(20, VisualFamily.Helicopter)]
    [InlineData(21, VisualFamily.Helicopter)]
    [InlineData(23, VisualFamily.Helicopter)]
    [InlineData(25, VisualFamily.Helicopter)]
    [InlineData(1, VisualFamily.Jet)]
    [InlineData(2, VisualFamily.Jet)]
    [InlineData(50, VisualFamily.Jet)]
    [InlineData(4, VisualFamily.CargoPlane)]
    [InlineData(3, VisualFamily.CargoPlane)]
    [InlineData(57, VisualFamily.CargoPlane)]
    [InlineData(0, VisualFamily.Unknown)]
    public void Air_TheDisCategory_PicksTheKit(byte category, VisualFamily expected)
    {
        var t = new TkbTemplate("air", 9100 + category) { DisType = new DISEntityType { Kind = 1, Domain = 2, Category = category } };
        Assert.Equal(expected, VisualFamilies.Of(t));
        Assert.Null(EntityTypeCatalog.FallbackIconName(t));   // no air glyph exists — the picker's icon is unchanged
    }

    [Fact]
    public void Air_TheJetsNoseCone_PointsForward()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Jet);
        var cone = ShapeKits.Jet.Parts.Single(p => p.Shape == PartShape.Cone);
        var m = cone.BodyTransform(size);
        var baseCentre = Vector3.Transform(Vector3.Zero, m);
        var apex = Vector3.Transform(new Vector3(0, 0, 1), m);
        Assert.True(apex.X > baseCentre.X + 1f, $"cone base {baseCentre} apex {apex}");
    }

    // ── built-in aircraft (CE-1041, Body.Geometry) ──

    [Theory]
    [InlineData(TkbEntityTypes.Heli_UH60, VisualFamily.Helicopter)]
    [InlineData(TkbEntityTypes.Jet_F16, VisualFamily.Jet)]
    [InlineData(TkbEntityTypes.Cargo_C130, VisualFamily.CargoPlane)]
    public void Aircraft_BuiltIn_HaveTheirKit_TheirGeometry_AndNoCarKinematics(long type, VisualFamily family)
    {
        var t = BuiltIn().GetByType(type);
        Assert.Equal(family, VisualFamilies.Of(t));
        Assert.Null(t.GetDescriptor<VehicleParametersDto>());            // no car kinematics for an aircraft
        var g = Assert.IsType<BodyGeometryDto>(t.GetDescriptor<BodyGeometryDto>());
        Assert.Equal(ReferencePointKind.CentreOfGravity, g.ReferencePoint);
        Assert.True(g.GroundContacts.Count >= 3, "a body rests on at least three points");

        // The size box's bottom is where the tyres touch: the drawing and the landing agree on the ground line.
        float lowestTyre = g.GroundContacts.Min(c => c.Z);
        Assert.InRange(g.BodyCentreZ - g.Height / 2f - lowestTyre, -0.05f, 0.05f);
        Assert.InRange(BodyGeometry.RestingHeight(g), 1f, 4f);

        // ⭐ The map sizes the kit from the geometry, not the family default.
        var look = EntityBodyLayer3D.Resolve(t);
        Assert.Equal(new Vector3(g.Length, g.Width, g.Height), look.Size);
        Assert.Same(g, look.Geometry);
    }

    [Fact]
    public void Aircraft_AreListedInAddEntity_UnderTheirDisAirCategory()
    {
        var catalog = EntityTypeCatalog.Build(BuiltIn());
        Assert.Contains(catalog, e => e.TkbType == TkbEntityTypes.Heli_UH60 && e.Category.Contains("Utility Helicopter"));
        Assert.Contains(catalog, e => e.TkbType == TkbEntityTypes.Jet_F16 && e.Category.Contains("Fighter"));
        Assert.Contains(catalog, e => e.TkbType == TkbEntityTypes.Cargo_C130 && e.Category.Contains("Cargo"));
    }

    // ── articulation (§3.11) ──

    private static Vector3 Muzzle(ShapeKit kit, Vector3 size, ArticulationPose pose)
    {
        var gun = kit.Parts.Single(p => p.Role == PartRole.Gun);
        return Vector3.Transform(new Vector3(0, 0, 1), gun.BodyTransform(size, kit.Pivots, pose));   // the cylinder's far end
    }

    [Theory]
    [InlineData(VisualFamily.Tank)]
    [InlineData(VisualFamily.Afv)]
    public void Articulation_TheTurretTraverses_AndTheGunElevates_TheHullStays(VisualFamily family)
    {
        var kit = ShapeKits.For(family);
        var size = ShapeKits.DefaultSize(family);
        Assert.True(kit.IsArticulated);

        var ahead = Muzzle(kit, size, ArticulationPose.Neutral);
        var left = Muzzle(kit, size, new ArticulationPose(MathF.PI / 2f, 0f));
        var raised = Muzzle(kit, size, new ArticulationPose(0f, 0.3f));

        var pivot = new Vector2(kit.Pivots.Turret.X * size.X, kit.Pivots.Turret.Y * size.Y);
        float reach = 0.25f * size.X;
        Assert.True(ahead.X > pivot.X + reach && MathF.Abs(ahead.Y) < 0.01f, $"neutral muzzle {ahead}");
        Assert.True(left.Y > pivot.Y + reach && MathF.Abs(left.X - pivot.X) < 0.01f, $"turret 90° left: muzzle {left}");   // CCW = left
        Assert.True(raised.Z > ahead.Z + 0.3f, $"elevated muzzle {raised} vs {ahead}");

        Assert.InRange(Vector2.Distance(new Vector2(ahead.X, ahead.Y), pivot) - Vector2.Distance(new Vector2(left.X, left.Y), pivot), -0.01f, 0.01f);

        foreach (var hull in kit.Parts.Where(p => p.Role == PartRole.Hull))
            Assert.Equal(hull.BodyTransform(size), hull.BodyTransform(size, kit.Pivots, new ArticulationPose(1.2f, 0.4f)));
    }

    // ── the camera ──

    /// <summary>A fixed screen: the frame rail may have a window open in parallel, and the camera reads the window's size.</summary>
    private static readonly Vector2 Viewport = new(1280, 720);

    private static MapCamera Camera2D(Vector2 centre, float zoom)
    {
        var c = new MapCamera();
        var vp = Viewport;
        // ⭐ CE-1040 — the Target is the world point at the screen's top-left; north-up, the screen's top is the view's NORTH edge.
        var target = new Vector2(centre.X - vp.X / 2f / zoom, centre.Y + vp.Y / 2f / zoom);
        c.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        return c;
    }

    [Fact]
    public void S1_TheOverheadPose_ShowsWhatThe2DMapShows_SameCentre_SameScale()
    {
        var two = Camera2D(new Vector2(250, -40), 2.5f);
        var three = new MapCamera3D { ViewportOverride = Viewport };
        three.Pose = three.OverheadMatching(two);

        var centre = three.ScreenToWorld(new Vector2(640, 360));
        Assert.InRange(Vector2.Distance(centre, new Vector2(250, -40)), 0f, 0.05f);
        Assert.InRange(three.MetresPerPixel, 1f / 2.5f * 0.999f, 1f / 2.5f * 1.001f);
        Assert.InRange(three.Zoom, 2.5f * 0.999f, 2.5f * 1.001f);
    }

    [Fact]
    public void S1_In3D_NorthIsUp_AndEastIsRight()
    {
        var three = new MapCamera3D { ViewportOverride = Viewport, LookAt = new Vector3(0, 0, 5), Distance = 200f };
        var centre = three.WorldToScreen(new Vector2(0, 0));
        var north = three.WorldToScreen(new Vector2(0, 20));
        var east = three.WorldToScreen(new Vector2(20, 0));
        Assert.True(north.Y < centre.Y - 10f, $"north {north} should be above the centre {centre}");
        Assert.True(east.X > centre.X + 10f, $"east {east} should be right of the centre {centre}");
    }

    [Theory]
    [InlineData(-89.9f, 300f)]
    [InlineData(-35f, 300f)]
    [InlineData(-12f, 80f)]
    public void S1_ScreenToWorld_AndBack_AreInverses_OnTheGround(float pitchDeg, float distance)
    {
        var three = new MapCamera3D { ViewportOverride = Viewport, LookAt = new Vector3(100, 50, 3), Distance = distance, Pitch = pitchDeg * MathF.PI / 180f, Yaw = 0.7f };
        foreach (var px in new[] { new Vector2(640, 360), new Vector2(200, 600), new Vector2(1100, 500) })
        {
            var ground = three.ScreenToWorld(px);
            var back = three.WorldToScreen(ground);
            Assert.InRange(Vector2.Distance(px, back), 0f, 0.5f);
        }
    }

    [Fact]
    public void S1_AHorizonPixel_GivesAFiniteFarPoint_NeverNaN()
    {
        var three = new MapCamera3D { ViewportOverride = Viewport, Distance = 50f, Pitch = -0.05f };
        var p = three.ScreenToWorld(new Vector2(640, 5));
        Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y));
    }

    [Fact]
    public void S1_TheSwitch_RoundTrips_The2DView()
    {
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var two = Camera2D(new Vector2(-120, 300), 1.7f);
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = Viewport });

        sw.Set(true, animate: false);
        Assert.True(sw.Is3D);
        Assert.Same(sw.Camera3D, canvas.Camera);
        Assert.InRange(sw.Camera3D.Pitch, sw.TiltPitch - 1e-3f, sw.TiltPitch + 1e-3f);

        sw.Set(false, animate: false);
        Assert.False(sw.Is3D);
        Assert.Same(two, canvas.Camera);
        Assert.InRange(two.Zoom, 1.7f * 0.999f, 1.7f * 1.001f);
        var centre = two.ScreenToWorld(new Vector2(640, 360));
        Assert.InRange(Vector2.Distance(centre, new Vector2(-120, 300)), 0f, 0.05f);
    }

    // ── CE-1033 S4 — the terrain by surface and material, water ──

    [Fact]
    public void S4_TheTerrainTags_LeaveTheNavmeshSoupUnchanged_AndColourForestAndWater()
    {
        var town = TestTown();
        Fdp.Toolkit.Terrain.TerrainWorldMesh.Build(town, out var soupV, out var soupI);
        Fdp.Toolkit.Terrain.TerrainWorldMesh.Build(town, out var tagV, out var tagI, out var tags);
        Assert.Equal(soupV, tagV);                                       // the navmesh is baked from exactly the same triangles
        Assert.Equal(soupI, tagI);
        Assert.Equal(tagI.Length / 3, tags.Length);
        Assert.Contains(tags, t => t.Kind == Fdp.Toolkit.Terrain.TerrainTriangleKind.Wall && t.Material == "concrete");
        Assert.Contains(tags, t => t.Material == Fdp.Toolkit.Terrain.TerrainWorldMesh.ForestMaterial);   // test-town has a forest

        var geometry = WorldQuery.RenderGeometryOf(WorldWith(town))!;
        geometry.BuildTagged(out var verts, out var indices, out var kinds, out var materials);
        Assert.Contains(TerrainSurfaceKind.Water, kinds);                                     // and a pond, drawn into its hole
        Assert.Equal(kinds.Length, materials.Length);
        TerrainLayer3D.BuildVertexData(verts, indices, kinds, materials, out _, out var normals, out var colours);
        int water = Array.IndexOf(kinds, TerrainSurfaceKind.Water);
        Assert.Equal(TerrainLayer3D.WaterColor, colours[3 * water]);
        Assert.True(normals[3 * water].Y > 0.9f, "the water faces up");
        int forest = Array.IndexOf(materials, "forest");
        Assert.Equal(TerrainLayer3D.ForestColor, colours[3 * forest]);
        Assert.Equal(new Raylib_cs.Color(172, 96, 72, 255), TerrainLayer3D.ColourOf(TerrainSurfaceKind.Wall, "brick"));
        Assert.Equal(TerrainLayer3D.RoofColor, TerrainLayer3D.ColourOf(TerrainSurfaceKind.Roof, "concrete"));
    }

    // ── CE-1033 S4 — things come alive (§6 S4): wheels roll, limbs swing, a rotor turns ──

    [Fact]
    public void S4_WheelsRollByTheDistanceDriven_BackwardsInReverse_AndATeleportDoesNotSpinThem()
    {
        var t = new MotionTracker();
        var car = new Entity(1, 1);
        var size = new Vector3(4.5f, 1.8f, 1.5f);
        float radius = ShapeKits.WheelRadiusOfHeight * size.Z;
        t.Advance(car, Vector3.Zero, Quaternion.Identity, VisualFamily.WheeledCar, size, 0, false);
        var p = t.Advance(car, new Vector3(1f, 0, 0), Quaternion.Identity, VisualFamily.WheeledCar, size, 0.1, false);
        Assert.Equal(1f / radius % (MathF.PI * 2f), p.WheelRoll, 3);
        p = t.Advance(car, new Vector3(0.5f, 0, 0), Quaternion.Identity, VisualFamily.WheeledCar, size, 0.2, false);
        Assert.Equal(0.5f / radius % (MathF.PI * 2f), p.WheelRoll, 3);                        // reversing rolls back
        var before = p.WheelRoll;
        p = t.Advance(car, new Vector3(500f, 0, 0), Quaternion.Identity, VisualFamily.WheeledCar, size, 0.3, false);
        Assert.Equal(before, p.WheelRoll, 4);                                                  // a teleport is not driving
    }

    [Fact]
    public void S4_AWalkingFigureSwingsItsLimbs_AStandingOneDoesNot_ByTheSharedGaitRule()
    {
        var t = new MotionTracker();
        var man = new Entity(2, 1);
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        float maxSwing = 0f, minSwing = 0f;
        for (int i = 0; i <= 60; i++)   // 1.5 m/s for 2 s at 30 Hz — a full walk
        {
            var pose = t.Advance(man, new Vector3(i * 0.05f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, i / 30.0, false);
            maxSwing = MathF.Max(maxSwing, pose.Swing); minSwing = MathF.Min(minSwing, pose.Swing);
        }
        Assert.True(maxSwing > 0.3f && minSwing < -0.3f, $"the legs swing both ways at a walk ({minSwing}..{maxSwing})");
        var still = t.Advance(man, new Vector3(3f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, 2.1, false);
        Assert.Equal(0f, still.Swing);

        // the left leg's foot goes FORWARD (+x) on a positive swing
        var leg = BlockFigure.Parts(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, false)
                             .Last(k => k.Role == PartRole.LegLeft);   // WL-6c — the shin (thigh + shin swing together from the hip)
        var foot = Vector3.Transform(new Vector3(0.5f, 0, 0), leg.BodyTransform(size, default, ArticulationPose.Neutral, new MotionPose(0, 0, 0.4f)));
        var footStill = Vector3.Transform(new Vector3(0.5f, 0, 0), leg.BodyTransform(size));
        Assert.True(foot.X > footStill.X + 0.1f, $"left foot forward: {foot} vs {footStill}");
    }

    [Fact]
    public void S4_AStanceChange_Blends_HalfWayIsBetweenTheTwoPoses()
    {
        var stand = BlockFigure.Parts(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, true);
        var prone = BlockFigure.Parts(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, true);
        var half = BlockFigure.Blend(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Fdp.Toolkit.Tkb.Domain.StanceId.Prone, 0.5f, true);
        Assert.Equal(stand.Length, half.Length);
        for (int i = 0; i < half.Length; i++)
            Assert.InRange(Vector3.Distance(half[i].Centre, (stand[i].Centre + prone[i].Centre) / 2f), 0f, 1e-4f);
        Assert.Same(prone, BlockFigure.Blend(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, Fdp.Toolkit.Tkb.Domain.StanceId.Prone, 1f, true));
    }

    // ── ⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13e, R-272) — the rifle in the weapon brain's posture ─────────────────────────────

    private static Vector3 End(KitPart p, Vector3 size, float along = 0.5f) => Vector3.Transform(new Vector3(along, 0, 0), p.BodyTransform(size));

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>⭐ WL-6 (W-13e) — at ease the rifle hangs low, muzzle down; ready it is at the chest, muzzle forward and down; aimed it
    /// is level at the shoulder. In every posture and stance both hands are ON the rifle and each arm's two segments meet at the elbow.</summary>
    [Theory]
    [InlineData(StanceId.Standing)]
    [InlineData(StanceId.Crouched)]
    public void WL6_TheRifleIsHeldInThePosture_HandsOnIt_ElbowsJoined(StanceId stance)
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        float Muzzle(WeaponPostureKind k) => End(BlockFigure.Parts(stance, true, k)[^1], size).Z;
        float Stock(WeaponPostureKind k) => End(BlockFigure.Parts(stance, true, k)[^1], size, -0.5f).Z;
        Assert.True(Muzzle(WeaponPostureKind.AtEase) < Muzzle(WeaponPostureKind.Ready), "at ease is lower than ready");
        Assert.True(Muzzle(WeaponPostureKind.Ready) < Muzzle(WeaponPostureKind.Aimed), "ready is lower than aimed");
        Assert.Equal(Stock(WeaponPostureKind.Aimed), Muzzle(WeaponPostureKind.Aimed), 2);          // aimed is level
        Assert.True(Muzzle(WeaponPostureKind.AtEase) < Stock(WeaponPostureKind.AtEase) - 0.4f, "at ease: muzzle well down");

        foreach (WeaponPostureKind k in Enum.GetValues(typeof(WeaponPostureKind)))
        {
            var parts = BlockFigure.Parts(stance, true, k);
            Assert.Equal(BlockFigure.Parts(stance, false).Length + 1, parts.Length);
            var rifle = parts[^1];
            Vector3 stock = End(rifle, size, -0.5f), muzzle = End(rifle, size);
            for (int arm = BlockFigure.TorsoIndex + 2; arm <= BlockFigure.TorsoIndex + 4; arm += 2)   // left then right: upper arm, forearm
            {
                Assert.InRange(Vector3.Distance(End(parts[arm], size), End(parts[arm + 1], size, -0.5f)), 0f, 0.01f);   // the elbow
                Assert.InRange(DistanceToSegment(End(parts[arm + 1], size), stock, muzzle), 0f, 0.07f);                // the hand on the rifle
            }
            Assert.All(parts, p => Assert.False(p.Role is PartRole.ArmLeft or PartRole.ArmRight, "an armed soldier's arms hold the rifle — they do not swing"));
        }
    }

    /// <summary>⭐ WL-6 — a civilian's arm swings as one piece from the shoulder: the forearm follows the upper arm.</summary>
    [Fact]
    public void WL6_ACiviliansArm_SwingsFromTheShoulder_ForearmAndAll()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var parts = BlockFigure.Parts(StanceId.Standing, false);
        var swing = new MotionPose(0, 0, 0.5f);
        var upper = parts[BlockFigure.TorsoIndex + 2]; var fore = parts[BlockFigure.TorsoIndex + 3];
        Assert.Equal(PartRole.ArmLeft, upper.Role);
        Assert.Equal(PartRole.ArmLeft, fore.Role);
        var elbow = Vector3.Transform(new Vector3(0.5f, 0, 0), upper.BodyTransform(size, default, ArticulationPose.Neutral, swing));
        var forearmTop = Vector3.Transform(new Vector3(-0.5f, 0, 0), fore.BodyTransform(size, default, ArticulationPose.Neutral, swing));
        Assert.InRange(Vector3.Distance(elbow, forearmTop), 0f, 0.01f);
        var hand = Vector3.Transform(new Vector3(0.5f, 0, 0), fore.BodyTransform(size, default, ArticulationPose.Neutral, swing));
        Assert.True(hand.X < End(fore, size).X - 0.15f, $"the left arm swings BACK on a positive swing: {hand}");
    }

    /// <summary>⭐ WL-6 (W-13e) — a running figure leans forward from the hip (12° at a full run), a walking one barely, a still one not;
    /// the legs do not lean.</summary>
    [Fact]
    public void WL6_ARunningFigureLeansForward_FromTheHip()
    {
        var t = new MotionTracker();
        var man = new Entity(3, 1);
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        MotionPose pose = default;
        for (int i = 0; i <= 30; i++) pose = t.Advance(man, new Vector3(i * 0.2f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, i / 30.0, false);   // 6 m/s
        Assert.Equal(MotionTracker.RunLean, pose.Lean, 2);
        var still = t.Advance(man, new Vector3(6f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, 1.1, false);
        Assert.Equal(0f, still.Lean);

        var parts = BlockFigure.Parts(StanceId.Standing, true);
        var head = parts[BlockFigure.TorsoIndex + 1];
        var leaning = new MotionPose(0, 0, 0, Lean: MotionTracker.RunLean, HipZ: 0.5f);
        var headAt = Vector3.Transform(Vector3.Zero, head.BodyTransform(size, default, ArticulationPose.Neutral, leaning));
        Assert.True(headAt.X > 0.12f, $"the head goes forward: {headAt}");
        var leg = parts[0];
        Assert.Equal(Vector3.Transform(Vector3.Zero, leg.BodyTransform(size)),
                     Vector3.Transform(Vector3.Zero, leg.BodyTransform(size, default, ArticulationPose.Neutral, leaning)));
    }

    /// <summary>⭐ WL-6 (W-13e) — an AIMED soldier turns his upper body toward his target (his heading does not change) and pitches it;
    /// the turn is clamped at 75°; a soldier not aimed, or aimed at nothing placeable, does not turn.</summary>
    [Fact]
    public void WL6_AnAimedSoldierTurnsAndPitchesTowardHisTarget_Clamped()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var man = world.CreateEntity();
        var tf = new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity };
        world.AddComponent(man, tf);
        var target = world.CreateEntity();
        world.AddComponent(target, new SimTransform { Position = new Vector3(20f, 20f, -10f), Rotation = Quaternion.Identity });
        world.AddComponent(man, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = WeaponPostureKind.Ready, Target = target });
        Assert.False(EntityBodyLayer3D.TryAim(world, man, tf, size, StanceId.Standing, out _, out _));      // ready: not aimed

        world.SetComponent(man, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = target });
        Assert.True(EntityBodyLayer3D.TryAim(world, man, tf, size, StanceId.Standing, out float twist, out float pitch));
        Assert.Equal(MathF.PI / 4f, twist, 2);                                     // 45° to the left
        Assert.True(pitch < -0.2f, $"pitched down at the lower target: {pitch}");

        world.SetComponent(target, new SimTransform { Position = new Vector3(-10f, 1f, 1f), Rotation = Quaternion.Identity });
        Assert.True(EntityBodyLayer3D.TryAim(world, man, tf, size, StanceId.Standing, out twist, out _));
        Assert.Equal(EntityBodyLayer3D.MaxAimTwist, twist, 3);                     // behind him: clamped

        world.SetComponent(man, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = WeaponPostureKind.Aimed, Point = new Vector3(0f, -30f, 1.4f) });
        Assert.True(EntityBodyLayer3D.TryAim(world, man, tf, size, StanceId.Standing, out twist, out _));
        Assert.Equal(-EntityBodyLayer3D.MaxAimTwist, twist, 3);                    // a point square to his right — clamped at 75°
    }

    /// <summary>⭐ WL-6b (docs/DESIGN_Weapon_Layers.md §5h) — a posture change moves the rifle over 0.3 s of SIM time: a unit first seen
    /// is simply in its posture; halfway the muzzle is between the two poses; a pause (no sim time) freezes it; a change mid-blend starts
    /// from the nearer end; a clock that goes back snaps.</summary>
    [Fact]
    public void WL6b_APostureChangeMovesTheRifleOverSimTime_NotInOneFrame()
    {
        var blend = new PostureBlend();
        var man = new Entity(7, 1);
        Assert.Equal((WeaponPostureKind.AtEase, WeaponPostureKind.AtEase, 1f), blend.Posture(man, WeaponPostureKind.AtEase, 10.0));
        var (from, to, t) = blend.Posture(man, WeaponPostureKind.Aimed, 10.0);          // the weapon brain raises it
        Assert.Equal((WeaponPostureKind.AtEase, WeaponPostureKind.Aimed, 0f), (from, to, t));
        Assert.Equal(0.5f, blend.Posture(man, WeaponPostureKind.Aimed, 10.15).T, 3);
        Assert.Equal(0.5f, blend.Posture(man, WeaponPostureKind.Aimed, 10.15).T, 3);     // paused: frozen
        Assert.Equal(1f, blend.Posture(man, WeaponPostureKind.Aimed, 10.4).T);
        blend.Posture(man, WeaponPostureKind.Ready, 11.0);                                  // a new change …
        var mid = blend.Posture(man, WeaponPostureKind.AtEase, 11.2);                       // … and another at 2/3 of it
        Assert.Equal((WeaponPostureKind.Ready, WeaponPostureKind.AtEase), (mid.From, mid.To));   // from the nearer end
        Assert.Equal(1f, blend.Posture(man, WeaponPostureKind.AtEase, 5.0).T);              // a replay jump back: snapped

        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        float Muzzle(KitPart[] parts) => Vector3.Transform(new Vector3(0.5f, 0, 0), parts[^1].BodyTransform(size)).Z;
        float low = Muzzle(BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.AtEase));
        float high = Muzzle(BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.Aimed));
        float half = Muzzle(BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.AtEase, WeaponPostureKind.Aimed, 0.5f));
        Assert.InRange(half, low + 0.1f, high - 0.1f);
        // a soldier walking while he raises his rifle keeps swinging his legs (a posture blend keeps the parts' roles)
        var halfway = BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.AtEase, WeaponPostureKind.Aimed, 0.5f);
        Assert.Equal(PartRole.LegLeft, halfway[0].Role);
        Assert.Equal(PartRole.LegRight, halfway[2].Role);
    }

    /// <summary>⭐ WL-6b — the aimed body TURNS toward its target over a fraction of a second instead of jumping, and turns back when
    /// it is no longer aimed; a pause freezes it; a unit first seen is already turned.</summary>
    [Fact]
    public void WL6b_TheAimTurnEasesIn_AndBackOut()
    {
        var blend = new PostureBlend();
        var man = new Entity(8, 1);
        Assert.Equal((0f, 0f), blend.Aim(man, 0f, 0f, 1.0));
        var (twist, _) = blend.Aim(man, 1.0f, 0f, 1.05);
        Assert.InRange(twist, 0.2f, 0.5f);                                  // 0.05 s in: part of the way
        Assert.Equal(twist, blend.Aim(man, 1.0f, 0f, 1.05).Twist);          // paused: frozen
        Assert.InRange(blend.Aim(man, 1.0f, 0f, 1.6).Twist, 0.95f, 1.0f);    // ~0.5 s: there
        Assert.InRange(blend.Aim(man, 0f, 0f, 1.65).Twist, 0.5f, 0.8f);      // no longer aimed: turning back
        var other = new Entity(9, 1);
        Assert.Equal(0.7f, blend.Aim(other, 0.7f, 0f, 2.0).Twist);           // first seen: already turned
    }

    // ── ⭐ WL-6c — knees: the athletic fighting stance, the combat glide, the walking knee ─────────────────────────────────────────

    private static Vector3 Point(KitPart p, Vector3 size, float along) => Vector3.Transform(new Vector3(along, 0, 0), p.BodyTransform(size));

    /// <summary>⭐ WL-6c — the knee keeps the thigh and shin lengths, bends forward, and a leg out of reach is straight.</summary>
    [Fact]
    public void WL6c_TheKneeIsSolvedFromHipAndAnkle()
    {
        var hip = new Vector3(0, 0, 0.8f); var ankle = new Vector3(0.1f, 0, 0.02f);
        var knee = BlockFigure.Knee(hip, ankle, 0.44f, 0.44f, Vector3.UnitX);
        Assert.Equal(0.44f, Vector3.Distance(hip, knee), 3);
        Assert.Equal(0.44f, Vector3.Distance(knee, ankle), 3);
        Assert.True(knee.X > 0.1f, $"the knee bends forward: {knee}");
        var straight = BlockFigure.Knee(new Vector3(0, 0, 0.9f), new Vector3(0, 0, -0.5f), 0.44f, 0.44f, Vector3.UnitX);
        Assert.InRange(Vector3.Distance(new Vector3(0, 0, 0.46f), straight), 0f, 1e-5f);
    }

    /// <summary>⭐ WL-6c — an AIMED soldier standing still is in the athletic fighting stance: hips lower, knees bent, the support foot
    /// forward and the firing-side foot back, both feet on the ground, the head lower; at ease he stands straight.</summary>
    [Fact]
    public void WL6c_AnAimedSoldierStandsInTheAthleticStance()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var easy = BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.AtEase);
        var aimed = BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.Aimed);
        float HipZ(KitPart[] p) => Point(p[0], size, -0.5f).Z;
        Assert.Equal(0.90f, HipZ(easy), 2);
        Assert.Equal(0.90f - BlockFigure.AthleticDrop, HipZ(aimed), 2);
        Vector3 ankleL = Point(aimed[1], size, 0.5f), ankleR = Point(aimed[3], size, 0.5f);
        Assert.True(ankleL.X - ankleR.X > 0.3f, $"staggered: left {ankleL}, right {ankleR}");
        Assert.InRange(ankleL.Z, 0f, 0.05f); Assert.InRange(ankleR.Z, 0f, 0.05f);   // on the ground
        var kneeL = Point(aimed[0], size, 0.5f);
        Assert.True(kneeL.X > (Point(aimed[0], size, -0.5f).X + ankleL.X) / 2f + 0.05f, "the knee is bent forward");
        Assert.InRange(Point(easy[1], size, 0.5f).X, -0.01f, 0.01f);              // at ease: straight, feet together under him
        float Head(KitPart[] p) => Vector3.Transform(Vector3.Zero, p[BlockFigure.TorsoIndex + 1].BodyTransform(size)).Z;
        Assert.Equal(Head(easy) - BlockFigure.AthleticDrop, Head(aimed), 2);
    }

    /// <summary>⭐ WL-6c — aimed and on the move he GLIDES: feet back under the lowered hips, knees bent; the leg parts keep their
    /// swing roles; a shin bends back about its knee as the gait swings it through.</summary>
    [Fact]
    public void WL6c_AimedAndMovingHeGlides_KneesBent_AndTheShinBendsAsTheLegSwingsThrough()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var glide = BlockFigure.Glide(BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.Aimed), 1f);
        for (int i = 1; i <= 3; i += 2)
        {
            var ankle = Point(glide[i], size, 0.5f);
            Assert.InRange(ankle.X, -0.01f, 0.07f);                                         // under the hips
            Assert.True(Vector3.Distance(Point(glide[i - 1], size, -0.5f), ankle) < 0.84f, "knees bent");
        }
        Assert.Equal(PartRole.LegLeft, glide[1].Role);
        Assert.Equal(PartRole.LegRight, glide[3].Role);
        Assert.Same(BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.Aimed),
                    BlockFigure.Glide(BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.Aimed), 0f));

        var walk = BlockFigure.Parts(StanceId.Standing, false);
        var shin = walk[1];
        var footStill = Point(shin, size, 0.5f);
        var footBent = Vector3.Transform(new Vector3(0.5f, 0, 0), shin.BodyTransform(size, default, ArticulationPose.Neutral, new MotionPose(0, 0, 0, KneeFlexL: 0.5f)));
        Assert.True(footBent.X < footStill.X - 0.15f && footBent.Z > footStill.Z + 0.04f, $"the foot lifts back: {footBent} vs {footStill}");
        var thigh = walk[0];
        Assert.Equal(Point(thigh, size, 0.5f),
                     Vector3.Transform(new Vector3(0.5f, 0, 0), thigh.BodyTransform(size, default, ArticulationPose.Neutral, new MotionPose(0, 0, 0, KneeFlexL: 0.5f))));

        var t = new MotionTracker();
        var man = new Entity(11, 1);
        MotionPose pose = default;
        for (int i = 0; i <= 30; i++) pose = t.Advance(man, new Vector3(i * 0.05f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, i / 30.0, false);
        Assert.InRange(pose.Stride, 0.9f, 1f);                                              // 1.5 m/s: a full walk
    }

    /// <summary>⭐ W4 (docs/DESIGN_Visual_Effects.md §7 VE-S) — the dead figure lies on the ground (lower than prone), arms flung out
    /// wider than any stance; a soldier's rifle lies beside him; halfway through the fall he is between his stance and the ground.</summary>
    [Fact]
    public void W4_TheDeadFigureLiesOnTheGround_ArmsOut_AndTheFallBlendsIntoIt()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var dead = BlockFigure.Dead(soldier: true);
        Assert.Equal(BlockFigure.Parts(StanceId.Standing, true).Length, dead.Length);   // same parts, so a fall can blend
        Assert.Equal(BlockFigure.Dead(false).Length + 1, dead.Length);                   // the dropped rifle
        Assert.InRange(Top(dead, size), 0.1f, 0.35f);
        float Reach(KitPart[] parts) => parts.Max(p => MathF.Abs(Vector3.Transform(Vector3.Zero, p.BodyTransform(size)).Y));
        Assert.True(Reach(BlockFigure.Dead(false)) > Reach(BlockFigure.Parts(StanceId.Prone, false)) + 0.1f, "arms flung out");

        var stand = BlockFigure.Parts(StanceId.Standing, true);
        var half = BlockFigure.Fall(stand, 0.5f, true);
        Assert.InRange(Top(half, size), Top(dead, size) + 0.1f, Top(stand, size) - 0.1f);
        Assert.Same(stand, BlockFigure.Fall(stand, 0f, true));
        Assert.Same(dead, BlockFigure.Fall(stand, 1f, true));
    }

    /// <summary>⭐ W4 (VE-S) — a death SEEN happening falls over 0.6 s of sim time from the stance he died in (a pause freezes it); a
    /// body first seen already dead, or a death found across a jump, is simply lying there.</summary>
    [Fact]
    public void W4_AFallIsSimTime_FromHisLastStance_ABodyFoundDeadJustLies()
    {
        var falls = new DeathFall();
        var shot = new Entity(1, 1);
        falls.Alive(shot, StanceId.Crouched, 10.0);
        Assert.Equal((0f, StanceId.Crouched), falls.Dead(shot, 10.0));
        Assert.Equal(0.5f, falls.Dead(shot, 10.3).T, 3);
        Assert.Equal(0.5f, falls.Dead(shot, 10.3).T, 3);                   // paused: the same sim time, the same pose
        Assert.Equal(1f, falls.Dead(shot, 11.0).T);

        Assert.Equal((1f, StanceId.Standing), falls.Dead(new Entity(2, 1), 10.0));   // first seen dead: lying

        var jumped = new Entity(3, 1);
        falls.Alive(jumped, StanceId.Standing, 10.0);
        Assert.Equal(1f, falls.Dead(jumped, 40.0).T);                       // found dead 30 s later (a replay jump): no fall

        falls.EndFrame(new HashSet<Entity> { shot });
        Assert.Equal(1f, falls.Dead(jumped, 40.1).T);                       // forgotten, then first seen dead again
    }

    [Fact]
    public void S4_ARotorTurnsOnSimulationTimeWhileFlying_AndStandsWhenParked()
    {
        var t = new MotionTracker();
        var heli = new Entity(3, 1);
        var size = ShapeKits.DefaultSize(VisualFamily.Helicopter);
        var a = t.Advance(heli, new Vector3(0, 0, 30), Quaternion.Identity, VisualFamily.Helicopter, size, 10.00, rotorTurning: true);
        var b = t.Advance(heli, new Vector3(0, 0, 30), Quaternion.Identity, VisualFamily.Helicopter, size, 10.05, rotorTurning: true);
        Assert.NotEqual(a.Rotor, b.Rotor);
        var c = t.Advance(heli, new Vector3(0, 0, 30), Quaternion.Identity, VisualFamily.Helicopter, size, 10.05, rotorTurning: true);
        Assert.Equal(b.Rotor, c.Rotor);                                              // a paused sim clock freezes it
        Assert.Equal(0f, t.Advance(heli, Vector3.Zero, Quaternion.Identity, VisualFamily.Helicopter, size, 11.0, rotorTurning: false).Rotor);
        // the blades turn about their mast, the spokes about their wheel: the part's centre stays put
        var blade = ShapeKits.Helicopter.Parts.First(k => k.Role == PartRole.Rotor);
        var centre = Vector3.Transform(Vector3.Zero, blade.BodyTransform(size, default, ArticulationPose.Neutral, new MotionPose(1.2f, 0, 0)));
        Assert.InRange(Vector3.Distance(centre, Vector3.Transform(Vector3.Zero, blade.BodyTransform(size))), 0f, 1e-3f);
    }

    // ── S5c — the entity colour (§3.7, M15): built like affiliation ──

    [Fact]
    public void S5c_TheTkbColour_IsStampedOnlyIfAbsent_APerSpawnColourWins_AndNoColourIsStillStamped()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<VisualData>();
        w.RegisterComponent<EntityInfo>();
        w.RegisterComponent<EntityAppearance>();
        var tpl = new TkbTemplate("Painted", 90101);
        tpl.AddDescriptor(new VisualDefinitionDto { SymbolCode = "SFGPUCA---", ColorHex = "#2E4057" });
        var blank = new TkbTemplate("Blank", 90102);
        blank.AddDescriptor(new VisualDefinitionDto { SymbolCode = "SFGPUCA---", ColorHex = "" });
        var translator = new Hrot.Map.Definitions.Tkb.PresentationTkbTranslator();

        var fromTkb = w.CreateEntity();
        translator.Inject(w, fromTkb, tpl);
        var authored = w.CreateEntity();
        w.AddComponent(authored, EntityAppearance.Of(200, 30, 30));   // the scenario's own value, applied before / kept after
        translator.Inject(w, authored, tpl);
        var none = w.CreateEntity();
        translator.Inject(w, none, blank);

        Assert.Equal("#2E4057", w.GetComponentRO<EntityAppearance>(fromTkb).ToHex());
        Assert.Equal("#C81E1E", w.GetComponentRO<EntityAppearance>(authored).ToHex());
        Assert.True(w.HasComponent<EntityAppearance>(none), "always stamped — a [PerInstanceValue] component must never leave a ghost waiting");
        Assert.False(w.GetComponentRO<EntityAppearance>(none).HasColour);
        Assert.Contains(typeof(EntityAppearance), translator.GetProducedComponents());
    }

    [Fact]
    public void S5c_TheBodyColour_IsTheEntitysOwn_ThenTheTkbs_ThenTheFamilys()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<VisualData>();
        w.RegisterComponent<EntityAppearance>();
        var family = new Raylib_cs.Color((byte)1, (byte)2, (byte)3, (byte)255);
        var bare = w.CreateEntity();
        var tkbOnly = w.CreateEntity();
        w.AddComponent(tkbOnly, new VisualData { ColorHex = new FixedString32("#102030") });
        var own = w.CreateEntity();
        w.AddComponent(own, new VisualData { ColorHex = new FixedString32("#102030") });
        w.AddComponent(own, EntityAppearance.Of(250, 200, 10));
        var noColour = w.CreateEntity();
        w.AddComponent(noColour, new VisualData { ColorHex = new FixedString32("#102030") });
        w.AddComponent(noColour, default(EntityAppearance));

        Assert.Equal(family, EntityBodyLayer3D.ColourOf(w, bare, family));
        Assert.Equal(new Raylib_cs.Color((byte)0x10, (byte)0x20, (byte)0x30, (byte)255), EntityBodyLayer3D.ColourOf(w, tkbOnly, family));
        Assert.Equal(new Raylib_cs.Color((byte)250, (byte)200, (byte)10, (byte)255), EntityBodyLayer3D.ColourOf(w, own, family));
        Assert.Equal(new Raylib_cs.Color((byte)0x10, (byte)0x20, (byte)0x30, (byte)255), EntityBodyLayer3D.ColourOf(w, noColour, family));
    }

    [Fact]
    public void S5c_TheColour_IsSavedInTheScenario_AndComesBack()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<EntityAppearance>();
        var e = w.CreateEntity();
        w.AddComponent(e, EntityAppearance.FromHex("#C81E1E"));
        var serializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.Editor").Build();

        string json = serializer.Serialize(w, new Fdp.Toolkit.Scenario.ScenarioHeader("Hrot.Editor")).ToJsonString();
        Assert.Contains("\"EntityAppearance\"", json);

        using var back = new EntityRepository();
        back.RegisterComponent<EntityAppearance>();
        serializer.Deserialize(back, json);
        Entity loaded = default;
        int count = 0;
        foreach (var x in back.Query().With<EntityAppearance>().Build()) { loaded = x; count++; }
        Assert.Equal(1, count);   // the coloured entity comes back
        Assert.Equal("#C81E1E", back.GetComponentRO<EntityAppearance>(loaded).ToHex());
        Assert.Equal(0x2E4057AAu, EntityAppearance.FromHex("#2E4057AA").ColorRgba);   // #RRGGBBAA keeps its alpha
    }

    // ── S5 — the scenario's camera entity (§3.12, R-260) ──

    private static (Fdp.Toolkit.Vis2D.MapCanvas Canvas, MapViewSwitch Switch, MapCamera Two) Map(Vector2 centre, float zoom, Func<float, float, float>? ground = null)
    {
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var two = Camera2D(centre, zoom);
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = Viewport, GroundHeight = ground });
        return (canvas, sw, two);
    }

    private static EntityRepository CameraWorld()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        return w;
    }

    private static Entity SaveCamera(EntityRepository w, CameraPose pose)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, Hrot.UI.Common.Map3D.MapCameraPose.ToTransform(pose));
        w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Hrot.Core.Tkb.MapCameraTkb.Type });
        return e;
    }

    private static void AssertPose(CameraPose expected, CameraPose actual)
    {
        Assert.InRange(Vector3.Distance(expected.LookAt, actual.LookAt), 0f, 0.05f);
        Assert.InRange(MathF.Abs(expected.Distance - actual.Distance), 0f, 0.05f);
        Assert.InRange(MathF.Abs(MathF.IEEERemainder(expected.Yaw - actual.Yaw, 2f * MathF.PI)), 0f, 1e-3f);
        Assert.InRange(MathF.Abs(expected.Pitch - actual.Pitch), 0f, 1e-3f);
    }

    [Fact]
    public void S5_ThePose_RoundTripsThroughAnOrdinarySimTransform_OnASlope_AndOverhead()
    {
        // R-248: never flat — the look-at sits on a slope rising half a metre per metre east
        Func<float, float, float> slope = (x, y) => 0.5f * x;
        var tilted = new CameraPose(new Vector3(100f, 40f, 50f), 220f, 0.7f, -0.6f);
        var tf = Hrot.UI.Common.Map3D.MapCameraPose.ToTransform(tilted);
        AssertPose(tilted, Hrot.UI.Common.Map3D.MapCameraPose.FromTransform(tf, slope));
        Assert.True(tf.Position.Z > 50f, "the position is the EYE, above the look-at");

        var overhead = new CameraPose(new Vector3(-30f, 12f, -15f), 900f, MapCamera3D.NorthUpYaw, MapCamera3D.OverheadPitch);
        var back = Hrot.UI.Common.Map3D.MapCameraPose.FromTransform(Hrot.UI.Common.Map3D.MapCameraPose.ToTransform(overhead), slope);
        AssertPose(overhead, back);
        Assert.True(MapViewSwitch.IsTwoDPose(back), "straight down, north up = the 2-D map");
        Assert.False(MapViewSwitch.IsTwoDPose(tilted));
    }

    [Fact]
    public void S5_TheSavedView_IsAdoptedWhenTheScenarioOpens_In3D_OrIn2D_ByItsPitch()
    {
        using var w = CameraWorld();
        var saved3D = new CameraPose(new Vector3(500f, 200f, 0f), 350f, 1.1f, -0.5f);
        SaveCamera(w, saved3D);
        var (canvas, sw, _) = Map(Vector2.Zero, 1f);
        var layer = new Hrot.UI.Common.Map3D.MapCameraLayer(sw, () => w);

        layer.Update(0.016f);

        Assert.True(layer.Adopted);
        Assert.True(sw.Is3D);
        AssertPose(saved3D, sw.Camera3D.Pose);

        using var w2 = CameraWorld();
        SaveCamera(w2, new CameraPose(new Vector3(-80f, 60f, 0f), 400f, MapCamera3D.NorthUpYaw, MapCamera3D.OverheadPitch));
        var (canvas2, sw2, two2) = Map(new Vector2(900f, 900f), 3f);
        new Hrot.UI.Common.Map3D.MapCameraLayer(sw2, () => w2).Update(0.016f);
        Assert.False(sw2.Is3D);
        Assert.Same(two2, canvas2.Camera);
        Assert.InRange(Vector2.Distance(two2.ScreenToWorld(Viewport / 2f), new Vector2(-80f, 60f)), 0f, 0.1f);
    }

    [Fact]
    public void S5_TheFirstMove_RequestsAnUnownedCamera_TheCreatorOwnsIt_AndItsPoseIsKept_AndSaved()
    {
        using var w = CameraWorld();
        var requests = new Hrot.Core.Network.ScenarioEntityCreationRequestSource();
        w.SetSingletonManaged(requests);                       // as the shared EntityCreationPack publishes it on every creating host
        var (_, sw, two) = Map(new Vector2(10f, 20f), 2f);
        var layer = new Hrot.UI.Common.Map3D.MapCameraLayer(sw, () => w);
        layer.Update(0.016f);                                  // the baseline
        Assert.True(requests.IsEmpty);

        two.ApplyCameraView(new MapCameraView { Target = new Vector2(300f, 400f), Zoom = 2f, SmoothTarget = new Vector2(300f, 400f), SmoothZoom = 2f });
        layer.Update(0.016f);                                  // a move …
        Assert.True(requests.IsEmpty);                         // … nothing asked while the view is still changing
        for (int i = 0; i < 40; i++) layer.Update(0.016f);     // still for > SettleSeconds

        var asked = new List<Hrot.Core.Network.EntityCreationRequest>();
        requests.ProcessRequests(asked.Add);
        var r = Assert.Single(asked);                          // one request, not one per frame
        Assert.Equal(Hrot.Core.Tkb.MapCameraTkb.Type, r.TkbType);
        Assert.Equal(Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor, r.OwnerAppInstanceId);   // R-140: unowned
        Assert.True(layer.Requested);

        // the default processor creates it (here: locally, as the editor's one-node cluster does)
        var cam = w.CreateEntity();
        w.AddComponent(cam, (SimTransform)r.InitialComponents![0]);
        w.AddComponent(cam, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = r.TkbType });
        for (int i = 0; i < 40; i++) layer.Update(0.016f);
        Assert.Equal(cam, layer.Camera);
        Assert.False(layer.Requested);
        AssertPose(sw.CurrentPose, Hrot.UI.Common.Map3D.MapCameraPose.FromTransform(w.GetComponentRO<SimTransform>(cam), null));

        sw.Set(true, animate: false);                          // switching to 3-D is a camera move: the owner keeps it
        for (int i = 0; i < 40; i++) layer.Update(0.016f);
        Assert.False(MapViewSwitch.IsTwoDPose(Hrot.UI.Common.Map3D.MapCameraPose.FromTransform(w.GetComponentRO<SimTransform>(cam), null)));
        Assert.True(requests.IsEmpty);                         // moved, never asked for a second one
        Assert.Equal(1, w.EntityCount);

        var serializer = new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.Editor").Build();
        string json = serializer.Serialize(w, new Fdp.Toolkit.Scenario.ScenarioHeader("Hrot.Editor")).ToJsonString();
        Assert.Contains("\"SimTransform\"", json);           // an ordinary saved entity (U7)
    }

    [Fact]
    public void S5_AHostThatCannotCreate_NeverMakesACamera_AndAViewTheUserAlreadyMovedIsNotTakenOver()
    {
        using var w = CameraWorld();                           // no request source: the replay browser
        var (_, sw, two) = Map(Vector2.Zero, 1f);
        var layer = new Hrot.UI.Common.Map3D.MapCameraLayer(sw, () => w);
        layer.Update(0.016f);
        two.ApplyCameraView(new MapCameraView { Target = new Vector2(50f, 50f), Zoom = 1f, SmoothTarget = new Vector2(50f, 50f), SmoothZoom = 1f });
        for (int i = 0; i < 40; i++) layer.Update(0.016f);
        Assert.Equal(0, w.EntityCount);
        Assert.False(layer.Requested);

        SaveCamera(w, new CameraPose(new Vector3(999f, 999f, 0f), 300f, 0.3f, -0.4f));   // arrives after the user moved
        for (int i = 0; i < 40; i++) layer.Update(0.016f);
        Assert.False(sw.Is3D);
        Assert.False(layer.Adopted);
    }

    [Fact]
    public void S5_ACameraTheMapDoesNotOwn_IsNotMoved()
    {
        using var w = CameraWorld();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>();
        var cam = SaveCamera(w, new CameraPose(new Vector3(100f, 100f, 0f), 300f, 0.3f, -0.4f));
        w.AddComponent(cam, new Fdp.Toolkit.Replication.Components.NetworkAuthority(primaryOwnerId: 1, localNodeId: 2));   // owned elsewhere
        var before = w.GetComponentRO<SimTransform>(cam);
        var (_, sw, _) = Map(Vector2.Zero, 1f);
        var layer = new Hrot.UI.Common.Map3D.MapCameraLayer(sw, () => w);
        layer.Update(0.016f);                                  // adopts it
        Assert.True(layer.Adopted);
        sw.Camera3D.Pose = sw.Camera3D.Pose with { Yaw = 2.0f };
        for (int i = 0; i < 40; i++) layer.Update(0.016f);
        Assert.Equal(before.Position, w.GetComponentRO<SimTransform>(cam).Position);   // a request needs a pitch path (CE-1047)
    }

    [Fact]
    public void S5_TheCameraIsTheView_NotAThingOnTheMap_NoBody_NoPickBox_NoSymbol()
    {
        var bodies = new EntityBodyLayer3D(() => null, () => null);
        Assert.Equal(VisualFamily.Unit, bodies.LookOf(Hrot.Core.Tkb.MapCameraTkb.Type, null).Family);   // no body, not pickable

        using var w = CameraWorld();
        var cam = SaveCamera(w, new CameraPose(Vector3.Zero, 300f, 0.3f, -0.4f));
        w.AddComponent(cam, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4711));   // as it comes back from a load
        var draw = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        new Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo().Draw(w, cam, draw);
        Assert.Equal(0, draw.GetFrame().Length);

        var tkb = HrotEnvironment.CreateTkb();
        Assert.True(tkb.TryGetByType(Hrot.Core.Tkb.MapCameraTkb.Type, out var t) && t!.GetDescriptor<TkbMasterDto>()!.HideFromPalette);
    }

    // ── WL-1 — the drawn turret follows its part's TurretPose (docs/DESIGN_Weapon_Layers.md W-9, §3.11 M23) ──

    /// <summary>⭐ WL-1 — the 3-D map poses a kit from turret 0's <c>TurretPose</c>, read LIVE (a turn shows the next frame with no
    /// rebuild), and falls back to forward-and-level when the unit has no turret or the turret part is gone.</summary>
    [Fact]
    public void WL1_TheDrawnTurret_FollowsItsPartsPose_ReadLive()
    {
        using var w = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(w);
        var tank = w.CreateEntity();
        var bare = w.CreateEntity();
        var turret = Fdp.Toolkit.Combat.TurretParts.Ensure(w, tank, 0);
        Assert.False(turret.IsNull, "every shared-registry node can build a turret part");
        var source = new TurretPoseSource();

        Assert.Equal(ArticulationPose.Neutral, source.PoseOf(w, tank));
        w.GetComponentRW<Fdp.Toolkit.Combat.Components.TurretPose>(turret) =
            new Fdp.Toolkit.Combat.Components.TurretPose { Azimuth = 0.7f, Elevation = 0.1f };
        Assert.Equal(new ArticulationPose(0.7f, 0.1f), source.PoseOf(w, tank));
        Assert.Equal(ArticulationPose.Neutral, source.PoseOf(w, bare));
        w.DestroyEntity(turret);
        Assert.Equal(ArticulationPose.Neutral, source.PoseOf(w, tank));
    }

    /// <summary>⭐ WL-1 — end to end from the catalog: an M1 spawned through the base translator set on a shared-registry world gets
    /// one turret part, a Bradley's two weapons share its one turret, and every catalog weapon carries a category.</summary>
    [Fact]
    public void WL1_CatalogTanks_GetATurretPart_ThroughTheBaseTranslators()
    {
        var tkb = HrotEnvironment.CreateTkb();
        using var w = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(w);
        var translators = TkbTranslatorSet.Base();
        foreach (var type in new[] { TkbEntityTypes.Tank_M1Abrams, TkbEntityTypes.IFV_Bradley, TkbEntityTypes.Tank_T72 })
        {
            Assert.True(tkb.TryGetByType(type, out var tpl));
            var e = w.CreateEntity();
            foreach (var t in translators) t.Inject(w, e, tpl!);
            Assert.False(Fdp.Toolkit.Combat.TurretParts.Find(w, e, 0).IsNull, $"type {type} has no turret part");
            Assert.True(Fdp.Toolkit.Combat.TurretParts.Find(w, e, 1).IsNull);
            var mounts = tpl!.GetDescriptor<WeaponSuiteDto>()!.Mounts;
            Assert.All(mounts, m => Assert.Equal(0, m.TurretIndex));
            Assert.All(mounts, m => Assert.NotEqual(WeaponCategory.Unknown, m.Category));
        }
        Assert.True(tkb.TryGetByType(TkbEntityTypes.Infantry_Rifleman, out var rifleman));
        Assert.Null(rifleman!.GetDescriptor<TurretSuiteDto>());
        Assert.Equal(-1, rifleman.GetDescriptor<WeaponSuiteDto>()!.Mounts[0].TurretIndex);
    }

    // ── E4b — the drawn barrel tip IS Muzzle.Of (docs/DESIGN_Visual_Effects.md VE-L, §6b) ──

    /// <summary>⭐ E4b (VE-L) — for every catalog turreted type, the drawn gun's tip (the kit placed from the TKB, posed by the turret
    /// part) is within 5 cm of <see cref="Fdp.Toolkit.Combat.Muzzle.TryOf"/> — where the round leaves and the flash and tracer start —
    /// on a turned, sloped hull with the turret traversed and the gun elevated. (The family kit's own proportions put an M1's tip at
    /// 5.55 m ahead of the reference point against the TKB's 6.4 m — ~0.85 m off, which is what placing the kit from the TKB removes.)</summary>
    [Theory]
    [InlineData(TkbEntityTypes.Tank_M1Abrams)]
    [InlineData(TkbEntityTypes.Tank_T72)]
    [InlineData(TkbEntityTypes.IFV_Bradley)]
    public void E4b_TheDrawnBarrelTip_IsTheMuzzle_Within5cm(long type)
    {
        var tkb = HrotEnvironment.CreateTkb();
        using var w = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(w);
        w.SetSingletonManaged<ITkbDatabase>(tkb);
        Assert.True(tkb.TryGetByType(type, out var tpl));
        var tank = w.CreateEntity();
        w.AddComponent(tank, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = type });   // the spawn path stamps it
        foreach (var t in TkbTranslatorSet.Base()) t.Inject(w, tank, tpl!);
        var hullRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.1f);
        w.SetComponent(tank, new SimTransform { Position = new Vector3(30f, -20f, 4f), Rotation = hullRotation });
        var turret = Fdp.Toolkit.Combat.TurretParts.Find(w, tank, 0);
        w.SetComponent(turret, new Fdp.Toolkit.Combat.Components.TurretPose { Azimuth = 1.1f, Elevation = 0.15f });

        var bodies = new EntityBodyLayer3D(() => w, () => tkb, new TurretPoseSource().PoseOf);
        var look = bodies.LookOf(type, tkb);
        var (parts, pivots) = bodies.PlacedKit(ShapeKits.For(look.Family), type, tkb, look.Size);
        var gun = parts.First(p => p.Role == PartRole.Gun);
        var pose = new TurretPoseSource().PoseOf(w, tank);
        var body = Matrix4x4.CreateFromQuaternion(hullRotation) * Matrix4x4.CreateTranslation(new Vector3(30f, -20f, 4f));
        var tip = Vector3.Transform(new Vector3(0f, 0f, 1f), gun.BodyTransform(look.Size, pivots, pose) * body);   // the unit cylinder's far end

        Assert.True(Fdp.Toolkit.Combat.Muzzle.TryOf(w, tank, 0, out var muzzle));
        Assert.True(Vector3.Distance(tip, muzzle) < 0.05f, $"drawn tip {tip} vs muzzle {muzzle}: {Vector3.Distance(tip, muzzle):F3} m");
    }

    // ── CE-1040 — the 2-D map is north-up (docs/DESIGN_Map_North_Up.md) ──

    [Fact]
    public void CE1040_In2D_NorthIsUp_EastIsRight_AndTheConversionsAreInverses()
    {
        var two = Camera2D(new Vector2(40, -60), 3f);
        var centre = two.WorldToScreen(new Vector2(40, -60));
        var north = two.WorldToScreen(new Vector2(40, -40));
        var east = two.WorldToScreen(new Vector2(60, -60));
        Assert.True(north.Y < centre.Y - 10f, $"north {north} should be ABOVE the centre {centre}");
        Assert.True(east.X > centre.X + 10f, $"east {east} should be right of the centre {centre}");
        Assert.InRange(Vector2.Distance(centre, Viewport / 2f), 0f, 0.01f);
        foreach (var px in new[] { new Vector2(0, 0), new Vector2(200, 650), new Vector2(1279, 3) })
            Assert.InRange(Vector2.Distance(px, two.WorldToScreen(two.ScreenToWorld(px))), 0f, 0.01f);
        Assert.Equal(-1f / 3f, two.ScreenDeltaToWorld(new Vector2(0, 1)).Y, 4);   // a pixel DOWN is south
    }

    [Fact]
    public void CE1040_DraggingTheMapDown_MovesTheViewNorth_AndTheWheelZoomsAboutTheCursor()
    {
        var two = Camera2D(new Vector2(0, 0), 2f);
        two.EnableSmoothing = false;
        var under = two.ScreenToWorld(new Vector2(100, 100));
        two.ProcessInput(0f, new Vector2(100, 100), isPanDown: true, isInputCaptured: false);
        two.ProcessInput(0f, new Vector2(100, 160), isPanDown: true, isInputCaptured: false);   // drag 60 px down
        two.Update(0.016f);
        // the ground point that was under the cursor followed it down
        Assert.InRange(Vector2.Distance(two.WorldToScreen(under), new Vector2(100, 160)), 0f, 0.01f);

        two.ProcessInput(0f, new Vector2(100, 160), isPanDown: false, isInputCaptured: false);
        var atCursor = two.ScreenToWorld(new Vector2(300, 500));
        two.ProcessInput(2f, new Vector2(300, 500), isPanDown: false, isInputCaptured: false);
        two.Update(0.016f);
        Assert.InRange(Vector2.Distance(two.ScreenToWorld(new Vector2(300, 500)), atCursor), 0f, 0.01f);
    }

    [Fact]
    public void CE1040_TheSwitchKeepsNorthUp_ThePointNorthOfTheCentreIsAboveItInBothViews()
    {
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var two = Camera2D(new Vector2(500, 200), 1.2f);
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = Viewport });
        var northOf = new Vector2(500, 260);

        float twoDy = two.WorldToScreen(northOf).Y;
        sw.Set(true, animate: false);
        sw.Camera3D.Pose = sw.Camera3D.OverheadMatching(two);
        float threeDy = sw.Camera3D.WorldToScreen(northOf).Y;
        Assert.True(twoDy < 360f && threeDy < 360f, $"north is above the centre in 2-D ({twoDy}) and in 3-D ({threeDy})");
        Assert.InRange(MathF.Abs(twoDy - threeDy), 0f, 2f);   // the same pixel: the swap does not jump
        sw.Set(false, animate: false);
        Assert.True(two.WorldToScreen(northOf).Y < 360f);
    }

    [Fact]
    public void S1_TheCamera_RefusesToBePoisoned()
    {
        var three = new MapCamera3D();
        three.Pose = new CameraPose(new Vector3(float.NaN, 0, 0), float.NaN, float.NaN, float.NaN);
        three.Update(0.016f);
        Assert.True(float.IsFinite(three.LookAt.X) && float.IsFinite(three.Distance) && float.IsFinite(three.Pitch));
        Assert.True(float.IsFinite(three.Zoom));
    }

    // ── coordinates and terrain ──

    [Fact]
    public void S1_TheModelMatrix_PutsAHrotPointWhereRaylibExpectsIt()
    {
        var pos = new Vector3(10, 20, 3);
        var m = HrotToRaylib.Model(pos, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f), new Vector3(4, 2, 1));
        // Raylib applies the matrix to column vectors: undo the transpose to apply it with System.Numerics.
        var row = Matrix4x4.Transpose(m);
        Assert.Equal(HrotToRaylib.Position(pos), Vector3.Transform(Vector3.Zero, row));
        // The mesh's local +X (forward, length 4) turned 90° left about up ⇒ HROT +Y ⇒ Raylib −Z.
        var tip = Vector3.Transform(new Vector3(0.5f, 0, 0), row) - HrotToRaylib.Position(pos);
        Assert.InRange(Vector3.Distance(tip, new Vector3(0, 0, -2f)), 0f, 1e-4f);
    }

    /// <summary>⭐ CE-1034 H5d — the highway-rest terrain: a height grid, a highway under an overpass to a drivers' rest area.</summary>
    internal static TerrainWorld HighwayRest()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Path.GetDirectoryName(dir)!;
        var file = Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "highway-rest", "highway-rest.world.geojson");
        return TerrainWorldParser.Parse(File.ReadAllText(file), "highway-rest", TerrainAssets.ForFolder(Path.GetDirectoryName(file)!));
    }

    internal static TerrainWorld TestTown()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Path.GetDirectoryName(dir)!;
        var file = Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "test-town", "test-town.world.geojson");
        return TerrainWorldParser.Parse(File.ReadAllText(file), "test-town", TerrainAssets.ForFolder(Path.GetDirectoryName(file)!));
    }

    [Fact]
    public void S1_TestTown_RendersAsGroundWallsAndRoofs_AllFacingUpOrOut()
    {
        var geometry = WorldQuery.RenderGeometryOf(WorldWith(TestTown()))!;
        Assert.NotNull(geometry);
        geometry.Build(out var verts, out var indices, out var kinds);
        Assert.Contains(TerrainSurfaceKind.Ground, kinds);
        Assert.Contains(TerrainSurfaceKind.Wall, kinds);
        Assert.Contains(TerrainSurfaceKind.Roof, kinds);

        TerrainLayer3D.BuildVertexData(verts, indices, kinds, out var positions, out var normals, out var colours);
        Assert.Equal(indices.Length, positions.Length);
        for (int t = 0; t < kinds.Length; t++)
        {
            if (kinds[t] == TerrainSurfaceKind.Wall) continue;
            Assert.True(normals[3 * t].Y > 0.5f, $"ground/roof triangle {t} faces {normals[3 * t]} (Raylib +Y is up)");
            Assert.Equal(TerrainLayer3D.ColourOf(kinds[t]), colours[3 * t]);
        }
    }

    internal static Fdp.Core.EntityRepository WorldWith(TerrainWorld terrain)
    {
        var repo = new Fdp.Core.EntityRepository();
        repo.SetSingletonManaged(terrain);
        return repo;
    }
}
