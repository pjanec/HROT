using System.Numerics;
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
        var target = centre - vp / 2f / zoom;
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
