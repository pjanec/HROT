using System.Linq;
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
        CivilianTkbCatalog.RegisterAll(db);   // ⭐ CE-1162 — backend's civilian family + security forces (CE-3220), as HrotEnvironment registers them
        return db;
    }

    /// <summary>The icon fallback exactly as it was before the extraction — the oracle that the extraction changed nothing.</summary>
    private static string? OldFallbackIconName(TkbTemplate t)
    {
        if (t.GetDescriptor<TkbCompositionDef>() is not null) return "_unit";
        var d = t.DisType;
        if (d.Kind == 3) return "_person";
        if (d.Kind == 1 && d.Domain == 1)
            return d.Category switch { 1 or 4 => "_tank", 2 => "_afv", 3 or 6 or 7 or 81 => "_wheeled", _ => null };   // ⭐ IF-1: 4 (SP artillery) moved on purpose
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

    /// <summary>
    /// ⭐⭐⭐ CE-1054 — the 3-D map's ground height resolves the terrain ONCE per tick, not once per point. 🔒 User, 2026-10-11: "the 3d
    /// view had extremely low fps (1 FPS or so)". 🔴 Every draped gizmo point re-resolved the whole terrain query; a scene of ~2 250 lines
    /// at up to 160 points each is hundreds of thousands of resolutions a frame.
    /// </summary>
    [Fact]
    public void CE1054_TheGroundHeight_ResolvesTheTerrainOncePerTick_NotOncePerPoint()
    {
        var repo = new EntityRepository();
        repo.SetSingletonManaged(new TerrainWorld { Name = "t" });
        int asked = 0;
        long tick = 1;
        var ground = new CachedGroundHeight(() => { asked++; return repo; }, () => tick);

        for (int i = 0; i < 5000; i++) ground.At(i, i * 0.5f);
        Assert.Equal(1, asked);                              // 5 000 points, one resolution

        tick = 2;                                            // the next frame re-reads (a scenario load can replace the terrain)
        ground.At(0, 0);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void CE1054_TheGroundHeight_WithoutATerrain_IsZero_AndStillResolvesOnce()
    {
        int asked = 0;
        var ground = new CachedGroundHeight(() => { asked++; return null; }, () => 7);
        Assert.Equal(0f, ground.At(10, 10));
        Assert.Equal(0f, ground.At(20, 20));
        Assert.Equal(1, asked);
    }

    /// <summary>
    /// ⭐⭐ CE-1142 (supersedes CE-1044's "a tenth of the distance") — the fly speed follows the EYE's height above the ground, starts at a
    /// quarter, reaches full in a second and keeps accelerating to 5× while held; Shift ×4, Ctrl ×20, both ×100 — distinct. 🔒 User,
    /// 2026-10-16: "slower starting speed with acceleration desired … Ctrl+alt+shift modifiers are somewhat weak, similar to each other".
    /// </summary>
    [Fact]
    public void CE1142_TheFlySpeed_FollowsTheEyeHeight_StartsSlow_Accelerates_AndTheModifiersAreDistinct()
    {
        Assert.Equal(4f, MapCamera3D.FlySpeed(10f, 1f, false, false), 3);            // 10 m up: 4 m/s once ramped
        Assert.Equal(1f, MapCamera3D.FlySpeed(10f, 0f, false, false), 3);            // the press: a quarter
        Assert.Equal(20f, MapCamera3D.FlySpeed(10f, 5f, false, false), 3);           // held 5 s: 5×
        Assert.Equal(MapCamera3D.FlyMinSpeed, MapCamera3D.FlySpeed(0.5f, 1f, false, false), 3);   // on the ground: the floor
        Assert.Equal(16f, MapCamera3D.FlySpeed(10f, 1f, true, false), 3);
        Assert.Equal(80f, MapCamera3D.FlySpeed(10f, 1f, false, true), 3);
        Assert.Equal(400f, MapCamera3D.FlySpeed(10f, 1f, true, true), 3);
    }

    /// <summary>⭐ CE-1142 — a metre above a soldier with the pivot far ahead, a short tap moves centimetres, not the 30 m/s the old
    /// distance-based speed flew (Distance 300 ⇒ 30 m/s).</summary>
    [Fact]
    public void CE1142_CloseToTheGround_ATapMovesCentimetres_WhateverTheLookDistance()
    {
        var cam = FlyCamera();
        cam.Pose = new CameraPose(new Vector3(300f, 0f, 0f), 300f, 0f, -0.004f);   // the eye ~1.2 m up, looking 300 m ahead
        var input = new FakeInput { RightDown = true, MousePosition = new Vector2(600, 400) };
        Frame(cam, input);                                  // arms the look
        input.Keys.Add((int)Raylib_cs.KeyboardKey.W);
        var eye = cam.Eye;
        for (int i = 0; i < 12; i++) Frame(cam, input);     // ~0.2 s
        Assert.InRange(Vector3.Distance(eye, cam.Eye), 0.005f, 0.3f);
    }

    /// <summary>
    /// ⭐⭐ CE-1138 — a LONE wheel click is a small step (4 % of the distance, was 12 %); clicks in quick succession grow it, so a fast
    /// spin travels far. 🔒 User, 2026-10-16: "the mouse scroll zoom in 3d map still seem to make a very big jump … multiple wheel
    /// clicks needed to switch to faster mode for travelling long distances."
    /// </summary>
    [Fact]
    public void CE1138_ALoneClickNudges_AFastSpinTravelsFar()
    {
        var lone = FlyCamera();
        var input = new FakeInput { MousePosition = new Vector2(600, 400) };
        for (int c = 0; c < 5; c++)                         // five clicks a second apart
        {
            input.MouseWheelMove = 1f; Frame(lone, input, 0.016f);
            input.MouseWheelMove = 0f; for (int i = 0; i < 60; i++) Frame(lone, input, 0.016f);
        }
        var spin = FlyCamera();
        for (int c = 0; c < 5; c++)                         // the same five, 50 ms apart
        {
            input.MouseWheelMove = 1f; Frame(spin, input, 0.016f);
            input.MouseWheelMove = 0f; for (int i = 0; i < 3; i++) Frame(spin, input, 0.016f);
        }
        for (int i = 0; i < 120; i++) Frame(spin, input, 0.016f);
        Assert.Equal(100f * MathF.Pow(0.96f, 5), lone.Distance, 1);   // five lone nudges: ~18 m
        Assert.True(100f - spin.Distance >= 1.8f * (100f - lone.Distance),
            $"the spin went to {spin.Distance:F1} m, the lone clicks to {lone.Distance:F1} m");   // the same clicks, fast: ~twice as far
    }

    /// <summary>
    /// ⭐⭐ CE-1072 (docs/DESIGN_Map_3D_Mode.md §6l) — a wheel click GLIDES (over a few frames) to its step instead of JUMPING
    /// (⭐ CE-1138: a lone click's step is 4 % now, was 12 %).
    /// 🔒 User, 2026-10-14: "the scroll button based camera moving forwards and backwards jumps with each click instead of being a
    /// smooth flight".
    /// </summary>
    [Fact]
    public void CE1072_AWheelClick_GlidesToItsStep_OverSeveralFrames_InsteadOfJumping()
    {
        var cam = FlyCamera();                              // distance 100
        var input = new FakeInput { MousePosition = new Vector2(600, 400), MouseWheelMove = 1f };
        Frame(cam, input);
        input.MouseWheelMove = 0f;
        Assert.Equal(100f, cam.Distance, 3);                // the click frame: nothing jumped
        Frame(cam, input);
        Assert.InRange(cam.Distance, 96.01f, 99.9f);        // the next frame: on the way, not there
        float previous = cam.Distance;
        for (int i = 0; i < 60; i++) Frame(cam, input);
        Assert.True(cam.Distance < previous);
        Assert.Equal(96f, cam.Distance, 1);                 // a lone click's 4 % (CE-1138)

        input.MouseWheelMove = -1f;                         // and back out the same way
        Frame(cam, input);
        input.MouseWheelMove = 0f;
        for (int i = 0; i < 60; i++) Frame(cam, input);
        Assert.Equal(100f, cam.Distance, 0);
    }

    /// <summary>⭐ CE-1072 — at the pivot the wheel keeps FLYING forward (the look-at point travels with the eye), Unity-style,
    /// instead of stopping at the closest distance.</summary>
    [Fact]
    public void CE1072_AtThePivot_TheWheelKeepsFlyingForward()
    {
        var cam = FlyCamera();
        cam.Pose = new CameraPose(Vector3.Zero, cam.MinDistance, MapCamera3D.NorthUpYaw, -0.3f);
        var eye = cam.Eye;
        var forward = cam.Forward;
        var input = new FakeInput { MousePosition = new Vector2(600, 400), MouseWheelMove = 1f };
        for (int i = 0; i < 3; i++) Frame(cam, input);      // three clicks
        input.MouseWheelMove = 0f;
        for (int i = 0; i < 60; i++) Frame(cam, input);
        var flown = cam.Eye - eye;
        Assert.Equal(cam.MinDistance, cam.Distance, 3);
        Assert.True(flown.Length() > 3f, $"flew only {flown.Length():F2} m");
        Assert.True(Vector3.Dot(Vector3.Normalize(flown), forward) > 0.999f, "along the view");
    }

    /// <summary>⭐ CE-1072 — W eases in and out over a few frames: the first frame moves less than a settled frame, and letting go
    /// does not stop dead.</summary>
    [Fact]
    public void CE1072_TheFly_EasesInAndOut()
    {
        var cam = FlyCamera();
        var input = new FakeInput { RightDown = true, MousePosition = new Vector2(600, 400) };
        Frame(cam, input);                                  // arms the look
        input.Keys.Add((int)Raylib_cs.KeyboardKey.W);
        var e0 = cam.Eye; Frame(cam, input); float first = Vector3.Distance(e0, cam.Eye);
        for (int i = 0; i < 30; i++) Frame(cam, input);
        var e1 = cam.Eye; Frame(cam, input); float settled = Vector3.Distance(e1, cam.Eye);
        input.Keys.Clear();
        var e2 = cam.Eye; Frame(cam, input); float coasting = Vector3.Distance(e2, cam.Eye);
        Assert.True(first < settled * 0.5f, $"first {first:F3} m vs settled {settled:F3} m");
        Assert.InRange(coasting, 0.001f, settled * 0.95f);
    }

    /// <summary>
    /// ⭐ CE-1073 (§6l) — the head is part <see cref="BlockFigure.HeadIndex"/> of EVERY figure (each stance, soldier or not, each
    /// posture, the dead): the first-person eye is read from it, so a reordered pose would put the camera in a leg.
    /// </summary>
    [Fact]
    public void CE1073_EveryFigurePose_KeepsTheHead_AtHeadIndex()
    {
        var metres = new Vector3(0.35f, 0.55f, 1.8f);
        // the head is one 0.20 × 0.21 × 0.24 m box in every pose — lying down (prone, dead) it is the same box on its side
        string Box(KitPart p)
        {
            var m = p.Extent * metres;
            return string.Join(" ", new[] { m.X, m.Y, m.Z }.Select(v => v.ToString("0.00")).OrderBy(v => v));
        }
        var head = BlockFigure.Parts(StanceId.Standing, soldier: false)[BlockFigure.HeadIndex];
        Assert.Equal(1.60f / 1.8f, head.Centre.Z, 3);
        Assert.Equal("0.20 0.21 0.24", Box(head));
        foreach (StanceId stance in new[] { StanceId.Standing, StanceId.Crouched, StanceId.Prone })
            foreach (bool soldier in new[] { false, true })
                foreach (WeaponPostureKind posture in System.Enum.GetValues<WeaponPostureKind>())
                {
                    var parts = BlockFigure.Parts(stance, soldier, posture);
                    Assert.True(parts.Length > BlockFigure.HeadIndex);
                    Assert.True(Box(head) == Box(parts[BlockFigure.HeadIndex]), $"{stance}/{soldier}/{posture}");
                }
        // the dead man's head is a touch smaller (0.22 m) — still a head-sized box: no side under 0.18 m or over 0.26 m (a torso or a limb fails)
        var dead = BlockFigure.Fall(BlockFigure.Parts(StanceId.Standing, true), 1f, true)[BlockFigure.HeadIndex].Extent * metres;
        Assert.All(new[] { dead.X, dead.Y, dead.Z }, v => Assert.InRange(MathF.Abs(v), 0.18f, 0.26f));
    }

    /// <summary>
    /// ⭐⭐ CE-1073 — the eye goes WITH the drawn head: a running lean carries it forward and down, a crouch lowers it, prone puts it
    /// near the ground. 🔒 User, 2026-10-14: "When moving forward the character leans a bit forward basically putting his head in front
    /// of the FPS camera so i see nothing".
    /// </summary>
    [Fact]
    public void CE1073_TheEye_FollowsTheDrawnHead_LeanCrouchProne()
    {
        var size = new Vector3(0.35f, 0.55f, 1.8f);
        Vector3 Eye(StanceId stance, MotionPose motion)
            => EntityBodyLayer3D.EyeLocal(BlockFigure.Parts(stance, true)[BlockFigure.HeadIndex], size, default,
                                         ArticulationPose.Neutral, motion, Matrix4x4.Identity);
        var upright = Eye(StanceId.Standing, MotionPose.Still with { HipZ = 0.5f });
        Assert.InRange(upright.Z, 1.55f, 1.75f);
        Assert.InRange(upright.X, 0.05f, 0.15f);              // the face, ahead of the reference point
        var leaning = Eye(StanceId.Standing, MotionPose.Still with { HipZ = 0.5f, Lean = 0.35f });
        Assert.True(leaning.X > upright.X + 0.2f, $"a run's lean carries the eye forward: {upright} -> {leaning}");
        Assert.True(leaning.Z < upright.Z, "and down");
        Assert.True(Eye(StanceId.Crouched, MotionPose.Still with { HipZ = BlockFigure.HipFraction(StanceId.Crouched) }).Z < 1.3f);
        Assert.True(Eye(StanceId.Prone, MotionPose.Still).Z < 0.6f);
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
    [InlineData(CivilianTkbCatalog.TkbCivilianAdultMale, VisualFamily.Person)]
    [InlineData(TkbEntityTypes.Unit_TankPlatoon, VisualFamily.Unit)]
    public void S1_BuiltInTypes_MapToTheirKit(long type, VisualFamily expected)
        => Assert.Equal(expected, VisualFamilies.Of(BuiltIn().GetByType(type)));

    [Fact]
    public void S1_ASoldierCarriesARifle_ACivilianDoesNot()
    {
        var db = BuiltIn();
        Assert.True(EntityBodyLayer3D.Resolve(db.GetByType(TkbEntityTypes.Infantry_Rifleman)).Soldier);
        Assert.False(EntityBodyLayer3D.Resolve(db.GetByType(CivilianTkbCatalog.TkbCivilianAdultMale)).Soldier);
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

    /// <summary>⭐ CE-2138 (C2) — the family switch exactly as it was before the class table: the oracle the sweep compares to.</summary>
    private static VisualFamily OldFamily(DISEntityType d)
    {
        if (d.Kind == 3) return VisualFamily.Person;
        if (d.Kind == 1 && d.Domain == 1)
            return d.Category switch
            {
                1 => VisualFamily.Tank, 2 => VisualFamily.Afv, 81 => VisualFamily.WheeledCar,
                3 or 6 or 7 => VisualFamily.WheeledUtility,
                4 => VisualFamily.SpArtillery, 5 or 10 or 50 => VisualFamily.WeaponSystem,   // ⭐ CE-1087 / CE-1089 — deliberately moved off Unknown
                _ => VisualFamily.Unknown,
            };
        if (d.Kind == 1 && d.Domain == 2)
            return d.Category switch
            {
                20 => VisualFamily.AttackHelicopter,   // ⭐ CE-1103 — the attack helicopter has its own kit now
                >= 21 and <= 25 => VisualFamily.Helicopter,
                2 => VisualFamily.AttackJet,   // ⭐ CE-1092 — the attack plane has its own kit now
                1 or 6 or 7 or 40 or 47 or 50 => VisualFamily.Jet,
                3 or 4 or 5 or 8 or 57 => VisualFamily.CargoPlane,
                _ => VisualFamily.Unknown,
            };
        return VisualFamily.Unknown;
    }

    /// <summary>⭐ CE-2138 (C2) — <c>VisualFamilies.Of</c> rebuilt on the class table keeps the family of EVERY Kind × Domain ×
    /// Category (countries and subcategories too), not just the built-in types.</summary>
    [Fact]
    public void CE2138_TheClassTable_KeepsEveryFamily_AcrossTheWholeDisSpace()
    {
        var moved = new List<string>();
        foreach (byte kind in new byte[] { 0, 1, 2, 3, 4, 9, 10, 12 })
            for (int domain = 0; domain <= 6; domain++)
                for (int category = 0; category <= 255; category++)
                    foreach (ushort country in new ushort[] { 0, 222, 225 })
                    {
                        var d = new DISEntityType { Kind = kind, Domain = (byte)domain, Category = (byte)category, Country = country, Subcategory = 3 };
                        var now = VisualFamilies.Of(new TkbTemplate("sweep", 9999) { DisType = d });
                        if (now != OldFamily(d)) moved.Add($"{d}: {OldFamily(d)} -> {now}");
                    }
        Assert.True(moved.Count == 0, string.Join(", ", moved.Take(20)));
    }

    /// <summary>⭐ CE-2138 — every class a family or a 2-D avatar is keyed on exists in the table (a rename cannot silently
    /// turn a family into Unknown).</summary>
    [Fact]
    public void CE2138_EveryClassAFamilyOrAvatarNames_IsInTheTable()
    {
        foreach (var name in VisualFamilies.FamilyOfClass.Keys.Concat(Hrot.Presentation.Map.EntityClassShapeLibrary.ProfileOfClass.Keys))
            Assert.True(EntityClassTable.Default.Find(name) is not null, $"no class '{name}'");
    }

    /// <summary>⭐ CE-2127 — the 2-D avatar reads Category from bits 24–31: a helicopter is rotary-wing, a jet / trainer / UAV /
    /// airliner fixed-wing. 🔴 Before, Category came from Country's high byte (0 for US/RU), so every aircraft drew fixed-wing.</summary>
    [Theory]
    [InlineData(21, GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.RotaryWing)]
    [InlineData(25, GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.RotaryWing)]
    [InlineData(1, GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.FixedWing)]
    [InlineData(40, GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.FixedWing)]
    [InlineData(50, GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.FixedWing)]
    [InlineData(57, GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.FixedWing)]
    public void CE2127_TheAvatar_ReadsTheRealCategory(byte category, string expected)
    {
        var dis = new DISEntityType { Kind = 1, Domain = 2, Country = 225, Category = category }.Value;
        Assert.Equal(expected, new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary().GetShape(null, dis).Name);
        Assert.Equal(expected, new Hrot.Presentation.Map.EntityClassShapeLibrary().GetShape(null, dis).Name);
    }

    /// <summary>
    /// ⭐ <c>CE-3148</c> — 🔒 user: <i>"all entities in the duel scenario are rendered as pink boxes."</i> Every static obstacle states its DIS
    /// type: the parked car is a car (1.1.0.81 ⇒ the ground-vehicle avatar, the WheeledCar 3-D family), the sandbag wall, concrete block
    /// and crate are cultural features (5.1 ⇒ their footprint outline) — none is the magenta unknown-type box any more; and a 2-D avatar
    /// is sized from the obstacle's own footprint (<c>ObstacleShape</c>), not the renderer's 5 × 2.5 m default.
    /// </summary>
    [Fact]
    public void CE3148_EveryObstacle_HasADisType_TheCarDrawsAsACar_TheRestAsTheirFootprint_AtTheirSize()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterObstacles(db);
        var shapes = new Hrot.Presentation.Map.EntityClassShapeLibrary();
        var car = db.GetByType(TkbEntityTypes.Obstacle_Car)!;
        Assert.Equal(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.GroundVehicle, shapes.GetShape(null, car.DisType.Value).Name);
        Assert.Equal(VisualFamily.WheeledCar, VisualFamilies.Of(car));
        foreach (var type in new[] { TkbEntityTypes.Obstacle_SandbagWall, TkbEntityTypes.Obstacle_ConcreteBlock, TkbEntityTypes.Obstacle_Crate })
        {
            var t = db.GetByType(type)!;
            Assert.Equal(5, t.DisType.Kind);
            Assert.Equal(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.CulturalFeature, shapes.GetShape(null, t.DisType.Value).Name);
            Assert.Null(t.GetDescriptor<TkbMasterDto>());   // not a picker entry (a sided unit) — the type only
        }

        using var world = new EntityRepository();
        world.RegisterComponent<ObstacleShape>();
        var e = world.CreateEntity();
        world.AddComponent(e, new ObstacleShape { Length = 3f, Width = 0.6f, Height = 1f });
        Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmoShared.TryGetVehicleDimensions(world, e, out float length, out float width);
        Assert.Equal((3f, 0.6f), (length, width));
    }

    /// <summary>⭐ CE-2138 — the class-table avatar picks what the (fixed) GizmoMap switch picks over the whole DIS space, and a
    /// named shape still wins.</summary>
    [Fact]
    public void CE2138_TheClassAvatar_AgreesWithTheDisSwitch_AcrossTheDisSpace()
    {
        var byClass = new Hrot.Presentation.Map.EntityClassShapeLibrary();
        var bySwitch = new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary();
        var moved = new List<string>();
        foreach (byte kind in new byte[] { 0, 1, 2, 3, 4, 5, 9, 11 })   // ⭐ CE-3148 — 5, a cultural feature
            for (int domain = 0; domain <= 6; domain++)
                for (int category = 0; category <= 255; category++)
                {
                    var d = new DISEntityType { Kind = kind, Domain = (byte)domain, Category = (byte)category, Country = 225 };
                    string a = byClass.GetShape(null, d.Value).Name, b = bySwitch.GetShape(null, d.Value).Name;
                    if (a != b) moved.Add($"{d}: {b} -> {a}");
                }
        Assert.True(moved.Count == 0, string.Join(", ", moved.Take(20)));
        Assert.Equal(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.Humanoid,
                     byClass.GetShape(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.Humanoid, 0).Name);
    }

    /// <summary>⭐ CE-2139 (C3) — a unit is a unit by its DIS Kind: family Unit (no body), no 2-D avatar, in the ground-units map
    /// layer but no longer in "vehicles" (it was, as 1.1.225); a composite with a PLATFORM DIS type is no longer special.</summary>
    [Fact]
    public void CE2139_AUnit_IsAUnitByItsDisKind_NoBodyNoAvatar_NotAVehicle()
    {
        var squad = BuiltIn().GetByType(TkbEntityTypes.Unit_InfantrySquad)!;
        Assert.Equal(VisualFamily.Unit, VisualFamilies.Of(squad));
        Assert.Equal(VisualFamily.Unit, VisualFamilies.Of(UnitDis.Land(UnitArm.Artillery, UnitEchelon.Battalion, 78)));
        Assert.Equal(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.Fallback,
                     new Hrot.Presentation.Map.EntityClassShapeLibrary().GetShape(null, squad.DisType.Value).Name);

        bool In(string layer) => Hrot.Presentation.Map.MapLayerRegistry.All.Single(l => l.Name == layer).IsMember(default, squad.DisType, null!);
        Assert.True(In("units_ground"));     // the DIS-only layers; the others read components
        Assert.False(In("vehicles"));

        var platformComposite = new TkbTemplate("old", 9998) { DisType = new DISEntityType { Kind = 1, Domain = 1, Country = 225 } };
        platformComposite.AddDescriptor(new TkbCompositionDef());
        Assert.Equal(VisualFamily.Unknown, VisualFamilies.Of(platformComposite));   // the DIS type decides, not the composition
    }

    /// <summary>⭐ <c>CE-3213</c> (backend, cross-lane) — the units layers hold UNITS only (platforms, life forms, composites): an anti-air round (munition domain
    /// 1) is not a ground unit, an anti-armour round (domain 2) not an air unit, a flare (expendable, air) neither; a tank and a helicopter
    /// are. 🔴 Red-proof: before, the layers keyed on the domain alone.</summary>
    [Fact]
    public void CE3213_TheUnitsLayers_HoldUnits_NotRoundsOrDecoys()
    {
        bool In(string layer, DISEntityType dis) => Hrot.Presentation.Map.MapLayerRegistry.All.Single(l => l.Name == layer).IsMember(default, dis, null!);
        Assert.False(In("units_ground", new DISEntityType { Kind = 2, Domain = 1, Country = 225, Category = 1, Subcategory = 15 }));   // Stinger
        Assert.False(In("units_air", new DISEntityType { Kind = 2, Domain = 2, Country = 225, Category = 1, Subcategory = 8 }));      // Javelin
        Assert.False(In("units_air", new DISEntityType { Kind = 8, Domain = 2, Category = 2 }));                                     // a flare
        Assert.True(In("units_ground", new DISEntityType { Kind = 1, Domain = 1, Country = 225, Category = 1 }));                    // a tank
        Assert.True(In("units_ground", new DISEntityType { Kind = 3, Domain = 1, Country = 225, Category = 1 }));                    // a soldier
        Assert.True(In("units_air", new DISEntityType { Kind = 1, Domain = 2, Country = 225, Category = 20 }));                      // a helicopter
    }

    /// <summary>⭐ <c>CE-1058</c> — the 3-D map: a passenger inside an ENCLOSED vehicle has no body box (so nothing to pick, no
    /// selection cube — and the body loop skips it by the same rule); an exposed rider keeps its box, carried at the vehicle.</summary>
    [Fact]
    public void CE1058_AnEnclosedPassenger_HasNoBody_AnExposedRiderHasOne()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        var db = BuiltIn();
        Entity Soldier(byte exposed, bool aboard)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(5f, 5f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
            if (aboard) world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { Exposed = exposed });
            return e;
        }
        var layer = new EntityBodyLayer3D(() => world, () => db);
        Assert.True(layer.TryGetBox(world, Soldier(0, aboard: false), db, out _, out _, out _));
        Assert.False(layer.TryGetBox(world, Soldier(0, aboard: true), db, out _, out _, out _));    // inside an APC
        Assert.True(layer.TryGetBox(world, Soldier(1, aboard: true), db, out _, out _, out _));     // on a motorcycle
    }

    // ── ⭐ CE-1081 … CE-1084 (docs/DESIGN_Map_3D_Mode.md §6m) — the carried, the dragged, the wounded, the seated, the jump down ──

    private static (EntityRepository World, ITkbDatabase Db) HeldWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
        return (world, BuiltIn());
    }

    private static Entity Man(EntityRepository world, Vector3 at, float health = 100f, long type = TkbEntityTypes.Infantry_Rifleman)
    {
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = type });
        world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
        world.AddComponent(e, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f, WoundedSpeedFactor = 0.6f });
        return e;
    }

    private static Entity Carries(EntityRepository world, Entity carrier, Entity casualty, Fdp.Toolkit.Behavior.Components.CarryMode mode)
    {
        world.AddComponent(carrier, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f, DragBehindMetres = 1f });
        var list = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1 };
        list.Passengers[0] = casualty;   // ⭐ CE-1085 — as the sim lists him (the classifier reads WHO is in the seat, not the count)
        list.SetMode(0, mode);
        world.AddComponent(carrier, list);
        world.AddComponent(casualty, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = carrier, Exposed = 1, Mode = mode });
        return casualty;
    }

    /// <summary>
    /// ⭐⭐ CE-1164 (civilian U-3, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U3-a/c) — a man KNOCKED DOWN by a non-lethal hit (the stored
    /// <c>EffectState</c>, CE-3219) lies like a man down at FULL health, and stands again when the effect runs out; the 2-D line says what
    /// runs (DOWN · STUNNED · GASSED), and nothing once it is over.
    /// </summary>
    [Fact]
    public void CE1164_AKnockedDownMan_LiesDown_UntilItRunsOut_AndThe2DLineSaysWhatRuns()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
            var man = Man(world, new Vector3(0f, 0f, 0f));
            Assert.Equal(CarriageKind.Free, Carriage.Of(world, man, db).Kind);
            Assert.Null(Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));

            world.AddComponent(man, new Fdp.Toolkit.Combat.EffectState { KnockedDownUntil = 14.0, IrritatedUntil = 30.0 });
            Assert.Equal(CarriageKind.Downed, Carriage.Of(world, man, db).Kind);   // at full health
            Assert.Equal("DOWN · GASSED", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));

            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 20.0 });       // the knock-down is over, the gas is not
            Assert.Equal(CarriageKind.Free, Carriage.Of(world, man, db).Kind);
            Assert.Equal("GASSED", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));

            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 40.0 });
            Assert.Null(Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1081 … CE-1083 — ONE classifier says how a man is held, from what every map host already has: the embarked tag (carried /
    /// dragged by a person carrier, seated in an exposed seat with a place), the carrier's own list (he carries / drags), and the injury
    /// state off the replicated health (wounded — combinable; incapacitated — down). An ENCLOSED seat, or an open one with no place, is
    /// no carriage of its own (the old rule draws or hides him).
    /// </summary>
    [Fact]
    public void CE1081_TheCarriage_SaysHowAManIsHeld()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            var bearer = Man(world, new Vector3(0f, 0f, 0f));
            var carried = Carries(world, bearer, Man(world, Vector3.Zero, health: 20f), Fdp.Toolkit.Behavior.Components.CarryMode.Carried);
            Assert.Equal((CarriageKind.Carried, bearer), (Carriage.Of(world, carried, db).Kind, Carriage.Of(world, carried, db).Holder));
            Assert.Equal(CarriageKind.Carrying, Carriage.Of(world, bearer, db).Kind);

            var puller = Man(world, new Vector3(10f, 0f, 0f), health: 50f);   // a WOUNDED man can still drag
            var dragged = Carries(world, puller, Man(world, Vector3.Zero, health: 20f), Fdp.Toolkit.Behavior.Components.CarryMode.Dragged);
            Assert.Equal(CarriageKind.Dragged, Carriage.Of(world, dragged, db).Kind);
            Assert.Equal(new FigureCarriage(CarriageKind.Dragging, Wounded: true), Carriage.Of(world, puller, db));

            var heli = Man(world, new Vector3(50f, 0f, 1.75f), type: TkbEntityTypes.Heli_UH60_DoorsOff);
            var rider = Man(world, Vector3.Zero);
            world.AddComponent(rider, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = heli, Exposed = 1, Seat = 5 });
            var seated = Carriage.Of(world, rider, db);
            Assert.Equal(CarriageKind.Seated, seated.Kind);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut, seated.Seat!.Pose);
            Assert.True(seated.Seat.Y < 0f, "seat 5 is in the RIGHT door");

            var inside = Man(world, Vector3.Zero);
            world.AddComponent(inside, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = heli, Exposed = 0, Seat = 1 });
            Assert.Equal(CarriageKind.Free, Carriage.Of(world, inside, db).Kind);

            Assert.Equal(new FigureCarriage(CarriageKind.Free, Wounded: true), Carriage.Of(world, Man(world, Vector3.Zero, health: 50f), db));
            Assert.Equal(CarriageKind.Downed, Carriage.Of(world, Man(world, Vector3.Zero, health: 20f), db).Kind);
            Assert.Equal(FigureCarriage.Free, Carriage.Of(world, Man(world, Vector3.Zero), db));
        }
    }

    // ── ⭐ CE-1085 … CE-1094 (docs/DESIGN_Map_3D_Mode.md §6n) — weapon systems, the drone, the attack plane ──

    private static Entity WeaponSystem(EntityRepository world, Vector3 at)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>()) world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        var w = world.CreateEntity();
        world.AddComponent(w, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        world.AddComponent(w, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1, Portable = 1 });
        return w;
    }

    /// <summary>
    /// ⭐⭐ CE-1085 (§6n N-3) — a soldier's casualty seat and his carry station are read APART: a weapon system in his list is the LOAD he
    /// carries, never a casualty. 📌 Red before: CE-1081 read seat 0's mode whenever the list was not empty, so a man with only a Javelin in
    /// his carry station (seat 1) was drawn CARRYING someone. ⭐ CE-1088 (N-2) — a man in a weapon's own seat MANS it.
    /// </summary>
    [Fact]
    public void CE1085_AWeaponHeCarries_IsHisLoad_NotACasualty_AndAManAtAWeapon_MansIt()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            var javelinMan = Man(world, Vector3.Zero);
            var javelin = WeaponSystem(world, Vector3.Zero);
            world.AddComponent(javelinMan, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f });
            var list = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 2 };
            list.Passengers[1] = javelin;   // the carry station; seat 0 (the casualty seat) empty, its mode bits 0 = Seated
            world.AddComponent(javelinMan, list);
            Assert.Equal(new FigureCarriage(CarriageKind.Free, Load: javelin), Carriage.Of(world, javelinMan, db));

            var both = Man(world, new Vector3(5f, 0f, 0f));
            var hmg = WeaponSystem(world, Vector3.Zero);
            var casualty = Carries(world, both, Man(world, Vector3.Zero, health: 20f), Fdp.Toolkit.Behavior.Components.CarryMode.Carried);
            ref var bothList = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(both);
            bothList.Passengers[0] = casualty;
            bothList.Passengers[1] = hmg;
            Assert.Equal(new FigureCarriage(CarriageKind.Carrying, Load: hmg), Carriage.Of(world, both, db));

            var mortar = WeaponSystem(world, new Vector3(20f, 0f, 0f));
            var gunner = Man(world, new Vector3(20f, 0f, 0f));
            world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = mortar, Exposed = 1, Seat = 0 });
            var manning = Carriage.Of(world, gunner, db);
            Assert.Equal((CarriageKind.Manning, mortar), (manning.Kind, manning.Holder));
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1086 (§6n N-7) — the FPV drone (403) is a QUADCOPTER as big as its flight body (0.6 × 0.6 × 0.3 m), not a 15 m fighter: a
    /// <c>Uav</c> that flies as a <c>Rotorcraft</c> is the Drone family; a fixed-wing type of the same Jet family stays a jet. Its four rotors
    /// spin about their own motors while it lives.
    /// </summary>
    [Fact]
    public void CE1086_TheFpvDrone_IsAQuadcopter_AsBigAsItsFlightBody()
    {
        var db = BuiltIn();
        var drone = EntityBodyLayer3D.Resolve(db.GetByType(TkbEntityTypes.Drone_Fpv));
        Assert.Equal(VisualFamily.Drone, drone.Family);
        Assert.Equal(new Vector3(0.6f, 0.6f, 0.3f), drone.Size);
        Assert.Equal(VisualFamily.Jet, EntityBodyLayer3D.Resolve(db.GetByType(TkbEntityTypes.Jet_F16)).Family);

        var rotors = ShapeKits.Drone.Parts.Where(p => p.Role == PartRole.Rotor).ToArray();
        Assert.Equal(4, rotors.Length);
        var tracker = new MotionTracker();
        var e = new Entity(7, 1);
        tracker.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.Drone, drone.Size, 10.0, rotorTurning: true);
        var pose = tracker.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.Drone, drone.Size, 10.03, rotorTurning: true);
        Assert.NotEqual(0f, pose.Rotor);
        foreach (var r in rotors)   // each spins about its own motor: its centre stays where it is
        {
            var still = Vector3.Transform(Vector3.Zero, r.BodyTransform(drone.Size));
            var spun = Vector3.Transform(Vector3.Zero, r.BodyTransform(drone.Size, default, ArticulationPose.Neutral, pose));
            Assert.InRange(Vector3.Distance(still, spun), 0f, 1e-4f);
        }
    }

    private static Fdp.Core.DISEntityType Dis(byte category, byte subcategory = 0)
        => new() { Kind = 1, Domain = 1, Country = 225, Category = category, Subcategory = subcategory };

    /// <summary>
    /// ⭐⭐ CE-1087 (§6n N-1) — every weapon-system class draws its OWN kit (no purple box): the kind comes from the class chain (HMG,
    /// minigun, ATGM, launcher, sentry, and the mortar and towed gun of Indirect Fire IF-G); each kind's stowed and deployed kits have the
    /// same parts in the same order, so a deploy blends; only a fully deployed kit articulates; the deploy state's timing reads as 0 → 1.
    /// </summary>
    [Fact]
    public void CE1087_EveryWeaponSystemClass_HasItsKit_StowedAndDeployed_Blending()
    {
        Assert.Equal(VisualFamily.WeaponSystem, VisualFamilies.Of(Dis(50, 1)));
        Assert.Equal(VisualFamily.WeaponSystem, VisualFamilies.Of(Dis(10)));      // Mortar
        Assert.Equal(VisualFamily.WeaponSystem, VisualFamilies.Of(Dis(5)));       // TowedArtillery
        Assert.Equal(VisualFamily.SpArtillery, VisualFamilies.Of(Dis(4)));        // SelfPropelledArtillery
        Assert.Equal(WeaponKind.Hmg, WeaponKits.KindOf(Dis(50, 1)));
        Assert.Equal(WeaponKind.Minigun, WeaponKits.KindOf(Dis(50, 2)));
        Assert.Equal(WeaponKind.Atgm, WeaponKits.KindOf(Dis(50, 3)));
        Assert.Equal(WeaponKind.Launcher, WeaponKits.KindOf(Dis(50, 4)));
        Assert.Equal(WeaponKind.Sentry, WeaponKits.KindOf(Dis(50, 5)));
        Assert.Equal(WeaponKind.Manpads, WeaponKits.KindOf(Dis(50, 6)));        // ⭐ CE-1112 — Stinger / Igla
        Assert.Equal(WeaponKind.AaGun, WeaponKits.KindOf(Dis(50, 7)));          // ⭐ CE-1112 — ZU-23-2
        Assert.Equal(WeaponKind.Hmg, WeaponKits.KindOf(Dis(50)));                // a plain weapon system
        Assert.Equal(WeaponKind.Mortar, WeaponKits.KindOf(Dis(10)));
        Assert.Equal(WeaponKind.TowedGun, WeaponKits.KindOf(Dis(5)));

        foreach (var kind in Enum.GetValues<WeaponKind>())
        {
            var stowed = WeaponKits.Kit(kind, deployed: false);
            var deployed = WeaponKits.Kit(kind, deployed: true);
            Assert.Equal(stowed.Parts.Length, deployed.Parts.Length);
            Assert.DoesNotContain(stowed.Parts, p => p.Role is PartRole.Turret or PartRole.Gun);
            var half = WeaponKits.Blend(kind, 0.5f);
            Assert.Equal(deployed.Parts.Length, half.Parts.Length);
            Assert.False(half.IsArticulated);
        }
        // the tripod HMG stands its gun up on its legs when deployed (stowed it lies on the ground)
        var size = WeaponKits.DefaultSize(WeaponKind.Hmg);
        float Top(ShapeKit k) => k.Parts.Max(p => Vector3.Transform(Vector3.Zero, p.BodyTransform(size)).Z);
        Assert.True(Top(WeaponKits.Kit(WeaponKind.Hmg, true)) > Top(WeaponKits.Kit(WeaponKind.Hmg, false)) + 0.3f);
        Assert.True(WeaponKits.Kit(WeaponKind.Hmg, true).IsArticulated);

        Assert.Equal(1f, WeaponKits.Deployment(Fdp.Toolkit.Combat.WeaponDeploy.Deployed, 0, 5, 3f));
        Assert.Equal(0f, WeaponKits.Deployment(Fdp.Toolkit.Combat.WeaponDeploy.Stowed, 0, 5, 3f));
        Assert.Equal(1f / 3f, WeaponKits.Deployment(Fdp.Toolkit.Combat.WeaponDeploy.Deploying, until: 7, now: 5, seconds: 3f), 3);
        Assert.Equal(2f / 3f, WeaponKits.Deployment(Fdp.Toolkit.Combat.WeaponDeploy.Packing, until: 7, now: 5, seconds: 3f), 3);
    }

    /// <summary>
    /// ⭐⭐ CE-1088 (§6n N-2) — a man in a weapon's own seat is drawn AT it: behind a tripod HMG, kneeling, his hands on its spade grips;
    /// he turns with the weapon's traverse (about its pivot), and a seat that declares a place wins. The pose keeps the shared part order.
    /// </summary>
    [Fact]
    public void CE1088_AGunner_KneelsBehindHisHmg_HandsOnTheGrips_AndTurnsWithIt()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            var hmgType = new TkbTemplate("Test HMG", 990001) { DisType = Dis(50, 1) };
            ((TkbDatabase)db).Register(hmgType);
            var hmg = WeaponSystem(world, new Vector3(100f, 50f, 0f));
            world.AddComponent(hmg, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 990001 });
            var gunner = Man(world, new Vector3(100f, 50f, 0f));
            world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = hmg, Exposed = 1, Seat = 0 });
            var carriage = Carriage.Of(world, gunner, db);

            float azimuth = 0f;
            var layer = new EntityBodyLayer3D(() => world, () => db, (_, _) => new ArticulationPose(azimuth, 0f));
            var man = ShapeKits.DefaultSize(VisualFamily.Person);
            Assert.True(layer.GunnerFrame(world, carriage, man, 0d, out var frame, out var pose));
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel, pose);
            bool kneel = true;
            var at = Vector3.Transform(Vector3.Zero, frame);
            Assert.InRange(at.X, 98.9f, 99.2f);   // ~0.95 m behind the pintle
            Assert.InRange(at.Y, 49.9f, 50.1f);
            var parts = BlockFigure.AtWeapon(kneel: true, soldier: true);
            Assert.Equal(BlockFigure.Parts(StanceId.Standing, true).Length, parts.Length);
            var hand = Vector3.Transform(new Vector3(0f, 0f, 0.5f), parts[BlockFigure.TorsoIndex + 3].BodyTransform(man) * frame);   // left forearm end
            var grips = Vector3.Transform(Vector3.Zero, WeaponKits.Kit(WeaponKind.Hmg, true).Parts[7].BodyTransform(WeaponKits.DefaultSize(WeaponKind.Hmg)))
                        + new Vector3(100f, 50f, 0f);
            Assert.InRange(Vector3.Distance(hand, grips), 0f, 0.35f);

            azimuth = MathF.PI / 2f;   // the gun traverses to its left: he goes round with it, behind it again
            Assert.True(layer.GunnerFrame(world, carriage, man, 0d, out var turned, out _));
            var turnedAt = Vector3.Transform(Vector3.Zero, turned);
            Assert.InRange(turnedAt.Y, 48.9f, 49.2f);
            Assert.InRange(turnedAt.X, 99.9f, 100.1f);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1112 (§6v AD-V2 … AD-V4) — backend's air defence drawn: the catalogue's launchers 613 / 614 are MANPADS and the 615 a
    /// ZU-23 (by their class rows, not as HMGs); the ZU-23's gunner SITS on its left seat (hips at the seat) and turns with it; a MANPADS is
    /// shoulder-fired and throws a backblast like the other tubes; the ZU-23 is not.
    /// </summary>
    [Fact]
    public void CE1112_TheMANPADS_AndTheZu23_DrawAsThemselves_AndTheZu23sGunnerSits()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            var builtIn = BuiltIn();
            Assert.Equal(WeaponKind.Manpads, WeaponKits.KindOf(builtIn.GetByType(TkbEntityTypes.WeaponSystem_Stinger)!.DisType));
            Assert.Equal(WeaponKind.Manpads, WeaponKits.KindOf(builtIn.GetByType(TkbEntityTypes.WeaponSystem_Igla)!.DisType));
            Assert.Equal(WeaponKind.AaGun, WeaponKits.KindOf(builtIn.GetByType(TkbEntityTypes.WeaponSystem_Zu23)!.DisType));
            Assert.True(WeaponKits.IsShoulderFired(WeaponKind.Manpads));
            Assert.False(WeaponKits.IsShoulderFired(WeaponKind.AaGun));
            // AD-V5 — mounted in a vehicle's station it loses its ground carriage (3 jacks, 2 wheels); other kinds keep every part
            Assert.Equal(17 - 5, WeaponKits.Mounted(WeaponKind.AaGun, WeaponKits.Kit(WeaponKind.AaGun, true).Parts).Length);
            Assert.Equal(WeaponKits.Kit(WeaponKind.Hmg, true).Parts.Length, WeaponKits.Mounted(WeaponKind.Hmg, WeaponKits.Kit(WeaponKind.Hmg, true).Parts).Length);

            ((TkbDatabase)db).Register(new TkbTemplate("Test ZU-23", 990011) { DisType = Dis(50, 7) });
            var gun = WeaponSystem(world, new Vector3(100f, 50f, 0f));
            world.AddComponent(gun, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 990011 });
            var gunner = Man(world, new Vector3(100f, 50f, 0f));
            world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = gun, Exposed = 1, Seat = 0 });
            var carriage = Carriage.Of(world, gunner, db);

            float azimuth = 0f;
            var layer = new EntityBodyLayer3D(() => world, () => db, (_, _) => new ArticulationPose(azimuth, 0f));
            var man = ShapeKits.DefaultSize(VisualFamily.Person);
            Assert.True(layer.GunnerFrame(world, carriage, man, 0d, out var frame, out var pose));
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.SeatPose.Sit, pose);
            var hips = Vector3.Transform(Vector3.Zero, frame);
            var size = WeaponKits.DefaultSize(WeaponKind.AaGun);
            var seat = Vector3.Transform(Vector3.Zero, WeaponKits.Kit(WeaponKind.AaGun, true).Parts[14].BodyTransform(size)) + new Vector3(100f, 50f, 0f);
            Assert.InRange(hips.X - seat.X, -0.05f, 0.05f);                // over the left seat
            Assert.InRange(hips.Y - seat.Y, -0.05f, 0.05f);
            Assert.InRange(hips.Z - seat.Z, 0f, 0.15f);                    // sitting on it, not in it

            azimuth = MathF.PI / 2f;                                        // the gun traverses left: he goes round with it
            Assert.True(layer.GunnerFrame(world, carriage, man, 0d, out var turned, out _));
            var turnedAt = Vector3.Transform(Vector3.Zero, turned);
            Assert.InRange(turnedAt.X, 100f - 0.50f, 100f - 0.40f);         // his left (+y) offset is now behind (−x)
            Assert.InRange(turnedAt.Y, 50f - 0.60f, 50f - 0.50f);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1090 (§6n N-4) — the launcher in his hands: the rifle part becomes a 1.1 m tube on the shoulder along his aim (a third of it
    /// behind the shoulder), whose FRONT is where the flash leaves; who throws what: a soldier's launcher mount (the Javelin team's) a
    /// backblast, a rifle nothing, a self-propelled gun a ground blast; the backblast billows behind him on the line of fire.
    /// </summary>
    [Fact]
    public void CE1090_TheLauncherOnTheShoulder_AndWhoThrowsABlast()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var aimed = BlockFigure.Parts(StanceId.Standing, true, WeaponPostureKind.Aimed);
        var shouldered = BlockFigure.Shouldered(aimed);
        var tube = shouldered[BlockFigure.WeaponIndex];
        Assert.Equal(PartShape.Cylinder, tube.Shape);
        var (tip, forward) = EntityBodyLayer3D.WeaponTip(tube, size, default, ArticulationPose.Neutral, MotionPose.Still, Matrix4x4.Identity);
        var (rifleTip, _) = EntityBodyLayer3D.WeaponTip(aimed[BlockFigure.WeaponIndex], size, default, ArticulationPose.Neutral, MotionPose.Still, Matrix4x4.Identity);
        var tail = tip - forward * BlockFigure.TubeLength;
        Assert.True(forward.X > 0.9f, $"the tube points along his aim ({forward})");
        Assert.True(tail.X < -0.05f, $"its rear sticks out behind the shoulder (tail x {tail.X:0.00})");
        Assert.InRange(tip.Z, 1.25f, 1.55f);
        Assert.True(MathF.Abs(tip.X - rifleTip.X) < 0.5f);

        var (world, db) = HeldWorld();
        using (world)
        {
            world.SetSingletonManaged<ITkbDatabase>(db);
            var javelin = Man(world, Vector3.Zero, type: UrbanCombatTkbCatalog.TkbJavelinTeam);
            int launcher = -1;
            var suite = db.GetByType(UrbanCombatTkbCatalog.TkbJavelinTeam)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>()!;
            for (int i = 0; i < suite.Mounts.Count; i++) if (suite.Mounts[i].Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm) launcher = i;
            Assert.True(launcher >= 0);
            Assert.Equal(FiringBlast.Backblast, WeaponKits.BlastOf(world, db, javelin, suite.Mounts[launcher]));
            Assert.Equal(FiringBlast.None, WeaponKits.BlastOf(world, db, javelin, suite.Mounts[launcher == 0 ? 1 : 0]));
            var sp = new TkbTemplate("Test SP howitzer", 990002) { DisType = Dis(4) };
            ((TkbDatabase)db).Register(sp);
            var gun = Man(world, Vector3.Zero, type: 990002);
            Assert.Equal(FiringBlast.Ground, WeaponKits.BlastOf(world, db, gun, null));

            var shots = Hrot.UI.Common.Effects.RecentShots.In(world);
            shots.Record(javelin, launcher, at: 10.0);
            Assert.True(shots.FiredWithin(javelin, 12.0, WeaponKits.ShoulderSeconds, out int m) && m == launcher);
            Assert.False(shots.FiredWithin(javelin, 15.0, WeaponKits.ShoulderSeconds, out _));
        }
        var back = WeaponKits.BackblastPoint(new Vector3(10f, 0f, 0f), Quaternion.Identity, new Vector3(110f, 0f, 0f));
        Assert.True(back.X < 9f && MathF.Abs(back.Y) < 0.01f, $"behind him on the line of fire ({back})");
    }

    /// <summary>
    /// ⭐ CE-1107 (§6n N-4, backend P4) — the launcher is on his shoulder from the moment he SELECTS it, before any shot: the replicated
    /// <c>WeaponPosture.ActiveMount</c> names the Javelin team's launcher (mount 0) ⇒ shouldered; his rifle (mount 1) ⇒ not; a rifleman's
    /// rifle ⇒ not; a rifleman carrying a Javelin weapon system with it selected (a controlled index, 32 + k) ⇒ shouldered.
    /// </summary>
    [Fact]
    public void CE1107_TheSelectedLauncherIsShouldered_BeforeTheFirstShot()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.SetSingletonManaged<ITkbDatabase>(db);
            world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
            world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
            bool Shoulders(Entity man, int mount)
            {
                world.SetComponent(man, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = WeaponPostureKind.Aimed, ActiveMount = (byte)mount });
                return EntityBodyLayer3D.Shouldering(world, man, Carriage.Of(world, man, db), db, now: 5.0);
            }
            var javelin = Man(world, Vector3.Zero, type: UrbanCombatTkbCatalog.TkbJavelinTeam);
            world.AddComponent(javelin, new Fdp.Toolkit.Combat.Components.WeaponPosture());
            Assert.True(Shoulders(javelin, 0), "his launcher selected");
            Assert.False(Shoulders(javelin, 1), "his rifle selected");
            var rifleman = Man(world, new Vector3(5f, 0f, 0f));
            world.AddComponent(rifleman, new Fdp.Toolkit.Combat.Components.WeaponPosture());
            Assert.False(Shoulders(rifleman, 0));

            // a rifleman carrying a Javelin weapon system (fires carried): it is his first controlled weapon
            var bearer = Man(world, new Vector3(10f, 0f, 0f));
            world.AddComponent(bearer, new Fdp.Toolkit.Combat.Components.WeaponPosture());
            var tube = world.CreateEntity();
            world.AddComponent(tube, new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(tube, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TkbEntityTypes.WeaponSystem_Javelin });
            world.AddComponent(tube, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1, Portable = 1, FireWhileCarried = 1 });
            world.AddComponent(tube, new Fdp.Toolkit.Combat.WeaponSystemState { Deploy = Fdp.Toolkit.Combat.WeaponDeploy.Stowed });
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierStations>();
            new Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator().Inject(world, bearer, ((TkbDatabase)db).GetByType(TkbEntityTypes.Infantry_Rifleman)!);   // his seats and stations, as the sim gives them
            var stations = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.CarrierStations>(bearer);
            int carry = Enumerable.Range(0, Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity).First(i => stations.IsStation(i));
            var list = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(bearer);
            list.Passengers[carry] = tube;
            list.Count = 1;
            world.SetComponent(bearer, list);
            world.AddComponent(tube, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = bearer, Exposed = 1, Seat = (byte)carry });
            Assert.Equal(tube, Fdp.Toolkit.Combat.WeaponSystems.ControlledAt(world, bearer, Fdp.Toolkit.Combat.WeaponSystems.ControlledMountBase));
            Assert.True(Shoulders(bearer, Fdp.Toolkit.Combat.WeaponSystems.ControlledMountBase), "the carried launcher selected");
            Assert.False(Shoulders(bearer, 0), "his own rifle selected");
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1091 (§6n N-6) — the smoke trail follows the REMEMBERED path (it bends as the round did — a top-attack climb and dive), not a
    /// straight line back to the muzzle; a shell or a bomb draws only a faint streak of its last two seconds; a bullet nothing.
    /// </summary>
    [Fact]
    public void CE1091_TheTrailFollowsThePath_AShellStreaksItsLastTwoSeconds()
    {
        var missile = new Fdp.Toolkit.Tkb.Domain.MunitionVisualDto { Shape = Fdp.Toolkit.Tkb.Domain.MunitionShape.Missile, SmokeTrail = true, Length = 1.1f, Diameter = 0.13f };
        var path = new Hrot.UI.Common.Effects.ProjectileLayer.RoundPath();
        // a top attack: out level 0 → 30 m, climbing to 40 m up at x 60, then diving onto x 100
        for (int i = 0; i <= 100; i++)
        {
            float x = i;
            float z = x < 30f ? 1f : x < 60f ? 1f + (x - 30f) * 39f / 30f : 40f - (x - 60f) * 39f / 40f;
            path.Add(new Vector3(x, 0f, z), i * 0.05);   // 5 s of flight
        }
        var round = new Hrot.UI.Common.Effects.ProjectileLayer.Round(missile, new Vector3(100f, 0f, 1f), Vector3.Normalize(new Vector3(1f, 0f, -1f)), false, 0f,
                                                                    new Vector3(0f, 0f, 1f), 7, 140f);
        var curved = Hrot.UI.Common.Effects.ProjectileLayer.TrailOf(round, path);
        var straight = Hrot.UI.Common.Effects.ProjectileLayer.TrailOf(round);
        Assert.NotEmpty(curved);
        Assert.True(curved.Max(p => p.At.Z) > 25f, "the trail climbs over the top of the arc");
        Assert.True(straight.Max(p => p.At.Z) < 10f, "(the old straight trail never left the line)");

        var shell = missile with { Shape = Fdp.Toolkit.Tkb.Domain.MunitionShape.Shell, SmokeTrail = false };
        var streak = Hrot.UI.Common.Effects.ProjectileLayer.StreakOf(round with { Look = shell }, path, now: 5.0);
        Assert.NotEmpty(streak);
        Assert.All(streak, l => Assert.True(l.To.X >= 60f - 0.01f, "only the last two seconds (x 60 … 100)"));
        Assert.Empty(Hrot.UI.Common.Effects.ProjectileLayer.StreakOf(round, path, now: 5.0));   // a missile: smoke, no streak
    }

    /// <summary>
    /// ⭐⭐ CE-1093 (§6n N-9) — a helicopter's rotor RUNS while it takes off from the ground (below the "airborne" height, moving), spools up
    /// over <see cref="MotionTracker.RotorSpoolSeconds"/> rather than snapping, and its tail-rotor blades spin about their own axle.
    /// 📌 Red before: the rotor stood still until the body was 5 m above its resting height.
    /// </summary>
    [Fact]
    public void CE1093_TheRotorRunsWhileItTakesOff_SpoolingUp_AndTheTailRotorSpins()
    {
        var t = new MotionTracker();
        var heli = new Entity(9, 1);
        var size = ShapeKits.DefaultSize(VisualFamily.Helicopter);
        t.Advance(heli, Vector3.Zero, Quaternion.Identity, VisualFamily.Helicopter, size, 0.0, rotorTurning: false);
        float prev = 0f, firstStep = 0f, lateStep = 0f;
        for (int i = 1; i <= 60; i++)   // 3 s rising 0.5 m/s: never "airborne"
        {
            var pose = t.Advance(heli, new Vector3(0f, 0f, i * 0.025f), Quaternion.Identity, VisualFamily.Helicopter, size, i * 0.05, rotorTurning: false);
            float step = MathF.Abs(pose.Rotor - prev);
            if (i == 2) firstStep = step;
            if (i == 59) lateStep = step;
            prev = pose.Rotor;
        }
        Assert.True(prev != 0f, "the rotor turns while it lifts off");
        Assert.True(lateStep > firstStep * 3f, $"and it spools up ({firstStep:0.000} → {lateStep:0.000} rad a frame)");

        var tail = ShapeKits.Helicopter.Parts.Where(k => k.Role == PartRole.TailRotor).ToArray();
        Assert.Equal(2, tail.Length);
        var spun = new MotionPose(0.9f, 0, 0);
        foreach (var blade in tail)
        {
            var still = Vector3.Transform(Vector3.Zero, blade.BodyTransform(size));
            Assert.InRange(Vector3.Distance(still, Vector3.Transform(Vector3.Zero, blade.BodyTransform(size, default, ArticulationPose.Neutral, spun))), 0f, 1e-3f);
            Assert.NotEqual(Vector3.Transform(new Vector3(0.5f, 0f, 0f), blade.BodyTransform(size)),
                            Vector3.Transform(new Vector3(0.5f, 0f, 0f), blade.BodyTransform(size, default, ArticulationPose.Neutral, spun)));
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1092 (§6n N-8) — the A-10 is an ATTACK PLANE, not an F-16: its own kit (straight wing, twin fins), and a bomb on a pylon for
    /// every round its bomb rack still holds — the replicated ammo mirror counts them down.
    /// </summary>
    [Fact]
    public void CE1092_TheAttackPlane_HasItsOwnKit_AndCarriesTheBombsItHasLeft()
    {
        var db = BuiltIn();
        Assert.Equal(VisualFamily.AttackJet, EntityBodyLayer3D.Resolve(db.GetByType(TkbEntityTypes.Jet_AttackA10)).Family);
        Assert.Same(ShapeKits.AttackJet, ShapeKits.For(VisualFamily.AttackJet));
        Assert.Equal(2, ShapeKits.AttackJet.Parts.Count(p => p.Shape == PartShape.Box && p.Extent.Z > 0.25f && p.Extent.Y < 0.02f));   // twin fins

        using var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        world.RegisterComponent<WeaponAmmo>();
        world.SetSingletonManaged<ITkbDatabase>(db);
        var plane = world.CreateEntity();
        world.AddComponent(plane, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TkbEntityTypes.Jet_AttackA10 });
        Assert.Equal(4, EntityBodyLayer3D.BombsLeft(world, plane, db));   // no mirror yet: the full load
        world.AddComponent(plane, new WeaponAmmo { Count = 1, Ammo0 = 1, Max0 = 4 });
        Assert.Equal(1, EntityBodyLayer3D.BombsLeft(world, plane, db));
    }

    /// <summary>⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o F-1, F-4) — every pose's head is skin, tagged with the side its face is on:
    /// upright, crouched, prone, crawling, seated and at a weapon its front; on his back (dead, downed, dragged) up; draped over a shoulder
    /// down. No other part is a head. A fall turns the face over halfway.</summary>
    [Fact]
    public void CE1097_EveryHead_IsSkin_WithItsFaceSide_AndAFallTurnsItOverHalfway()
    {
        foreach (bool soldier in new[] { true, false })
        {
            var poses = new List<(string Name, KitPart[] Parts, FaceOn Face)>();
            foreach (var stance in new[] { StanceId.Standing, StanceId.Crouched, StanceId.Prone })
                foreach (var posture in new[] { WeaponPostureKind.AtEase, WeaponPostureKind.Ready, WeaponPostureKind.Aimed })
                    poses.Add(($"{stance}/{posture}", BlockFigure.Parts(stance, soldier, posture), FaceOn.Front));
            poses.Add(("crawl", BlockFigure.CrawlPose(soldier, 1f), FaceOn.Front));
            poses.Add(("seated", BlockFigure.Seated(false, soldier), FaceOn.Front));
            poses.Add(("at a weapon", BlockFigure.AtWeapon(true, soldier), FaceOn.Front));
            poses.Add(("dead", BlockFigure.Dead(soldier), FaceOn.Up));
            poses.Add(("downed", BlockFigure.Downed(soldier), FaceOn.Up));
            poses.Add(("dragged", BlockFigure.Towed(soldier), FaceOn.Up));
            poses.Add(("draped", BlockFigure.Draped(soldier), FaceOn.Down));
            foreach (var (name, parts, face) in poses)
            {
                Assert.Equal(face, parts[BlockFigure.HeadIndex].Face);
                Assert.Equal(ColourRole.Skin, parts[BlockFigure.HeadIndex].Colour);
                Assert.Single(parts, p => p.Face != FaceOn.None);   // only the head ($name)
            }
        }
        var standing = BlockFigure.Parts(StanceId.Standing, true);
        Assert.Equal(FaceOn.Front, BlockFigure.Fall(standing, 0.4f, true)[BlockFigure.HeadIndex].Face);
        Assert.Equal(FaceOn.Up, BlockFigure.Fall(standing, 0.6f, true)[BlockFigure.HeadIndex].Face);
    }

    /// <summary>⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q O-1, O-2, O-5) — civilians of a type with no colour are NOT the family green and
    /// VARY: across sixty men every kind of clothes appears — long and short sleeves, trousers, shorts and skirts, a suit, long hair and
    /// none; each man's outfit is repeatable. A type WITH a colour dresses its men in that colour, lighter or darker.</summary>
    [Fact]
    public void CE1099_Civilians_VaryInWhatTheyWear_AndATypesColourIsKept()
    {
        var green = EntityBodyLayer3D.DefaultColour(VisualFamily.Person);
        var men = Enumerable.Range(1, 60).Select(s => Outfits.For(s, 1001, soldier: false, child: false, own: null, Fdp.Core.ForceId.Neutral)).ToArray();
        Assert.DoesNotContain(men, o => o.Top.Equals(green));
        Assert.True(men.Select(o => (o.Top.R, o.Top.G, o.Top.B)).Distinct().Count() >= 8, "their tops vary");
        Assert.Contains(men, o => !o.Suit && o.Sleeve.Equals(o.Skin));                    // short sleeves
        Assert.Contains(men, o => !o.Suit && o.Sleeve.Equals(o.Top));                     // long sleeves
        Assert.Contains(men, o => o.Legwear == Legwear.Trousers);
        Assert.Contains(men, o => o.Legwear == Legwear.Shorts);
        Assert.Contains(men, o => o.Legwear == Legwear.Skirt);
        Assert.Contains(men, o => o.Suit);
        Assert.Contains(men, o => o.HairStyle == HairStyle.Long);
        Assert.Contains(men, o => o.HairStyle == HairStyle.None);
        Assert.True(men.Select(o => (o.Skin.R, o.Skin.G, o.Skin.B)).Distinct().Count() >= 3, "their skin varies");
        Assert.Equal(Outfits.For(17, 1001, false, false, null, Fdp.Core.ForceId.Neutral), Outfits.For(17, 1001, false, false, null, Fdp.Core.ForceId.Neutral));

        var red = new Raylib_cs.Color(200, 50, 50, 255);
        var reds = Enumerable.Range(1, 40).Select(s => Outfits.For(s, 1001, false, false, red, Fdp.Core.ForceId.Neutral)).Where(o => !o.Suit).ToArray();
        Assert.All(reds, o => Assert.True(o.Top.R > 2 * o.Top.G && o.Top.R > 2 * o.Top.B, $"a red type's man wears red ({o.Top.R},{o.Top.G},{o.Top.B})"));
        Assert.True(reds.Select(o => o.Top.R).Distinct().Count() >= 5, "lighter or darker, man by man");
    }

    /// <summary>⭐ CE-1099 (§6q O-2) — what the clothes do to the figure: short sleeves bare the forearms, shorts and a skirt the shins (a
    /// skirt the thighs too, under it), and the details come with them — a skirt, a suit's shirt and tie, long hair down the back.</summary>
    [Fact]
    public void CE1099_TheClothesColourTheParts_AndBringTheirDetails()
    {
        var all = Enumerable.Range(1, 200).Select(s => Outfits.For(s, 1001, false, false, null, Fdp.Core.ForceId.Neutral)).ToArray();
        var details = new List<PartDetail>();

        var skirt = all.First(o => o.Legwear == Legwear.Skirt);
        Assert.Equal(skirt.Skin, Outfits.ColourOfPart(skirt, 0));
        Assert.Equal(skirt.Skin, Outfits.ColourOfPart(skirt, 1));
        Outfits.Details(skirt, 1, details);
        Assert.Contains(details, d => d.Part == BlockFigure.TorsoIndex && d.Centre.Z < -0.5f && d.Colour.Equals(skirt.Bottom) && d.Turned);

        var shorts = all.First(o => o.Legwear == Legwear.Shorts);
        Assert.Equal(shorts.Bottom, Outfits.ColourOfPart(shorts, 0));
        Assert.Equal(shorts.Skin, Outfits.ColourOfPart(shorts, 3));

        var suit = all.First(o => o.Suit);
        Outfits.Details(suit, 1, details);
        Assert.Equal(2, details.Count(d => d.Part == BlockFigure.TorsoIndex && d.Centre.X > 0.5f));   // the shirt and the tie, on his chest
        Assert.Equal(suit.Top, Outfits.ColourOfPart(suit, 9));                                        // a jacket's sleeves

        var longHair = all.First(o => o.HairStyle == HairStyle.Long && !o.Suit && o.Legwear != Legwear.Skirt);
        Outfits.Details(longHair, 1, details);
        Assert.Single(details, d => d.Part == BlockFigure.HeadIndex && d.Centre.X < 0f && d.Colour.Equals(longHair.Hair));

        Assert.Equal(all[0].Skin, Outfits.ColourOfPart(all[0], BlockFigure.HeadIndex));
        Assert.Null(Outfits.ColourOfPart(all[0], BlockFigure.WeaponIndex));
    }

    /// <summary>⭐ CE-1099 (§6q O-4) — a soldier keeps his uniform (his trousers the same cloth darker, no longer black) and his SIDE adds
    /// camouflage: a friend's woodland is fewer, larger patches, a hostile's fleck more, smaller ones; a neutral's uniform is plain.</summary>
    [Fact]
    public void CE1099_ASoldiersSide_GivesHisUniformItsCamouflage()
    {
        var olive = new Raylib_cs.Color(85, 107, 47, 255);
        var friend = Outfits.For(9, 200, soldier: true, child: false, olive, Fdp.Core.ForceId.Friend);
        var hostile = Outfits.For(9, 200, true, false, olive, Fdp.Core.ForceId.Hostile);
        var neutral = Outfits.For(9, 200, true, false, olive, Fdp.Core.ForceId.Neutral);
        Assert.Equal(olive, friend.Top);
        Assert.Equal(olive, Outfits.ColourOfPart(friend, 7));
        Assert.True(friend.Bottom.G < olive.G && friend.Bottom.G > friend.Bottom.R, "his trousers are the uniform's green, darker");
        Assert.Equal((CamoPattern.Woodland, CamoPattern.Fleck, CamoPattern.None), (friend.Camo, hostile.Camo, neutral.Camo));

        // ⭐ CE-1101 (§6r) — the pattern is the SHADER's: no patch boxes; its tones differ by side — fleck reads paler than woodland
        var d = new List<PartDetail>();
        Outfits.Details(friend, 9, d);
        Assert.Empty(d);
        Outfits.Details(hostile, 9, d);
        Assert.Empty(d);
        static float Light((Raylib_cs.Color, Raylib_cs.Color, Raylib_cs.Color) t) => (t.Item1.R + t.Item1.G + t.Item2.R + t.Item2.G + t.Item3.R + t.Item3.G) / 6f;
        Assert.True(Light(Outfits.CamoTones(CamoPattern.Fleck)) > Light(Outfits.CamoTones(CamoPattern.Woodland)) + 20f, "a hostile's fleck reads paler");
        Assert.NotEqual(Outfits.CamoSeed(9), Outfits.CamoSeed(10));   // no two men wear the same cut
        Assert.Equal(Outfits.CamoSeed(9), Outfits.CamoSeed(9));
    }

    /// <summary>⭐ CE-1101 (docs/DESIGN_Map_3D_Mode.md §6r S-3) — faces VARY man by man and are repeatable: eye spacing, mouth width, brow
    /// weight, eye and lip colours; some grown men have a moustache or a beard, never a woman or a child.</summary>
    [Fact]
    public void CE1101_FacesVaryManByMan_AndOnlyGrownMenWearBeards()
    {
        var men = Enumerable.Range(1, 80).Select(s => Outfits.FaceOf(s, 200, woman: false, child: false)).ToArray();
        Assert.Contains(men, f => f.Beard == BeardKind.Beard);
        Assert.Contains(men, f => f.Beard == BeardKind.Moustache);
        Assert.Contains(men, f => f.Beard == BeardKind.None);
        Assert.True(men.Select(f => f.EyeColour).Distinct().Count() >= 4, "eye colours vary");
        Assert.True(men.Max(f => f.EyeSpacing) - men.Min(f => f.EyeSpacing) > 0.05f, "eye spacing varies");
        Assert.True(men.Max(f => f.MouthHalfWidth) - men.Min(f => f.MouthHalfWidth) > 0.05f, "mouths vary");
        Assert.All(men, f => Assert.InRange(f.EyeSpacing + 0.085f, 0f, 0.5f));   // an eye stays on the face
        Assert.All(Enumerable.Range(1, 80), s => Assert.Equal(BeardKind.None, Outfits.FaceOf(s, 1001, woman: true, child: false).Beard));
        Assert.All(Enumerable.Range(1, 80), s => Assert.Equal(BeardKind.None, Outfits.FaceOf(s, 1003, woman: false, child: true).Beard));
        Assert.Equal(Outfits.FaceOf(5, 200, false, false), Outfits.FaceOf(5, 200, false, false));
        Assert.Equal(Outfits.FaceOf(5, 200, false, false), Outfits.For(5, 200, soldier: true, child: false, null, Fdp.Core.ForceId.Friend).Face);
    }

    /// <summary>⭐ CE-1100 (docs/DESIGN_Map_3D_Mode.md §6q O-6) — personal cars of a type with no colour are not all the family blue: each car
    /// has a street colour (mostly white, black, silver, grey), repeatable per car; a car with its own colour keeps its hue, lighter or darker.</summary>
    [Fact]
    public void CE1100_PersonalCars_EachHaveTheirOwnPaint()
    {
        var blue = EntityBodyLayer3D.DefaultColour(VisualFamily.WheeledCar);
        var cars = Enumerable.Range(1, 80).Select(s => Outfits.CarColour(s, 1002, own: null)).ToArray();
        Assert.DoesNotContain(cars, c => c.Equals(blue));
        Assert.True(cars.Select(c => (c.R, c.G, c.B)).Distinct().Count() >= 6, "the paints vary");
        int neutral = cars.Count(c => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 12);
        Assert.InRange(neutral, 40, 80);   // white, black, silver and grey are most of the street
        Assert.Equal(Outfits.CarColour(33, 1002, null), Outfits.CarColour(33, 1002, null));
        var red = new Raylib_cs.Color(180, 30, 30, 255);
        Assert.All(Enumerable.Range(1, 20).Select(s => Outfits.CarColour(s, 1002, red)), c => Assert.True(c.R > 3 * c.G, "a red car stays red"));
        Assert.Equal(VisualFamily.WheeledCar, EntityBodyLayer3D.Resolve(BuiltIn().GetByType(UrbanCombatTkbCatalog.TkbCivilianCar)).Family);
    }

    /// <summary>⭐ CE-1104 (docs/DESIGN_Map_3D_Mode.md §6t) — a team carry is found from the carrier's list and the derived tag: the casualty
    /// on a STRETCHER (held by the front man, the assistant his partner), the front man at its front handles, the assistant at its rear;
    /// a TWO-MAN weapon in the carry station with an assistant makes a front and a rear man too. The load is drawn MIDWAY between the
    /// pair at their hands, facing the front man's way. 📌 Before: the casualty fell to Free (drawn standing), the carrier to Carrying.</summary>
    [Fact]
    public void CE1104_ATeamCarry_IsFoundFromTheList_AndItsLoadHangsBetweenThePair()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsAssistingTag>();
            var front = Man(world, new Vector3(10f, 0f, 0f));
            var rear = Man(world, new Vector3(8f, 0f, 0f));
            var casualty = Man(world, new Vector3(9f, 0f, 0f), health: 20f);
            Carries(world, front, casualty, Fdp.Toolkit.Behavior.Components.CarryMode.Stretcher);
            var list = world.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(front);
            list.Assistant = rear;
            world.SetComponent(front, list);
            world.AddComponent(rear, new Fdp.Toolkit.Behavior.Components.IsAssistingTag { Carrier = front });

            var onIt = Carriage.Of(world, casualty, db);
            Assert.Equal((CarriageKind.OnStretcher, front, rear), (onIt.Kind, onIt.Holder, onIt.Partner));
            var atFront = Carriage.Of(world, front, db);
            Assert.Equal((CarriageKind.StretcherFront, rear), (atFront.Kind, atFront.Partner));
            var atRear = Carriage.Of(world, rear, db);
            Assert.Equal((CarriageKind.StretcherRear, front), (atRear.Kind, atRear.Holder));

            var frame = Carriage.TeamFrame(world.GetComponentRO<SimTransform>(front), world.GetComponentRO<SimTransform>(rear));
            Assert.True(Vector3.Distance(new Vector3(9f, 0f, Carriage.HandleHeight), frame.Translation) < 1e-4f, $"midway at the hands: {frame.Translation}");
            Assert.True(Vector3.TransformNormal(Vector3.UnitX, frame).X > 0.99f, "facing the front man's way");

            // a two-man weapon: the 120 mm mortar in the carry station, an assistant at its rear
            var crewFront = Man(world, new Vector3(20f, 0f, 0f));
            var crewRear = Man(world, new Vector3(18f, 0f, 0f));
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.WeaponSystemParams>()) world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
            var mortar = world.CreateEntity();
            world.AddComponent(mortar, new SimTransform { Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity });
            world.AddComponent(mortar, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1, Portable = (byte)WeaponPortability.TwoMan });
            world.AddComponent(crewFront, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f });
            var crew = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1, Assistant = crewRear };
            crew.Passengers[1] = mortar;
            world.AddComponent(crewFront, crew);
            world.AddComponent(crewRear, new Fdp.Toolkit.Behavior.Components.IsAssistingTag { Carrier = crewFront });
            var teamFront = Carriage.Of(world, crewFront, db);
            Assert.Equal((CarriageKind.TeamFront, mortar, crewRear), (teamFront.Kind, teamFront.Load, teamFront.Partner));
            var teamRear = Carriage.Of(world, crewRear, db);
            Assert.Equal((CarriageKind.TeamRear, crewFront, mortar), (teamRear.Kind, teamRear.Holder, teamRear.Load));
        }
    }

    /// <summary>⭐ CE-1104 (§6t T-b1 … T-b3) — the figures: on the litter he lies on his back with his head toward the front man and the
    /// litter's poles end at the handles; the front man's hands are behind him at the handles' height, the rear man's ahead of him.</summary>
    [Fact]
    public void CE1104_HeLiesOnTheLitter_AndTheirHandsAreOnItsHandles()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        Vector3 At(KitPart p, Vector3 unit) => Vector3.Transform(unit, p.BodyTransform(size));
        var litter = BlockFigure.OnLitter(soldier: true);
        Assert.True(At(litter[BlockFigure.HeadIndex], Vector3.Zero).X > At(litter[BlockFigure.TorsoIndex], Vector3.Zero).X, "his head toward the front man");
        Assert.Equal(FaceOn.Up, litter[BlockFigure.HeadIndex].Face);
        var poles = litter.Where(p => p.Colour == ColourRole.Metal && p.Extent.X * size.X > 1.5f).ToArray();
        Assert.Equal(2, poles.Length);
        Assert.All(poles, p => Assert.InRange(At(p, new Vector3(0.5f, 0f, 0f)).X, BlockFigure.LitterHalfLength - 0.01f, BlockFigure.LitterHalfLength + 0.01f));
        Assert.Single(litter, p => p.Colour == ColourRole.Canvas);

        var standing = BlockFigure.Parts(StanceId.Standing, true);
        var frontHand = At(BlockFigure.HandleArms(standing, front: true, soldier: true)[7], new Vector3(0.5f, 0f, 0f));   // the left forearm's end
        var rearHand = At(BlockFigure.HandleArms(standing, front: false, soldier: true)[7], new Vector3(0.5f, 0f, 0f));
        Assert.True(frontHand.X < -0.1f && MathF.Abs(frontHand.Z - Carriage.HandleHeight) < 0.02f, $"front hands behind him at the handles: {frontHand}");
        Assert.True(rearHand.X > 0.1f && MathF.Abs(rearHand.Z - Carriage.HandleHeight) < 0.02f, $"rear hands ahead of him: {rearHand}");
    }

    /// <summary>⭐ CE-1103 (docs/DESIGN_Map_3D_Mode.md §6s) — the AH-64 is an ATTACK helicopter, not a UH-60: its own kit (a chin gun on a
    /// turret, four rotor blades, a tail rotor, two rocket pods), sized by its TKB box; its rotors spool as a helicopter's.</summary>
    [Fact]
    public void CE1103_TheAh64_HasItsOwnKit_AChinGunTurret_AndRotors()
    {
        var look = EntityBodyLayer3D.Resolve(BuiltIn().GetByType(TkbEntityTypes.Heli_AH64));
        Assert.Equal(VisualFamily.AttackHelicopter, look.Family);
        Assert.Same(ShapeKits.AttackHelicopter, ShapeKits.For(VisualFamily.AttackHelicopter));
        Assert.Equal(new Vector3(17.73f, 14.63f, 3.87f), look.Size);
        var kit = ShapeKits.AttackHelicopter;
        Assert.True(kit.IsArticulated);
        Assert.Single(kit.Parts, p => p.Role == PartRole.Turret);
        Assert.Single(kit.Parts, p => p.Role == PartRole.Gun);
        Assert.Equal(2, kit.Parts.Count(p => p.Role == PartRole.Rotor));
        Assert.Equal(2, kit.Parts.Count(p => p.Role == PartRole.TailRotor));
        Assert.Equal(2, kit.Parts.Count(p => p.Shape == PartShape.Cylinder && p.Axis == PartAxis.X && p.Colour == ColourRole.Dark));   // the pods
        Assert.Equal(VisualFamily.Helicopter, EntityBodyLayer3D.Resolve(BuiltIn().GetByType(TkbEntityTypes.Heli_UH60)).Family);   // the UH-60 stays

        var motion = new MotionTracker();
        var e = new Entity(1, 1);
        float last = 0f;
        for (int k = 0; k < 120; k++)
            last = motion.Advance(e, new Vector3(0f, 0f, 30f), Quaternion.Identity, VisualFamily.AttackHelicopter, look.Size, k / 30.0, rotorTurning: true).Rotor;
        Assert.NotEqual(0f, last);   // its rotor turns
    }

    /// <summary>⭐ CE-1099 (§6q O-3) — the CivilianChild type is a person 1.25 m tall, drawn as a child; a grown pedestrian is not.</summary>
    [Fact]
    public void CE1099_TheChildType_IsASmallPerson_DrawnAsAChild()
    {
        var db = BuiltIn();
        var child = EntityBodyLayer3D.Resolve(db.GetByType(CivilianTkbCatalog.TkbCivilianChild));
        Assert.Equal(VisualFamily.Person, child.Family);
        Assert.True(child.Child);
        Assert.False(child.Soldier);
        Assert.InRange(child.Size.Z, 1.2f, 1.3f);
        var grown = EntityBodyLayer3D.Resolve(db.GetByType(CivilianTkbCatalog.TkbCivilianAdultMale));
        Assert.False(grown.Child);
        Assert.False(grown.TypeColoured);   // ⇒ his clothes come from the everyday palette, not the family green
    }

    /// <summary>
    /// ⭐⭐ CE-1162 (civilian U-1, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U1-a) — the person kit comes from the CLASS: the new civilian
    /// family draws in clothes, a security-force officer in the navy police uniform with no camouflage whatever his side (unarmed until his
    /// type carries a weapon, B-5), an infantryman as before. 🔒 Before: a hard-coded <c>DIS Category == 1</c> drew the police officer as a
    /// civilian.
    /// </summary>
    [Fact]
    public void CE1162_ThePersonKit_ComesFromTheClass_PoliceWearTheirUniform_CiviliansTheirClothes()
    {
        var db = BuiltIn();
        var civilian = EntityBodyLayer3D.Resolve(db.GetByType(Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultMale));
        var police = EntityBodyLayer3D.Resolve(db.GetByType(Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityShield));
        Assert.Equal((VisualFamily.Person, PersonKit.Civilian, false), (civilian.Family, civilian.Kit, civilian.Soldier));
        Assert.Equal((VisualFamily.Person, PersonKit.Security, false), (police.Family, police.Kit, police.Soldier));   // stun grenades only
        var launcher = EntityBodyLayer3D.Resolve(db.GetByType(Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityLauncher));
        Assert.Equal((PersonKit.Security, true), (launcher.Kit, launcher.Soldier));   // a baton launcher is held: he carries it
        foreach (var t in db.GetAll())
            if (t.DisType.Kind == 3 && t.DisType.Category == 1 && EntityBodyLayer3D.Resolve(t).Family == VisualFamily.Person)
                Assert.Equal(PersonKit.Soldier, EntityBodyLayer3D.Resolve(t).Kit);   // every infantryman is still a soldier

        foreach (var side in new[] { ForceId.Neutral, ForceId.Friend, ForceId.Hostile })
        {
            var uniform = Outfits.For(7, Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityShield, police.Soldier, false, null, side, police.Kit);
            Assert.Equal((Outfits.PoliceUniform, CamoPattern.None, Legwear.Trousers), (uniform.Top, uniform.Camo, uniform.Legwear));
        }
        var clothes = Outfits.For(7, Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultMale, civilian.Soldier, false, null, ForceId.Neutral, civilian.Kit);
        Assert.NotEqual(Outfits.PoliceUniform, clothes.Top);
        Assert.False(clothes.Soldier);
    }

    /// <summary>⭐ CE-1162 (civilian U1-b, docs/DESIGN_Civilians_And_Crowd_Control.md §8) — the TYPE says an elderly person (backend F-4,
    /// <c>PersonDto.Age</c>): grey or white hair, long trousers or a skirt (never shorts), on every seed; a younger adult keeps his clothes.</summary>
    [Fact]
    public void CE1162_AnElderlyType_IsDressedAndGreyedAsOne()
    {
        var db = BuiltIn();
        var old = EntityBodyLayer3D.Resolve(db.GetByType(Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianElderly));
        var adult = EntityBodyLayer3D.Resolve(db.GetByType(Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianAdultMale));
        Assert.Equal((true, false, false), (old.Elderly, adult.Elderly, old.Child));
        int grey = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var o = Outfits.For(seed, Hrot.Core.Tkb.CivilianTkbCatalog.TkbCivilianElderly, false, false, null, ForceId.Neutral, old.Kit, old.Elderly);
            Assert.NotEqual(Legwear.Shorts, o.Legwear);
            float sat = MathF.Max(o.Hair.R, MathF.Max(o.Hair.G, o.Hair.B)) - MathF.Min(o.Hair.R, MathF.Min(o.Hair.G, o.Hair.B));
            Assert.True(sat <= 8f && o.Hair.R >= 130, $"seed {seed}: hair {o.Hair.R},{o.Hair.G},{o.Hair.B} is not grey");
            grey++;
        }
        Assert.Equal(40, grey);
    }

    /// <summary>⭐ CE-1164 (civilian U-3, §8 U3-a) — a COWERING civilian (backend F-3, <c>EffectState.Cowering</c>) is drawn crouched with
    /// his forearms over his head: as low as a crouch, and the only parts that differ from the crouch are the arms, which reach above his
    /// head's middle. A soldier never cowers in the drawing; the 2-D line says COWERING.</summary>
    [Fact]
    public void CE1164_ACoweringCivilian_CrouchesWithHisArmsOverHisHead()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
            var man = Man(world, new Vector3(0f, 0f, 0f));
            Assert.False(EntityBodyLayer3D.Cowers(world, man, soldier: false));
            Assert.True(Fdp.Toolkit.Combat.Effects.SetCowering(world, man, true));
            Assert.True(EntityBodyLayer3D.Cowers(world, man, soldier: false));
            Assert.False(EntityBodyLayer3D.Cowers(world, man, soldier: true));
            Assert.Equal("COWERING", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));
            Assert.True(Fdp.Toolkit.Combat.Effects.SetCowering(world, man, false));
            Assert.False(EntityBodyLayer3D.Cowers(world, man, soldier: false));
        }

        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var crouched = BlockFigure.Parts(StanceId.Crouched, soldier: false);
        var cower = BlockFigure.Cower;
        Assert.Equal(crouched.Length, cower.Length);
        Assert.InRange(Top(cower, size), Top(crouched, size) - 0.05f, Top(crouched, size) + 0.15f);
        float headMid = Vector3.Transform(Vector3.Zero, cower[BlockFigure.HeadIndex].BodyTransform(size)).Z;
        var arms = Enumerable.Range(0, cower.Length).Where(i => cower[i] != crouched[i]).ToList();
        Assert.NotEmpty(arms);
        Assert.All(arms, i => Assert.NotEqual(BlockFigure.HeadIndex, i));
        Assert.True(arms.Max(i => Top([cower[i]], size)) > headMid, "the hands are not over the head");
    }

    /// <summary>⭐ CE-1166 (civilian U-6, docs/DESIGN_Civilians_And_Crowd_Control.md §8.7) — each gesture changes ONLY the four arm parts
    /// of a standing and of a crouched figure (the frame's acceptance: the arm boxes differ from the stance's), None changes nothing, a
    /// soldier's rifle is slung, and HoldSign alone adds the pole and a board above his head.</summary>
    [Theory]
    [InlineData(BlockFigure.ArmGesture.HandsUp)]
    [InlineData(BlockFigure.ArmGesture.FistRaised)]
    [InlineData(BlockFigure.ArmGesture.HoldSign)]
    [InlineData(BlockFigure.ArmGesture.Waving)]
    [InlineData(BlockFigure.ArmGesture.Throwing)]
    [InlineData(BlockFigure.ArmGesture.Warning)]
    public void CE1166_EachGesture_ChangesOnlyTheArms(BlockFigure.ArmGesture gesture)
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        foreach (var (stance, crouched) in new[] { (StanceId.Standing, false), (StanceId.Crouched, true) })
        {
            var plain = BlockFigure.Parts(stance, soldier: false);
            Assert.Same(plain, BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.None, crouched, false));
            var shown = BlockFigure.GestureArms(plain, gesture, crouched, soldier: false);
            int extra = gesture == BlockFigure.ArmGesture.HoldSign ? BlockFigure.PlacardParts : 0;
            Assert.Equal(plain.Length + extra, shown.Length);
            var changed = Enumerable.Range(0, plain.Length).Where(i => plain[i] != shown[i]).ToList();
            Assert.Equal(Enumerable.Range(BlockFigure.TorsoIndex + 2, 4), changed);   // the four arm parts, nothing else
            if (gesture == BlockFigure.ArmGesture.HoldSign)
                Assert.True(Top(shown[^1..], size) > Top(plain, size) + 0.3f, $"{stance}: the board is not above his head");

            var armed = BlockFigure.Parts(stance, soldier: true);
            var slung = BlockFigure.GestureArms(armed, gesture, crouched, soldier: true);
            Assert.NotEqual(armed[BlockFigure.WeaponIndex], slung[BlockFigure.WeaponIndex]);
        }
    }

    /// <summary>⭐ CE-1166 (§8.7 U6-b) — the drawing's names ARE backend's (B-6 <c>GestureKind</c>), name for name and byte for byte, so
    /// the cast in <c>Gestured</c> cannot drift: a gesture added on one side fails here.</summary>
    [Fact]
    public void CE1166_TheDrawingsGestures_AreBackendsGestures_ByteForByte()
    {
        Assert.Equal(Enum.GetNames<Fdp.Toolkit.Combat.GestureKind>(), Enum.GetNames<BlockFigure.ArmGesture>());
        foreach (var kind in Enum.GetValues<Fdp.Toolkit.Combat.GestureKind>())
            Assert.Equal(kind.ToString(), ((BlockFigure.ArmGesture)(byte)kind).ToString());
    }

    /// <summary>⭐ CE-1166 (§8.7 U6-a, U6-e) — the replicated gesture reaches the figure: a free man's arms show it; cowering, carrying or
    /// None leave the arms to their owner; the 2-D line says HANDS UP or PROTEST, after any effect.</summary>
    [Fact]
    public void CE1166_TheReplicatedGesture_DrivesTheArms_AndThe2DLine()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Gesture>();
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
            var man = Man(world, new Vector3(0f, 0f, 0f));
            var plain = BlockFigure.Parts(StanceId.Standing, soldier: false);
            Assert.Same(plain, EntityBodyLayer3D.Gestured(world, man, false, FigureCarriage.Free, plain));
            Assert.Null(Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));

            Assert.True(Fdp.Toolkit.Combat.Gestures.Set(world, man, Fdp.Toolkit.Combat.GestureKind.HandsUp));
            var shown = EntityBodyLayer3D.Gestured(world, man, false, FigureCarriage.Free, plain);
            Assert.Equal(BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.HandsUp, false, false, EntityBodyLayer3D.GestureClock(world, man, 0)), shown);   // ⭐ CE-1175: on his own clock
            Assert.Equal("HANDS UP", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));
            Assert.Same(plain, EntityBodyLayer3D.Gestured(world, man, false, new FigureCarriage(CarriageKind.Carrying), plain));

            Assert.True(Fdp.Toolkit.Combat.Gestures.Set(world, man, Fdp.Toolkit.Combat.GestureKind.HoldSign));
            Assert.Equal(plain.Length + BlockFigure.PlacardParts, EntityBodyLayer3D.Gestured(world, man, false, FigureCarriage.Free, plain).Length);
            Assert.Equal("PROTEST", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));

            // ⭐ behaviors' police WARNING (EL-3) is its own pose and word — not the throwing wind-up, not "PROTEST"
            Assert.True(Fdp.Toolkit.Combat.Gestures.Set(world, man, Fdp.Toolkit.Combat.GestureKind.Warning));
            Assert.NotEqual(BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.Throwing, false, false),
                            EntityBodyLayer3D.Gestured(world, man, false, FigureCarriage.Free, plain));
            Assert.Equal("WARNING", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));
            Assert.True(Fdp.Toolkit.Combat.Gestures.Set(world, man, Fdp.Toolkit.Combat.GestureKind.HoldSign));

            Assert.True(Fdp.Toolkit.Combat.Effects.SetCowering(world, man, true));   // cowering owns the arms
            Assert.Same(plain, EntityBodyLayer3D.Gestured(world, man, false, FigureCarriage.Free, plain));
            Assert.Equal("COWERING · PROTEST", Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Text(world, man));
        }
    }

    /// <summary>⭐ CE-1193 (backend's <c>Lean</c>, CE-3257 §13 LN-2) — the drawn lean IS the sim's: a shear about his feet across his left
    /// axis that moves a point at his eye height exactly <see cref="Fdp.Toolkit.Combat.Leans.Shift"/> (left +, right −), leaves the feet
    /// where they are, scales for a crouched eye, and does nothing to a man who does not lean or lies prone.</summary>
    [Fact]
    public void CE1193_TheDrawnLean_IsTheSimsShear()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Combat.Lean>();
            world.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
            var man = Man(world, Vector3.Zero);
            Assert.Equal(Matrix4x4.Identity, EntityBodyLayer3D.LeanShear(world, man));
            float stand = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding;
            float crouch = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightCrouched;

            Assert.True(Fdp.Toolkit.Combat.Leans.Set(world, man, Fdp.Toolkit.Combat.LeanSide.LeanLeft));
            var left = EntityBodyLayer3D.LeanShear(world, man);
            Assert.Equal(Fdp.Toolkit.Combat.Leans.Shift, Vector3.Transform(new Vector3(0f, 0f, stand), left).Y, 4);   // his eye: 0.4 m to his left
            Assert.Equal(stand, Vector3.Transform(new Vector3(0f, 0f, stand), left).Z, 4);                             // not lowered
            Assert.Equal(Vector3.Zero, Vector3.Transform(Vector3.Zero, left));                                          // his feet stay at the corner
            Assert.Equal(0.1f, Vector3.Transform(new Vector3(0.1f, 0f, 1f), left).X, 4);                                // no forward pitch

            Assert.True(Fdp.Toolkit.Combat.Leans.Set(world, man, Fdp.Toolkit.Combat.LeanSide.LeanRight));
            Assert.Equal(-Fdp.Toolkit.Combat.Leans.Shift, Vector3.Transform(new Vector3(0f, 0f, stand), EntityBodyLayer3D.LeanShear(world, man)).Y, 4);

            world.AddComponent(man, new Hrot.MuscleCharacter.Animation.Components.StanceIntent { TargetStance = StanceId.Crouched });
            Assert.Equal(-Fdp.Toolkit.Combat.Leans.Shift, Vector3.Transform(new Vector3(0f, 0f, crouch), EntityBodyLayer3D.LeanShear(world, man)).Y, 4);
            world.GetComponentRW<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(man).TargetStance = StanceId.Prone;
            Assert.Equal(Matrix4x4.Identity, EntityBodyLayer3D.LeanShear(world, man));   // a prone man does not lean (the sim ignores it too)
        }
    }

    /// <summary>⭐ CE-1178 (docs/DESIGN_People_Display.md §5 U17) — the effect / gesture line is a ROW OF HIS CARD, keyed by his network id,
    /// so it sits above his head in 3-D (the card's anchor is the body top, S5b) and above the symbol in 2-D; ⛔ not loose ground text, which
    /// 3-D drew at his feet. A man with no network id (no card) keeps the loose line.</summary>
    [Fact]
    public void CE1178_TheOverheadLine_IsARowOfHisCard_NotTextAtHisFeet()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Gesture>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 10.0 });
            var man = Man(world, new Vector3(0f, 0f, 0f));
            Assert.True(Fdp.Toolkit.Combat.Gestures.Set(world, man, Fdp.Toolkit.Combat.GestureKind.HandsUp));

            Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive[] Emit()
            {
                var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
                new Hrot.Common.Diagnostics.Gizmos.GestureGizmo().Draw(world, man, buffer);
                return buffer.GetFrame().ToArray();
            }

            Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.World, Assert.Single(Emit()).Space);   // no network id: no card, the loose line

            world.AddComponent(man, new Fdp.Toolkit.Replication.Components.NetworkIdentity(1043));
            var prims = Emit();
            var row = Assert.Single(prims);
            Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace.EntityCard, row.Space);
            Assert.Equal(1043, row.AnchorIndex);
            Assert.Equal(Hrot.Common.Diagnostics.Gizmos.EffectStateGizmo.Row, row.ZIndex);
            Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Labels, row.DebugLayer);
            Assert.Equal("HANDS UP", row.TextContent.ToString());

            // the card renderer puts it ABOVE the entity's anchor point (in 3-D that point is the top of his body)
            var at = new Vector2(400f, 300f);
            var card = Assert.Single(new GizmoMap.Presentation.EntityCardRenderer().Layout(prims, id => id == 1043 ? at : null, (s, px) => 7f * s.Length));
            Assert.True(card.Rect.Y + card.Rect.Height < at.Y);
        }

        // and in 3-D the card's point is the top of what is DRAWN: above a protest sign's board, never covering it
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var plain = BlockFigure.Parts(StanceId.Standing, soldier: false);
        var sign = BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.HoldSign, false, false);
        Assert.Equal(size.Z, EntityBodyLayer3D.LabelTop(plain, size), 3);
        Assert.True(EntityBodyLayer3D.LabelTop(sign, size) >= Top([sign[^1]], size) - 1e-3f, "the card covers the sign");
        Assert.True(EntityBodyLayer3D.LabelTop(sign, size) > size.Z + 0.3f);
    }

    /// <summary>⭐ CE-1166 (§8.7 U6-c) — the shapes read as what they are: hands up puts BOTH hands above the head, a raised fist ONE, and
    /// the wind-up puts the throwing hand behind the shoulder line.</summary>
    [Fact]
    public void CE1166_TheShapes_ReadAsTheGesture()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var plain = BlockFigure.Parts(StanceId.Standing, soldier: false);
        float headTop = Top([plain[BlockFigure.HeadIndex]], size);
        int HandsAboveHead(KitPart[] parts) => new[] { BlockFigure.TorsoIndex + 3, BlockFigure.TorsoIndex + 5 }
            .Count(i => Top([parts[i]], size) > headTop);
        Assert.Equal(0, HandsAboveHead(plain));
        Assert.Equal(2, HandsAboveHead(BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.HandsUp, false, false)));
        Assert.Equal(1, HandsAboveHead(BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.FistRaised, false, false)));
        var windUp = BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.Throwing, false, false);
        Assert.True(Vector3.Transform(Vector3.Zero, windUp[BlockFigure.TorsoIndex + 5].BodyTransform(size)).X < -0.1f, "the throwing hand is not back");
    }

    /// <summary>⭐ CE-1175 (docs/DESIGN_People_Display.md §5 U14) — the gestures MOVE, with no state: at t = 0 the static pose; the fist
    /// pumps and the wave waves about the elbow (the forearm keeps its length), the sign bobs 6 cm, hands-up sways, and through the whole
    /// cycle the shape still reads as the gesture (one fist / both hands above the head); the warning and the wind-up hold still. Each man
    /// runs on his own clock (sim time + his seed's offset — the same on every node, different from his neighbour's) and the drawing
    /// passes sim time through.</summary>
    [Fact]
    public void CE1175_TheGestures_Move_OnEachMansOwnClock_AndStillReadAsTheGesture()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var plain = BlockFigure.Parts(StanceId.Standing, soldier: false);
        float headTop = Top([plain[BlockFigure.HeadIndex]], size);
        Vector3 At(KitPart part) => Vector3.Transform(Vector3.Zero, part.BodyTransform(size));
        int HandsAboveHead(KitPart[] parts) => new[] { BlockFigure.TorsoIndex + 3, BlockFigure.TorsoIndex + 5 }
            .Count(i => Top([parts[i]], size) > headTop);
        const int RightForearm = BlockFigure.TorsoIndex + 5;

        foreach (var g in new[] { BlockFigure.ArmGesture.FistRaised, BlockFigure.ArmGesture.HoldSign, BlockFigure.ArmGesture.HandsUp, BlockFigure.ArmGesture.Waving })
        {
            Assert.Equal(BlockFigure.GestureArms(plain, g, false, false), BlockFigure.GestureArms(plain, g, false, false, 0f));
            float hz = g switch { BlockFigure.ArmGesture.FistRaised => 1.4f, BlockFigure.ArmGesture.HoldSign => 0.8f, BlockFigure.ArmGesture.HandsUp => 0.5f, _ => 1.8f };
            var still = BlockFigure.GestureArms(plain, g, false, false);
            var peak = BlockFigure.GestureArms(plain, g, false, false, 1f / (4f * hz));
            Assert.True(Vector3.Distance(At(still[RightForearm]), At(peak[RightForearm])) > 0.02f, $"{g}: the arm does not move");
            Assert.Equal(still[RightForearm].Extent.X, peak[RightForearm].Extent.X, 3);   // a rotation: the forearm keeps its length
        }
        var bobbed = BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.HoldSign, false, false, 1f / (4f * 0.8f));
        var board = BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.HoldSign, false, false);
        float rise = At(bobbed[^1]).Z - At(board[^1]).Z;
        Assert.True(rise > 0.02f, "the sign does not bob");
        Assert.Equal(At(bobbed[RightForearm]).Z - At(board[RightForearm]).Z, rise, 3);   // the board rides with the hands
        for (float t = 0f; t < 2f; t += 0.05f)
        {
            Assert.Equal(1, HandsAboveHead(BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.FistRaised, false, false, t)));
            Assert.Equal(2, HandsAboveHead(BlockFigure.GestureArms(plain, BlockFigure.ArmGesture.HandsUp, false, false, t)));
        }
        foreach (var g in new[] { BlockFigure.ArmGesture.Warning, BlockFigure.ArmGesture.Throwing })
            Assert.Equal(BlockFigure.GestureArms(plain, g, false, false), BlockFigure.GestureArms(plain, g, false, false, 0.37f));

        var (world, db) = HeldWorld();
        using (world)
        {
            world.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            world.RegisterComponent<Fdp.Toolkit.Combat.Gesture>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var a = Man(world, Vector3.Zero);
            var b = Man(world, new Vector3(1f, 0f, 0f));
            world.AddComponent(a, new Fdp.Toolkit.Replication.Components.NetworkIdentity(1043));
            world.AddComponent(b, new Fdp.Toolkit.Replication.Components.NetworkIdentity(1044));
            Assert.Equal(EntityBodyLayer3D.GestureClock(world, a, 12.0) + 1f, EntityBodyLayer3D.GestureClock(world, a, 13.0), 3);
            Assert.True(MathF.Abs(EntityBodyLayer3D.GestureClock(world, a, 12.0) - EntityBodyLayer3D.GestureClock(world, b, 12.0)) > 0.05f,
                        "neighbours pump in step");
            Assert.True(Fdp.Toolkit.Combat.Gestures.Set(world, a, Fdp.Toolkit.Combat.GestureKind.FistRaised));
            Assert.NotEqual(EntityBodyLayer3D.Gestured(world, a, false, FigureCarriage.Free, plain, 12.0),
                            EntityBodyLayer3D.Gestured(world, a, false, FigureCarriage.Free, plain, 12.18));
        }
    }

    /// <summary>⭐ CE-1098 (docs/DESIGN_Map_3D_Mode.md §6p B-2) — how many blood spots: none unhurt, one after any hit, more as health drops,
    /// all eight at none left (a dead man), none for a pool with no maximum.</summary>
    [Fact]
    public void CE1098_TheSpots_GrowWithTheHealthLost()
    {
        Assert.Equal(0, BlockFigure.StainCount(100f, 100f));
        Assert.Equal(1, BlockFigure.StainCount(99f, 100f));
        Assert.Equal(2, BlockFigure.StainCount(85f, 100f));
        Assert.Equal(4, BlockFigure.StainCount(50f, 100f));
        Assert.Equal(7, BlockFigure.StainCount(15f, 100f));
        Assert.Equal(BlockFigure.MaxStains, BlockFigure.StainCount(0f, 100f));
        Assert.Equal(BlockFigure.MaxStains, BlockFigure.StainCount(-20f, 100f));
        Assert.Equal(0, BlockFigure.StainCount(0f, 0f));
    }

    /// <summary>⭐ CE-1098 (§6p B-1, B-3) — a spot is REPEATABLE (the same man's spot k is the same on every call and every node, so his spots
    /// stay put and the earlier ones stay as more appear), different men bleed in different places, and every spot lies flat ON a side of a
    /// body part — never the head (his face), never the rifle.</summary>
    [Fact]
    public void CE1098_TheSpots_AreRepeatable_DifferPerMan_AndLieOnTheBody()
    {
        Assert.Equal(BlockFigure.StainOf(4711, 3), BlockFigure.StainOf(4711, 3));
        var one = Enumerable.Range(0, BlockFigure.MaxStains).Select(k => BlockFigure.StainOf(4711, k)).ToArray();
        var other = Enumerable.Range(0, BlockFigure.MaxStains).Select(k => BlockFigure.StainOf(4712, k)).ToArray();
        Assert.NotEqual(one, other);
        var parts = new HashSet<int>();
        for (long seed = 1; seed <= 200; seed++)
            for (int k = 0; k < BlockFigure.MaxStains; k++)
            {
                var st = BlockFigure.StainOf(seed, k);
                parts.Add(st.Part);
                Assert.NotEqual(BlockFigure.HeadIndex, st.Part);
                Assert.NotEqual(BlockFigure.WeaponIndex, st.Part);
                float[] c = [st.Centre.X, st.Centre.Y, st.Centre.Z];
                Assert.Equal(1, c.Count(v => MathF.Abs(v) > 0.5f));                  // proud of exactly one side
                float[] e = [st.Extent.X, st.Extent.Y, st.Extent.Z];
                for (int i = 0; i < 3; i++)
                    if (MathF.Abs(c[i]) <= 0.5f) Assert.InRange(MathF.Abs(c[i]) + e[i] / 2f, 0f, 0.5f);   // and wholly inside it
            }
        Assert.Contains(BlockFigure.TorsoIndex, parts);
        Assert.Contains(0, parts);   // a thigh
        Assert.Contains(6, parts);   // an upper arm
    }

    /// <summary>⭐ CE-1097 (§6o F-2, F-3, F-4) — WHERE the face is drawn: on a standing soldier the eyes are in front of his head and the helmet
    /// on top of it; on a dead one (on his back) the eyes look UP and the helmet is at the far end of his head from his body; draped over a
    /// shoulder his eyes look DOWN. A soldier's helmet is his uniform a step darker (green stays green); a civilian has hair, no helmet.</summary>
    [Fact]
    public void CE1097_TheFaceIsOnItsSide_TheHelmetOnTop_InTheUniformDarker()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        Vector3 Centre(KitPart head, FaceShade shade)
            => Vector3.Transform(Vector3.Zero, BlockFigure.FaceDetail(BlockFigure.FaceParts(true).First(d => d.Shade == shade), head.Face, head.BodyTransform(size)));
        Vector3 HeadCentre(KitPart head) => Vector3.Transform(Vector3.Zero, head.BodyTransform(size));
        // ⭐ CE-1101 (§6r) — the features are PAINTED on the side BodySurface.AxesOf names (in the head part's own frame)
        Vector3 FaceSide(KitPart head) => Vector3.TransformNormal(BodySurface.AxesOf(head.Face).Face, head.BodyTransform(size));

        var up = BlockFigure.Parts(StanceId.Standing, true)[BlockFigure.HeadIndex];
        Assert.True(FaceSide(up).X > 0.09f, "a standing man's face is the front of his head");
        Assert.True(Centre(up, FaceShade.Helmet).Z > HeadCentre(up).Z + 0.07f, "his helmet is on top");
        Assert.Equal((Vector3.UnitX, Vector3.UnitZ), BodySurface.AxesOf(FaceOn.Front));

        var dead = BlockFigure.Dead(true)[BlockFigure.HeadIndex];
        Assert.True(FaceSide(dead).Z > 0.09f, "a dead man on his back looks up");
        Assert.True(Centre(dead, FaceShade.Helmet).X > HeadCentre(dead).X + 0.07f, "his helmet is at the end of his head, away from his body");

        var draped = BlockFigure.Draped(true)[BlockFigure.HeadIndex];
        Assert.True(FaceSide(draped).Z < -0.09f, "draped over a shoulder he looks down");
        Assert.Single(BlockFigure.FaceParts(true));    // the helmet only: the eyes, nose and mouth are the shader's
        Assert.Single(BlockFigure.FaceParts(false));   // the hair only

        Assert.Contains(BlockFigure.FaceParts(true), d => d.Shade == FaceShade.Helmet);
        Assert.DoesNotContain(BlockFigure.FaceParts(false), d => d.Shade == FaceShade.Helmet);
        Assert.Contains(BlockFigure.FaceParts(false), d => d.Shade == FaceShade.Hair);
        var uniform = EntityBodyLayer3D.DefaultColour(VisualFamily.Person);
        var helmet = EntityBodyLayer3D.FaceColour(FaceShade.Helmet, uniform);
        Assert.True(helmet.G < uniform.G && helmet.G > helmet.R && helmet.G > helmet.B, "a green uniform's helmet is a darker green");
        Assert.Equal(EntityBodyLayer3D.Skin, EntityBodyLayer3D.PartColour(ColourRole.Skin, uniform, wreck: false));
    }

    /// <summary>⭐ CE-1081 (C-1) — the CARRIED man lies across his carrier's shoulders: his middle at the shoulders' height, crosswise (his
    /// head off to the carrier's LEFT and hanging lower), not standing inside the carrier as the sim's position alone would draw him.</summary>
    [Fact]
    public void CE1081_TheCarriedMan_LiesAcrossTheShoulders_HisHeadHangingAtTheLeft()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var carrierBody = Matrix4x4.CreateTranslation(10f, 20f, 0f);   // facing +X
        var frame = Carriage.OnShoulders(carrierBody, size.Z);
        var parts = BlockFigure.Draped(soldier: true);
        var torso = Vector3.Transform(Vector3.Zero, parts[BlockFigure.TorsoIndex].BodyTransform(size) * frame);
        var head = Vector3.Transform(Vector3.Zero, parts[BlockFigure.HeadIndex].BodyTransform(size) * frame);
        Assert.InRange(torso.Z, 1.4f, 1.7f);
        Assert.InRange(torso.X, 9.8f, 10.2f);
        Assert.True(head.Y > torso.Y + 0.2f, $"head {head} is not off to the carrier's left of the torso {torso}");
        Assert.True(head.Z < torso.Z, "the head hangs");
    }

    /// <summary>⭐ CE-1081 / CE-1082 / CE-1083 — every new figure keeps the shared parts-in-order rule (10 parts, 11 with a rifle; the
    /// head at <see cref="BlockFigure.HeadIndex"/>), so blends, the eye and the muzzle find their parts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CE1081_EveryHeldFigure_KeepsTheSharedPartOrder(bool soldier)
    {
        int n = BlockFigure.Parts(StanceId.Standing, soldier).Length;
        foreach (var parts in new[]
                 {
                     BlockFigure.Draped(soldier), BlockFigure.Towed(soldier), BlockFigure.Seated(true, soldier), BlockFigure.Seated(false, soldier),
                     BlockFigure.Downed(soldier), BlockFigure.CarrierArms(BlockFigure.Parts(StanceId.Standing, soldier), drag: true, soldier),
                 })
        {
            Assert.Equal(n, parts.Length);
            Assert.True(parts[BlockFigure.HeadIndex].Extent.Z > 0f);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1083 (C-3) — the doors-off Black Hawk's riders sit IN ITS DOORS: eight open seats with places; a rider's hips are at his seat
    /// (the vehicle turned and moved), he faces out of his door, and his legs hang down outside the fuselage. 🔒 User, 2026-10-14: "some
    /// helicopters might not have side doors … so passenger soldiers can be shown there".
    /// </summary>
    [Fact]
    public void CE1083_ADoorsOffBlackHawk_SeatsItsRidersInTheDoors_LegsOut()
    {
        var db = BuiltIn();
        Assert.True(db.TryGetByType(TkbEntityTypes.Heli_UH60_DoorsOff, out var heli) && heli != null);
        var carrier = heli!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>()!;
        Assert.Equal(Fdp.Toolkit.Behavior.Components.PassengerExposure.Open, carrier.Exposure);
        // ⭐ F4 (Air Ops §7a) — 12 seats now: the 8 riders in the doors, then two door gunners and two door guns forward of them
        var riders = carrier.Seats.Where(s => s.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Passenger).ToList();
        Assert.Equal(8, riders.Count);
        Assert.All(riders, seat => Assert.True(seat.HasPlace && MathF.Abs(seat.Y) > 1f));
        Assert.All(carrier.Seats, seat => Assert.True(seat.HasPlace));
        Assert.Null(db.GetByType(TkbEntityTypes.Heli_UH60)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>());   // the doors-on one is unchanged

        var seat = carrier.Seats[0];                                   // the left door, forward
        var heliBody = Matrix4x4.CreateFromYawPitchRoll(0f, 0f, MathF.PI / 2f) * Matrix4x4.CreateTranslation(100f, 50f, 1.75f);   // turned to face +Y
        var frame = Carriage.InSeat(heliBody, seat);
        var hips = Vector3.Transform(Vector3.Zero, frame);
        var expected = Vector3.Transform(new Vector3(seat.X, seat.Y, seat.Z), heliBody);
        Assert.InRange(Vector3.Distance(hips, expected), 0f, 0.01f);
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var parts = BlockFigure.Seated(legsOut: true, soldier: true);
        var foot = Vector3.Transform(Vector3.Zero, parts[1].BodyTransform(size) * frame);   // the left shin
        var outward = Vector3.TransformNormal(new Vector3(0f, MathF.Sign(seat.Y), 0f), heliBody);
        Assert.True(Vector3.Dot(foot - hips, outward) > 0.25f, $"his legs hang OUT of the door (out {Vector3.Dot(foot - hips, outward):0.00} m)");
        Assert.True(foot.Z < hips.Z - 0.3f, "and down");
    }

    /// <summary>
    /// ⭐ CE-1106 — the doors-off Black Hawk's DOOR GUNNERS sit in the doors like its riders (at 0.95 m off the centreline his body was drawn
    /// inside the fuselage, only his legs and arms showing), legs out, and his M134 stands in front of him, its pivot at his chest.
    /// </summary>
    [Fact]
    public void CE1106_ADoorGunnerSitsInHisDoor_HisGunInFrontOfHim()
    {
        var seats = BuiltIn().GetByType(TkbEntityTypes.Heli_UH60_DoorsOff)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>()!.Seats;
        float riders = seats.Where(s => s.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Passenger).Min(s => MathF.Abs(s.Y));
        foreach (int g in new[] { 8, 10 })
        {
            var (man, gun) = (seats[g], seats[g + 1]);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, man.Role);
            Assert.Equal(g, gun.OperatedBy);
            Assert.True(MathF.Abs(man.Y) >= riders - 1e-3f, $"in the door, not inside the fuselage (|Y| {MathF.Abs(man.Y)} < the riders' {riders})");
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut, man.Pose);
            Assert.True(MathF.Abs(gun.Y) > MathF.Abs(man.Y) && MathF.Sign(gun.Y) == MathF.Sign(man.Y), "the gun outboard of him");
            Assert.InRange(gun.X - man.X, -0.3f, 0.3f);
            Assert.InRange(gun.Z + 0.3f - man.Z, 0.3f, 0.6f);   // the pintle pivot 0.3 m above the station: at his chest
        }
    }

    /// <summary>
    /// ⭐ CE-1108 — the gunner of every armed preset has a PLACE at his gun: the technicals' (104 / 105 / 108 / 109) kneels in the bed
    /// behind the pintle (without a place he was drawn at the pickup's reference point, inside the body); the HMMWV's (102) stands in the
    /// roof hatch (seated at 1.2 m only his head cleared the roof). The place is his feet; the gun's pivot is at his chest height.
    /// </summary>
    [Theory]
    [InlineData(TkbEntityTypes.Truck_HMMWV, Fdp.Toolkit.Tkb.Domain.SeatPose.Stand)]
    [InlineData(TkbEntityTypes.Technical_Hmg, Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel)]
    [InlineData(TkbEntityTypes.Technical_Atgm, Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel)]
    [InlineData(TkbEntityTypes.Technical_Dshk, Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel)]
    [InlineData(TkbEntityTypes.Technical_Minigun, Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel)]
    public void CE1108_AnArmedPresetsGunner_StandsOrKneelsAtHisGun(long type, Fdp.Toolkit.Tkb.Domain.SeatPose pose)
    {
        var db = BuiltIn();
        var seats = db.GetByType(type)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>()!.Seats;
        var station = seats.Single(s => s.Role == Fdp.Toolkit.Behavior.Components.SeatRole.Station);
        var gunner = seats[station.OperatedBy];
        Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, gunner.Role);
        Assert.True(gunner.HasPlace, "a place, or he is drawn inside the body");
        Assert.Equal(pose, gunner.Pose);
        Assert.InRange(station.X - gunner.X, 0f, 0.8f);   // at or just behind his gun
        var pivot = db.GetByType(station.StartWith)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.TurretSuiteDto>()!.Turrets[0].PivotZ;
        Assert.InRange(station.Z + pivot - gunner.Z, 0.9f, 1.7f);   // from his feet to the gun: chest height kneeling or standing
    }

    /// <summary>
    /// ⭐ CE-1084 (C-4) — the JUMP DOWN: a rider seen in a raised seat who is free the next frame is drawn that much higher, falling as
    /// 1 − t² over the free-fall time, then crouched on landing, then free; a seat no higher than standing hips + 0.5 m is a step, not a
    /// jump; a ride not seen ending (a jump in time) shows no fall.
    /// </summary>
    [Fact]
    public void CE1084_LeavingARaisedSeat_IsAFall_ThenACrouchedLanding()
    {
        var drops = new DismountDrop();
        var a = new Entity(1, 1);
        drops.Riding(a, hipsZ: 3.0f, now: 10.0);
        var (lift0, jumping0) = drops.Free(a, standingHipsZ: 0.9f, now: 10.1);
        Assert.True(jumping0);
        Assert.InRange(lift0, 1.0f, 2.1f);
        float fall = DismountDrop.FallSeconds(2.1f);
        var (liftLater, _) = drops.Free(a, 0.9f, 10.1 + fall * 0.8);
        Assert.True(liftLater < lift0 * 0.5f, "falling");
        Assert.Equal((0f, true), drops.Free(a, 0.9f, 10.1 + fall + 0.1));                          // landed, crouched
        Assert.Equal((0f, false), drops.Free(a, 0.9f, 10.1 + fall + DismountDrop.LandSeconds + 0.1)); // up again

        var b = new Entity(2, 1);
        drops.Riding(b, hipsZ: 1.2f, now: 10.0);
        Assert.Equal((0f, false), drops.Free(b, 0.9f, 10.05));                                       // a landed Black Hawk: a step
        var c = new Entity(3, 1);
        drops.Riding(c, hipsZ: 4.0f, now: 10.0);
        Assert.Equal((0f, false), drops.Free(c, 0.9f, 12.0));                                        // not seen ending
    }

    /// <summary>
    /// ⭐ CE-1082 (C-2) — a WOUNDED man limps: his upper body bent ~15°, his right knee no longer bends (a stiff leg), his steps shorter —
    /// standing still he is still bent. ⭐ CE-1081 — a man carrying leans under the load and his hands go up to it; dragging, further.
    /// </summary>
    [Fact]
    public void CE1082_AWoundedManLimps_AndACarrierLeansUnderHisLoad()
    {
        var (world, db) = HeldWorld();
        using (world)
        {
            var e = Man(world, Vector3.Zero, health: 50f);
            var walking = new MotionPose(0f, 0f, 0.5f, Stride: 1f, KneeFlexL: 0.4f, KneeFlexR: 0.4f);
            var parts = BlockFigure.Parts(StanceId.Standing, soldier: true);
            var (_, limp) = EntityBodyLayer3D.Burdened(world, e, Carriage.Of(world, e, db), true, parts, walking);
            Assert.Equal(BlockFigure.WoundedLean, limp.Lean, 3);
            Assert.Equal(0f, limp.KneeFlexR);
            Assert.Equal(0.4f, limp.KneeFlexL, 3);
            Assert.True(limp.Swing < walking.Swing);

            var healthy = Man(world, Vector3.Zero);
            var (sameParts, same) = EntityBodyLayer3D.Burdened(world, healthy, Carriage.Of(world, healthy, db), true, parts, walking);
            Assert.Equal(walking, same);
            Assert.Same(parts, sameParts);

            var bearer = Man(world, Vector3.Zero);
            Carries(world, bearer, Man(world, Vector3.Zero, health: 20f), Fdp.Toolkit.Behavior.Components.CarryMode.Carried);
            var (arms, leaning) = EntityBodyLayer3D.Burdened(world, bearer, Carriage.Of(world, bearer, db), true, parts, walking);
            Assert.Equal(BlockFigure.CarryLean, leaning.Lean, 3);
            var size = ShapeKits.DefaultSize(VisualFamily.Person);
            var hand = Vector3.Transform(new Vector3(0.5f, 0f, 0f), arms[BlockFigure.TorsoIndex + 3].BodyTransform(size));   // left forearm's end
            Assert.True(hand.Z > 1.4f, $"his hand {hand} is up on the load");
        }
    }

    // ── aircraft (SISO-REF-010 platform / air categories) ──

    [Theory]
    [InlineData(20, VisualFamily.AttackHelicopter)]
    [InlineData(21, VisualFamily.Helicopter)]
    [InlineData(23, VisualFamily.Helicopter)]
    [InlineData(25, VisualFamily.Helicopter)]
    [InlineData(1, VisualFamily.Jet)]
    [InlineData(2, VisualFamily.AttackJet)]   // ⭐ CE-1092
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

    /// <summary>⭐ WL-6d — the CRAWL: elbows take turns reaching forward, the OPPOSITE knee draws up and out; it stays low; the leg
    /// segments keep their lengths; the rifle is carried, not aimed (well below the shoulder line and never ahead of the hands' reach);
    /// a figure not moving is in his prone pose; the tracker crawls one cycle per 0.7 m.</summary>
    [Fact]
    public void WL6d_ACrawlAlternatesElbowAndOppositeKnee_LowAndCarryingTheRifle()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var prone = BlockFigure.Parts(StanceId.Prone, true, WeaponPostureKind.Aimed);
        Assert.Same(prone, BlockFigure.Crawl(prone, true, 1f, 0f));
        Assert.Equal(prone.Length, BlockFigure.CrawlPose(true, 0f).Length);
        Assert.Equal(BlockFigure.Parts(StanceId.Prone, false).Length, BlockFigure.CrawlPose(false, 0f).Length);

        int t = BlockFigure.TorsoIndex;
        Vector3 ElbowL(KitPart[] p) => Point(p[t + 2], size, 0.5f);
        Vector3 ElbowR(KitPart[] p) => Point(p[t + 4], size, 0.5f);
        Vector3 AnkleL(KitPart[] p) => Point(p[1], size, 0.5f);
        Vector3 AnkleR(KitPart[] p) => Point(p[3], size, 0.5f);
        var leftReach = BlockFigure.CrawlPose(true, MathF.PI / 2f);
        Assert.True(ElbowL(leftReach).X > ElbowR(leftReach).X + 0.25f, "the left elbow reaches forward");
        Assert.True(AnkleR(leftReach).X > AnkleL(leftReach).X + 0.3f && -AnkleR(leftReach).Y > AnkleL(leftReach).Y + 0.2f,
                    $"the right knee draws up and out: R {AnkleR(leftReach)} L {AnkleL(leftReach)}");
        var rightReach = BlockFigure.CrawlPose(true, 3f * MathF.PI / 2f);
        Assert.True(ElbowR(rightReach).X > ElbowL(rightReach).X + 0.25f, "then the right elbow");
        Assert.True(AnkleL(rightReach).X > AnkleR(rightReach).X + 0.3f, "and the left knee");

        for (float phase = 0f; phase < 6.3f; phase += 0.7f)
        {
            var p = BlockFigure.CrawlPose(true, phase);
            Assert.All(p, part => Assert.True(Vector3.Transform(Vector3.Zero, part.BodyTransform(size)).Z < 0.4f, "he stays low"));
            for (int leg = 0; leg <= 2; leg += 2)
            {
                Assert.Equal(0.44f, Vector3.Distance(Point(p[leg], size, -0.5f), Point(p[leg], size, 0.5f)), 2);   // thigh
                Assert.Equal(0.44f, Vector3.Distance(Point(p[leg + 1], size, -0.5f), Point(p[leg + 1], size, 0.5f)), 2);   // shin
            }
            Assert.InRange(Vector3.Distance(Point(p[t + 5], size, 0.5f), Point(p[^1], size, -0.5f)), 0.15f, 0.25f);   // the right hand holds the rifle a quarter along
        }

        var tracker = new MotionTracker();
        var man = new Entity(12, 1);
        MotionPose first = tracker.Advance(man, Vector3.Zero, Quaternion.Identity, VisualFamily.Person, size, 0.0, false), pose = first;
        for (int i = 1; i <= 35; i++) pose = tracker.Advance(man, new Vector3(i * 0.01f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, i / 60.0, false);   // 0.6 m/s
        Assert.Equal(0.35f / MotionTracker.CrawlMetres * MathF.PI * 2f, pose.CrawlPhase, 3);   // half a cycle in 0.35 m
        Assert.Equal(1f, pose.Moving, 2);
        Assert.Equal(0f, tracker.Advance(man, new Vector3(0.35f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, 36 / 60.0, false).Moving);
    }

    // ── ⭐ CE-1064 (docs/DESIGN_Map_3D_Mode.md §6j) — the crouched figure: a kneel when still, a crouched run on the move ─────────────

    /// <summary>⭐ CE-1064 — crouched and STILL, he KNEELS: the firing-side shin lies on the ground, the support shin stands upright with
    /// its foot planted forward, and his hips are over his feet (between the planted foot and the kneeling heel) — ⛔ not behind both
    /// feet, the "sitting on an invisible chair" squat it replaced. The head stays at the crouched stance's height.</summary>
    [Fact]
    public void CE1064_CrouchedAndStill_HeKneels_HipsOverHisFeet()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        foreach (bool soldier in new[] { true, false })
        {
            var kneel = BlockFigure.Parts(StanceId.Crouched, soldier);
            Vector3 rightShinA = Point(kneel[3], size, -0.5f), rightShinB = Point(kneel[3], size, 0.5f);
            Assert.True(rightShinA.Z < 0.12f && rightShinB.Z < 0.12f, $"the kneeling shin lies on the ground: {rightShinA} {rightShinB}");
            Vector3 leftKnee = Point(kneel[1], size, -0.5f), leftFoot = Point(kneel[1], size, 0.5f);
            Assert.True(leftKnee.Z > 0.35f && leftFoot.Z < 0.05f && MathF.Abs(leftKnee.X - leftFoot.X) < 0.08f,
                        $"the support shin stands upright on a planted foot: {leftKnee} {leftFoot}");
            float hipX = Point(kneel[0], size, -0.5f).X;
            float backFoot = MathF.Min(rightShinA.X, rightShinB.X);
            Assert.True(backFoot < hipX && hipX < leftFoot.X, $"the hips are between the feet: {backFoot} < {hipX} < {leftFoot.X}");
            float torsoX = Point(kneel[BlockFigure.TorsoIndex], size, 0f).X;
            Assert.True(backFoot < torsoX && torsoX < leftFoot.X, $"the torso is over the feet: {torsoX}");
            Assert.All(kneel.Take(BlockFigure.TorsoIndex), p => Assert.Equal(PartRole.Hull, p.Role));   // still: nothing swings
        }
    }

    /// <summary>⭐ CE-1064 — crouched and MOVING (R-265's crouched run), the legs SWING: the crouched run's leg parts take the swing roles,
    /// so the gait moves the feet; the hips drop to the run's height, both feet come under him, and his head stays below a standing
    /// man's. ⛔ Before, a crouched figure's legs had no swing role and slid along frozen.</summary>
    [Fact]
    public void CE1064_CrouchedAndMoving_TheLegsSwing_LowAndKneesBent()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        foreach (bool soldier in new[] { true, false })
        {
            var still = BlockFigure.Parts(StanceId.Crouched, soldier, WeaponPostureKind.Ready, WeaponPostureKind.Ready, 1f);
            var run = BlockFigure.CrouchedMoving(still, soldier, WeaponPostureKind.Ready, WeaponPostureKind.Ready, 1f, 1f);
            Assert.Equal(still.Length, run.Length);
            Assert.Equal(PartRole.LegLeft, run[0].Role); Assert.Equal(PartRole.LegLeft, run[1].Role);
            Assert.Equal(PartRole.LegRight, run[2].Role); Assert.Equal(PartRole.LegRight, run[3].Role);
            Assert.Equal(0.90f - BlockFigure.CrouchRunDrop, Point(run[0], size, -0.5f).Z, 2);   // the run's hips
            for (int i = 1; i <= 3; i += 2)
            {
                var ankle = Point(run[i], size, 0.5f);
                Assert.InRange(ankle.Z, 0f, 0.05f);
                Assert.True(Vector3.Distance(Point(run[i - 1], size, -0.5f), ankle) < 0.80f, "knees bent");
            }
            // the gait moves the left foot forward on a positive swing (thigh and shin from the hip)
            var foot = Vector3.Transform(new Vector3(0.5f, 0, 0), run[1].BodyTransform(size, default, ArticulationPose.Neutral, new MotionPose(0, 0, 0.4f)));
            Assert.True(foot.X > Point(run[1], size, 0.5f).X + 0.1f, $"the left foot swings forward: {foot}");
            Assert.True(Top(run, size) < 1.55f, $"a crouched run stays low: {Top(run, size)}");
            Assert.Same(still, BlockFigure.CrouchedMoving(still, soldier, WeaponPostureKind.Ready, WeaponPostureKind.Ready, 1f, 0f));
        }
    }

    /// <summary>⭐ CE-1064 — a frame in which NO sim time passed and he did not move (a paused sim, a frame drawn between two sim ticks)
    /// keeps his last gait — a pause freezes him mid-stride (R-143) — while one in which time passed and he did not move stops him.</summary>
    [Fact]
    public void CE1064_AFrameWithNoSimTime_KeepsTheGait_AStopEndsIt()
    {
        var t = new MotionTracker();
        var man = new Entity(12, 1);
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        MotionPose walking = default;
        for (int i = 0; i <= 20; i++) walking = t.Advance(man, new Vector3(i * 0.1f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, i / 30.0, false);
        Assert.True(walking.Stride > 0.9f && walking.Swing != 0f, $"walking at 3 m/s: {walking}");
        var paused = t.Advance(man, new Vector3(2.0f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, 20 / 30.0, false);
        Assert.Equal(walking.Swing, paused.Swing);
        Assert.Equal(walking.Stride, paused.Stride);
        Assert.Equal(walking.GaitPhase, paused.GaitPhase);
        var stopped = t.Advance(man, new Vector3(2.0f, 0, 0), Quaternion.Identity, VisualFamily.Person, size, 21 / 30.0, false);
        Assert.Equal(0f, stopped.Stride);
        Assert.Equal(0f, stopped.Swing);
    }

    // ── ⭐ CE-1070 (docs/DESIGN_Visual_Effects.md §6c) — the muzzle flash at the weapon ─────────────────────────────────────────────

    /// <summary>⭐ CE-1070 — a rifleman has no TKB muzzle, so his flash starts where the rifle he is DRAWN holding ends: at the
    /// chest-low muzzle when ready, at eye height pointing at the target when aimed — never above his head (the old box-front fallback
    /// put it at 1.35 m, 0.55 m in front of a crouched man's face).</summary>
    [Fact]
    public void CE1070_ARiflemansMuzzle_IsTheEndOfTheRifleHeIsDrawnHolding()
    {
        var size = ShapeKits.DefaultSize(VisualFamily.Person);
        var body = Matrix4x4.CreateTranslation(100f, 50f, 2f);   // standing at (100, 50) on ground 2 m up, facing +x
        (Vector3 Tip, Vector3 Forward) TipOf(StanceId stance, WeaponPostureKind posture)
        {
            var rifle = BlockFigure.Parts(stance, true, posture).Last(p => p.Colour == ColourRole.Metal);
            return EntityBodyLayer3D.WeaponTip(rifle, size, default, ArticulationPose.Neutral, MotionPose.Still, body);
        }
        var ready = TipOf(StanceId.Standing, WeaponPostureKind.Ready);
        Assert.InRange(ready.Tip.Z - 2f, 0.7f, 1.2f);                       // chest-low, muzzle down and forward
        Assert.True(ready.Tip.X - 100f > 0.6f, $"in front of him: {ready.Tip}");
        Assert.True(ready.Forward.X > 0.7f && ready.Forward.Z < 0f, $"pointing forward and down: {ready.Forward}");
        var aimed = TipOf(StanceId.Standing, WeaponPostureKind.Aimed);
        Assert.InRange(aimed.Tip.Z - 2f, 1.2f, 1.45f);                      // shouldered, under the eye
        Assert.True(aimed.Forward.X > 0.95f, $"level, at the target: {aimed.Forward}");
        var crouched = TipOf(StanceId.Crouched, WeaponPostureKind.Aimed);
        Assert.True(crouched.Tip.Z - 2f < 1.1f, $"below a crouched man's eye: {crouched.Tip}");   // the old fallback said 1.35 m
    }

    /// <summary>⭐ CE-1070 — a tank's barrel direction comes from the same pose as its muzzle (turned with the hull), and the flash's
    /// fireball is centred FORWARD of the muzzle along it by most of its radius — out of the barrel, not swallowing it.</summary>
    [Fact]
    public void CE1070_ATankFlash_BloomsForwardOfItsMuzzle_AlongTheBarrel()
    {
        var w = new EntityRepository();
        w.RegisterComponent<SimTransform>();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        w.SetSingletonManaged<ITkbDatabase>(BuiltIn());
        var tank = w.CreateEntity();
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);   // facing north (+y)
        w.AddComponent(tank, new SimTransform { Position = new Vector3(10f, 20f, 0f), Rotation = yaw });
        w.AddComponent(tank, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
        Assert.True(Fdp.Toolkit.Combat.Muzzle.TryOf(w, tank, 0, out var muzzle, out var forward));
        Assert.InRange(Vector3.Distance(forward, Vector3.UnitY), 0f, 1e-4f);   // the barrel points where the hull does (turret neutral)
        Assert.True(muzzle.Y > 20f + 4f, $"the muzzle is at the barrel's end: {muzzle}");

        var r = new Hrot.UI.Common.Effects.EffectLayer.Resolved(
            EffectTkbCatalog.VisualOf(BuiltIn(), EffectTkbCatalog.MuzzleFlashLarge)!, muzzle, Vector3.Zero, 0f, 1f, default, Forward: forward);
        float radius = 2f;
        var centre = Hrot.UI.Common.Effects.EffectLayer.FlashCentre(r, radius);
        Assert.InRange(Vector3.Distance(centre, muzzle + forward * radius * Hrot.UI.Common.Effects.EffectLayer.FlashForwardOfMuzzle), 0f, 1e-4f);
        Assert.Equal(muzzle.Z, centre.Z, 4);                                 // not above the gun
        Assert.Equal(muzzle, Hrot.UI.Common.Effects.EffectLayer.FlashCentre(r with { Forward = Vector3.Zero }, radius));   // no direction: at the muzzle
    }

    // ── ⭐ CE-1068 (docs/DESIGN_Map_3D_Mode.md §6k) — a door is its LEAF, hinged in its doorway ───────────────────────────────────

    internal static TerrainWorld BtRange()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln"))) dir = Path.GetDirectoryName(dir)!;
        var file = Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "bt-range", "bt-range.world.geojson");
        return TerrainWorldParser.Parse(File.ReadAllText(file), "bt-range", TerrainAssets.ForFolder(Path.GetDirectoryName(file)!));
    }

    /// <summary>⭐ CE-1068 — the leaf fills its doorway shut (hinge → closed tip, sill → head, the leaf's thickness), turns about the
    /// hinge to stand where the door layer's open line ends, and a destroyed door asks for no angle (it is not drawn).</summary>
    [Fact]
    public void CE1068_ADoorLeaf_TurnsOnItsHinge_AndADestroyedDoorIsNotDrawn()
    {
        var terrain = BtRange();
        Assert.True(terrain.DoorLeaves.Count > 0, "bt-range has doors");
        var leaf = terrain.DoorLeaves[0];
        var (hinge, closedTip) = Hrot.ScenarioEditor.Gizmos.DoorLeafGizmo.Leaf(leaf.Footprint);
        float width = Vector2.Distance(hinge, closedTip);

        var (c0, r0, h0) = DoorLeaf3D.Box(leaf, 0f);
        Assert.Equal(width / 2f, h0.X, 3);
        Assert.Equal((leaf.TopZ - leaf.BaseZ) / 2f, h0.Z, 3);
        Assert.InRange(h0.Y, 0.01f, 0.05f);                                            // a 5 cm leaf
        Assert.InRange(Vector2.Distance(new Vector2(c0.X, c0.Y), (hinge + closedTip) / 2f), 0f, 1e-3f);   // shut: in the doorway

        var (c1, r1, _) = DoorLeaf3D.Box(leaf, DoorLeaf3D.OpenAngle);
        var farEdge = new Vector2(c1.X, c1.Y) + (new Vector2(c1.X, c1.Y) - hinge);      // the edge opposite the hinge
        var openTip = Hrot.ScenarioEditor.Gizmos.DoorLeafGizmo.OpenTip(hinge, closedTip);
        Assert.InRange(Vector2.Distance(farEdge, openTip), 0f, 1e-3f);                 // the same side the 2-D layer opens it to
        Assert.InRange(Vector2.Distance(hinge, new Vector2(c1.X, c1.Y)), width / 2f - 1e-3f, width / 2f + 1e-3f);   // still on its hinge

        Assert.Equal(0f, DoorLeaf3D.TargetAngle(TerrainDoorState.Closed));
        Assert.Equal(0f, DoorLeaf3D.TargetAngle(TerrainDoorState.Locked));
        Assert.Equal(DoorLeaf3D.OpenAngle, DoorLeaf3D.TargetAngle(TerrainDoorState.Open));
        Assert.Null(DoorLeaf3D.TargetAngle(TerrainDoorState.Destroyed));
    }

    /// <summary>⭐ CE-1069 — a LOCKED door is told apart in 3-D by colour: its wood tinted half-way to the 2-D door layer's locked
    /// purple (one colour language on both maps); shut and open are plain wood (the swing shows which).</summary>
    [Fact]
    public void CE1069_ALockedDoor_IsTintedWithTheTwoDLockedColour()
    {
        Assert.Equal(DoorLeaf3D.Wood, DoorLeaf3D.ColourOf(TerrainDoorState.Closed));
        Assert.Equal(DoorLeaf3D.Wood, DoorLeaf3D.ColourOf(TerrainDoorState.Open));
        var locked = DoorLeaf3D.ColourOf(TerrainDoorState.Locked);
        var purple = Hrot.ScenarioEditor.Gizmos.DoorLeafGizmo.ColorOf(TerrainDoorState.Locked);
        Assert.NotEqual(DoorLeaf3D.Wood, locked);
        Assert.Equal((DoorLeaf3D.Wood.B + purple.B) / 2, locked.B);
        Assert.True(locked.B > DoorLeaf3D.Wood.B + 30, $"visibly purple: {locked.R},{locked.G},{locked.B}");
    }

    /// <summary>⭐ CE-1068 — the leaf SWINGS on sim time (a quarter turn in <see cref="DoorLeaf3D.SwingSeconds"/>), a pause holds
    /// it, and a door first seen just stands in its state.</summary>
    [Fact]
    public void CE1068_ADoorSwings_OnSimTime_AndIsFirstSeenInItsState()
    {
        var swing = new DoorSwing();
        var door = new Entity(5, 1);
        Assert.Equal(DoorLeaf3D.OpenAngle, swing.Angle(door, DoorLeaf3D.OpenAngle, 10.0));   // first seen: already open
        float half = swing.Angle(door, 0f, 10.0 + DoorLeaf3D.SwingSeconds / 2.0);         // closing: half way after half the time
        Assert.Equal(DoorLeaf3D.OpenAngle / 2f, half, 3);
        Assert.Equal(half, swing.Angle(door, 0f, 10.0 + DoorLeaf3D.SwingSeconds / 2.0));  // paused: no time, no swing
        Assert.Equal(0f, swing.Angle(door, 0f, 12.0));                                     // shut
    }

    /// <summary>⭐ CE-1068 — a DOOR entity has no shape kit (it used to be an unknown box) and is picked and selected by its LEAF:
    /// open it and its box turns with it, destroy it and there is nothing to pick.</summary>
    [Fact]
    public void CE1068_ADoorEntity_HasNoKit_AndIsPickedByItsLeaf()
    {
        var terrain = BtRange();
        var world = WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        world.RegisterComponent<DoorState>();
        world.RegisterManagedComponent<TerrainObjectKey>();
        var def = terrain.Doors[0];
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(def.Center, def.SillZ), Rotation = Quaternion.Identity });
        world.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Hrot.Map.Common.TkbEntityTypes.Door });
        world.AddComponent(e, new DoorState { State = TerrainDoorState.Closed });
        world.AddComponent(e, new TerrainObjectKey { Key = def.Key });

        var bodies = new EntityBodyLayer3D(() => world, () => BuiltIn());
        Assert.Equal(VisualFamily.Unit, bodies.LookOf(Hrot.Map.Common.TkbEntityTypes.Door, BuiltIn()).Family);   // no kit, never Unknown

        Assert.True(bodies.TryGetBox(world, e, BuiltIn(), out var centre, out var rotation, out var half));
        var (c0, r0, _) = DoorLeaf3D.Box(terrain.DoorLeaves[0], 0f);
        Assert.InRange(Vector3.Distance(centre, c0), 0f, 1e-3f);
        Assert.Equal(r0, rotation);
        Assert.True(half.Z > 0.9f, $"the pick box is the door's height: {half}");

        world.SetComponent(e, new DoorState { State = TerrainDoorState.Destroyed });
        Assert.False(bodies.TryGetBox(world, e, BuiltIn(), out _, out _, out _));   // gone: nothing to pick
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
        // ⭐ CE-1093 (N-9) — parked, it runs on, then spools down and STANDS (no longer the instant the height test failed)
        float last = 0f;
        for (double s = 11.0; s <= 11.0 + MotionTracker.RotorRunOnSeconds + MotionTracker.RotorSpoolSeconds + 1.0; s += 0.1)
            last = t.Advance(heli, Vector3.Zero, Quaternion.Identity, VisualFamily.Helicopter, size, s, rotorTurning: false).Rotor;
        Assert.Equal(last, t.Advance(heli, Vector3.Zero, Quaternion.Identity, VisualFamily.Helicopter, size, 40.0, rotorTurning: false).Rotor);
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
        // ⭐ CE-3228 — the request rides the world's bus, registered on every creating host (Entity Creation §3.4d)
        w.RegisterManagedEvent<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>();
        List<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest> Asked()
        {
            w.Bus.SwapBuffers();
            return w.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>().ToList();
        }
        var (_, sw, two) = Map(new Vector2(10f, 20f), 2f);
        var layer = new Hrot.UI.Common.Map3D.MapCameraLayer(sw, () => w);
        layer.Update(0.016f);                                  // the baseline
        Assert.Empty(Asked());

        two.ApplyCameraView(new MapCameraView { Target = new Vector2(300f, 400f), Zoom = 2f, SmoothTarget = new Vector2(300f, 400f), SmoothZoom = 2f });
        layer.Update(0.016f);                                  // a move …
        Assert.Empty(Asked());                                 // … nothing asked while the view is still changing
        for (int i = 0; i < 40; i++) layer.Update(0.016f);     // still for > SettleSeconds

        var r = Assert.Single(Asked());                          // one request, not one per frame
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
        Assert.Empty(Asked());                                 // moved, never asked for a second one
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

    /// <summary>⭐ CE-1165 (civilian U-4, docs/DESIGN_Civilians_And_Crowd_Control.md §8.8 U4-f) — a gas / smoke CLOUD is drawn by the
    /// effect layer, never as a unit: no 3-D body (TKB 7202), and the one "not a unit" rule (<c>NotAUnit</c>) keeps the 2-D symbol, anchor
    /// and pick box away — the same entity without its cloud component draws as an ordinary unit.</summary>
    [Fact]
    public void CE1165_ACloudIsNotAUnit_NoBody_NoSymbol()
    {
        var bodies = new EntityBodyLayer3D(() => null, () => null);
        Assert.Equal(VisualFamily.Unit, bodies.LookOf(Hrot.Core.Tkb.AreaEffectCloudTkb.Type, null).Family);   // no body, not pickable

        using var w = new EntityRepository();
        w.RegisterComponent<SimTransform>();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        w.RegisterComponent<Fdp.Toolkit.Combat.AreaEffectCloud>();
        var e = w.CreateEntity();
        w.AddComponent(e, new SimTransform { Position = new Vector3(5f, 5f, 0f), Rotation = Quaternion.Identity });
        w.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4712));
        w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Hrot.Core.Tkb.AreaEffectCloudTkb.Type });
        w.AddComponent(e, new Fdp.Toolkit.Combat.AreaEffectCloud { Kind = Fdp.Toolkit.Combat.CloudKind.Smoke, Radius = 8f, Height = 4f, Seconds = 60f });
        Assert.True(Hrot.Common.Diagnostics.Gizmos.NotAUnit.Is(w, e));

        var draw = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        new Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo().Draw(w, e, draw);
        Assert.Equal(0, draw.GetFrame().Length);

        w.RemoveComponent<Fdp.Toolkit.Combat.AreaEffectCloud>(e);   // the same entity as a unit: it draws
        Assert.False(Hrot.Common.Diagnostics.Gizmos.NotAUnit.Is(w, e));
        var unit = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        new Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo().Draw(w, e, unit);
        Assert.NotEqual(0, unit.GetFrame().Length);
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

        Assert.Equal(new ArticulationPose(0f, 0f, Published: true), source.PoseOf(w, tank));   // ⭐ CE-1114 — a part's level pose is TRUE, not "none"
        w.GetComponentRW<Fdp.Toolkit.Combat.Components.TurretPose>(turret) =
            new Fdp.Toolkit.Combat.Components.TurretPose { Azimuth = 0.7f, Elevation = 0.1f };
        Assert.Equal(new ArticulationPose(0.7f, 0.1f, Published: true), source.PoseOf(w, tank));
        Assert.Equal(ArticulationPose.Neutral, source.PoseOf(w, bare));
        w.DestroyEntity(turret);
        Assert.Equal(ArticulationPose.Neutral, source.PoseOf(w, tank));
    }

    /// <summary>
    /// ⭐ CE-1114 (§6n N-5) — a mortar / towed gun / ZU-23 is drawn at its turret part's PUBLISHED pose, azimuth and elevation (backend P2
    /// lays it by W-9's replicated <c>TurretPose</c>) — a level gun at 0°, not the default; only a gun with no turret part is drawn at the
    /// default lay (60° for a mortar).
    /// </summary>
    [Fact]
    public void CE1114_AWeaponSystem_IsDrawnAtItsPublishedElevation_ALevelOneIncluded()
    {
        using var w = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(w);
        var laid = w.CreateEntity();
        var bare = w.CreateEntity();
        var turret = Fdp.Toolkit.Combat.TurretParts.Ensure(w, laid, 0);
        var source = new TurretPoseSource();
        var layer = new EntityBodyLayer3D(() => w, () => null, source.PoseOf);
        float mortarDefault = WeaponKits.DefaultElevation(WeaponKind.Mortar);

        w.GetComponentRW<Fdp.Toolkit.Combat.Components.TurretPose>(turret) =
            new Fdp.Toolkit.Combat.Components.TurretPose { Azimuth = 1.2f, Elevation = 1.3f };   // laid at ~74°, turned left
        var pose = layer.LaidPose(w, laid, mortarDefault);
        Assert.Equal(1.2f, pose.TurretAzimuth);
        Assert.Equal(1.3f, pose.GunElevation);

        w.GetComponentRW<Fdp.Toolkit.Combat.Components.TurretPose>(turret) =
            new Fdp.Toolkit.Combat.Components.TurretPose { Azimuth = 0.4f, Elevation = 0f };     // a gun laid level (a ZU-23 on the horizon)
        Assert.Equal(0f, layer.LaidPose(w, laid, mortarDefault).GunElevation);

        Assert.Equal(mortarDefault, layer.LaidPose(w, bare, mortarDefault).GunElevation);        // no turret part: the default lay
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
    [InlineData(TkbEntityTypes.Heli_AH64)]   // ⭐ CE-1103 — a CG-referenced body: its chin gun is placed through the box offset
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
        // ⭐ CE-1103 — the kit is drawn in its box; a Body.Geometry shifts the box off the reference point (the renderer's kitToBody)
        var kitToBody = look.Geometry is { } g
            ? Matrix4x4.CreateTranslation(g.BodyCentreX, g.BodyCentreY, g.BodyCentreZ - look.Size.Z / 2f) : Matrix4x4.Identity;
        var tip = Vector3.Transform(new Vector3(0f, 0f, 1f), gun.BodyTransform(look.Size, pivots, pose) * kitToBody * body);   // the unit cylinder's far end

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

    // ── ⭐ CE-1122 (docs/DESIGN_Map_3D_Mode.md §6y) — blood where he was hit ────────────────────────────────────────────────────────

    /// <summary>⭐ CE-1122 (§6y HM-1, HM-2) — a mark lies flat on the face its point is on, just proud of it, and is sized in METRES: a
    /// bullet's 9 cm spot on the torso's front, a fragment's smaller, a burn's larger and charred; on a forearm it is round, not stretched.</summary>
    [Fact]
    public void CE1122_AMark_LiesFlatOnTheFaceItEntered_SizedInMetres()
    {
        var torso = new Vector3(0.24f, 0.40f, 0.56f);
        var front = BlockFigure.MarkStain(BlockFigure.TorsoIndex, new Vector3(0.5f, 0.1f, -0.2f), Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Bullet, true, torso);
        Assert.Equal((BlockFigure.TorsoIndex, 0.51f, 0.1f, -0.2f), (front.Part, front.Centre.X, front.Centre.Y, front.Centre.Z));
        Assert.Equal(0.03f, front.Extent.X);
        Assert.Equal(BlockFigure.BulletMarkM / torso.Y, front.Extent.Y, 4);
        Assert.Equal(BlockFigure.BulletMarkM / torso.Z, front.Extent.Z, 4);
        Assert.False(front.Char);

        var side = BlockFigure.MarkStain(BlockFigure.TorsoIndex, new Vector3(0.1f, -0.5f, 0.3f), Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Fragment, false, torso);
        Assert.Equal(-0.51f, side.Centre.Y);
        Assert.True(side.Extent.Z < front.Extent.Z, "a fragment's spot is smaller than a bullet's");
        var burn = BlockFigure.MarkStain(BlockFigure.TorsoIndex, new Vector3(0f, 0f, 0.5f), Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Burn, false, torso);
        Assert.True(burn.Char && burn.Extent.X > front.Extent.Y);
        Assert.Equal(0.51f, burn.Centre.Z);

        var forearm = new Vector3(0.30f, 0.10f, 0.10f);   // a limb runs along its own x
        var arm = BlockFigure.MarkStain(7, new Vector3(0.1f, 0.5f, 0f), Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Bullet, true, forearm);
        Assert.Equal(BlockFigure.BulletMarkM, arm.Extent.X * forearm.X, 4);   // 9 cm along the arm…
        Assert.Equal(BlockFigure.BulletMarkM, arm.Extent.Z * forearm.Z, 4);   // …and 9 cm across it
    }

    /// <summary>⭐ CE-1122 (§6y HM-1, HM-3) — a man's replicated marks are his stains, each on the part of its id, the three newest fresh; past
    /// the ring's 24 the oldest are gone; a man with no marks gives none (the seeded spots by health are the layer's fallback).</summary>
    [Fact]
    public void CE1122_TheMarks_AreTheStains_OnTheirParts_TheNewestFresh()
    {
        var world = new Fdp.Core.EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Physics.BodyParts.HitMarks>();
        var man = world.CreateEntity();
        var parts = BlockFigure.Parts(StanceId.Standing, true);
        var size = new Vector3(0.35f, 0.55f, 1.8f);
        var stains = new List<Stain>();
        Assert.Equal(0, EntityBodyLayer3D.MarkStains(world, man, parts, size, stains));

        world.AddComponent(man, new Fdp.Toolkit.Physics.BodyParts.HitMarks());
        byte[] hit = { 4, 6, 1, 4, 5 };
        foreach (var p in hit) world.GetComponentRW<Fdp.Toolkit.Physics.BodyParts.HitMarks>(man).Add(p, new Vector3(0.5f, 0f, 0f), 0, Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Bullet);
        Assert.Equal(5, EntityBodyLayer3D.MarkStains(world, man, parts, size, stains));
        Assert.Equal(hit.Select(p => (int)p), stains.Select(s => s.Part));
        Assert.Equal(new[] { false, false, true, true, true }, stains.Select(s => s.Fresh));

        for (int i = 0; i < 30; i++) world.GetComponentRW<Fdp.Toolkit.Physics.BodyParts.HitMarks>(man).Add(2, new Vector3(0f, 0.5f, 0f), 0, Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Fragment);
        Assert.Equal(Fdp.Toolkit.Physics.BodyParts.HitMarks.Capacity, EntityBodyLayer3D.MarkStains(world, man, parts, size, stains));
        Assert.All(stains, s => Assert.Equal(2, s.Part));   // the five first are gone
    }

    // ── ⭐ CE-1119 (docs/DESIGN_Map_3D_Mode.md §6w) — airborne drawn ─────────────────────────────────────────────────────────────────

    private static Fdp.Toolkit.Motion.Descent Descending(Fdp.Toolkit.Motion.DescentKind kind, Fdp.Toolkit.Motion.DescentPhase phase, double since,
                                                         Fdp.Toolkit.Motion.CanopyKind canopy = Fdp.Toolkit.Motion.CanopyKind.Round)
        => new() { Kind = kind, Phase = phase, PhaseSince = since, Canopy = kind == Fdp.Toolkit.Motion.DescentKind.FastRope ? Fdp.Toolkit.Motion.CanopyKind.NoCanopy : canopy };

    /// <summary>⭐ CE-1119 (§6w AB-V2) — every descent phase has its figure in the shared part order (so it blends): the rope slide's hands
    /// above his head, the arch face down, hands up on the toggles under the canopy and down at the hips in the flare, sitting once landed;
    /// the canopy figure grows out of the tuck over the whole opening; off the rope he is a man on his feet (no figure).</summary>
    [Fact]
    public void CE1119_EveryDescentPhase_HasItsFigure_InTheSharedOrder_AndBlendsFromTheOneBefore()
    {
        const float H = 1.8f;
        int armed = BlockFigure.Parts(StanceId.Standing, true).Length;
        float Z(KitPart[] parts, int i) => parts[i].Centre.Z * H;
        const int HandL = BlockFigure.HeadIndex + 2;   // the left forearm (shared order: legs 0-3, torso 4, head 5, left arm 6-7)
        foreach (var kind in new[] { Fdp.Toolkit.Motion.DescentKind.FastRope, Fdp.Toolkit.Motion.DescentKind.StaticLine, Fdp.Toolkit.Motion.DescentKind.FreeFall })
            foreach (var phase in Enum.GetValues<Fdp.Toolkit.Motion.DescentPhase>())
            {
                var figure = DescentDraw.Figure(Descending(kind, phase, 0.0), 10.0, soldier: true, openSeconds: 4f);
                if (kind == Fdp.Toolkit.Motion.DescentKind.FastRope && phase == Fdp.Toolkit.Motion.DescentPhase.Landed) { Assert.Null(figure); continue; }
                Assert.NotNull(figure);
                Assert.Equal(armed, figure!.Length);
                Assert.NotEqual(FaceOn.None, figure[BlockFigure.HeadIndex].Face);
            }

        var rope = BlockFigure.Descending(DescentPose.RopeSlide, true);
        Assert.True(Z(rope, HandL) > Z(rope, BlockFigure.HeadIndex), "on the rope his hands are above his head");
        Assert.Equal(FaceOn.Down, BlockFigure.Descending(DescentPose.Arch, true)[BlockFigure.HeadIndex].Face);
        var under = BlockFigure.Descending(DescentPose.UnderCanopy, true);
        var flare = BlockFigure.Descending(DescentPose.Flare, true);
        Assert.True(Z(under, HandL) > Z(under, BlockFigure.HeadIndex), "under the canopy: hands up on the toggles");
        Assert.True(Z(flare, HandL) < Z(flare, BlockFigure.TorsoIndex), "the flare: toggles pulled down to the hips");
        Assert.True(Z(BlockFigure.Descending(DescentPose.SittingOut, true), BlockFigure.TorsoIndex) < 0.6f, "landed: sitting on the ground");

        // the canopy figure grows out of the tuck over the whole opening, and is complete when it has opened
        var deploying = Descending(Fdp.Toolkit.Motion.DescentKind.StaticLine, Fdp.Toolkit.Motion.DescentPhase.Deploying, 10.0);
        Assert.Equal(BlockFigure.Descending(DescentPose.Tuck, true)[HandL].Centre, DescentDraw.Figure(deploying, 10.0, true, 4f)![HandL].Centre);
        Assert.Equal(under[HandL].Centre, DescentDraw.Figure(deploying, 14.0, true, 4f)![HandL].Centre);
        float mid = Z(DescentDraw.Figure(deploying, 12.0, true, 4f)!, HandL);
        Assert.InRange(mid, Z(BlockFigure.Descending(DescentPose.Tuck, true), HandL) + 0.1f, Z(under, HandL) - 0.1f);
        // a free-fall team opens out of the arch
        var mff = Descending(Fdp.Toolkit.Motion.DescentKind.FreeFall, Fdp.Toolkit.Motion.DescentPhase.Deploying, 10.0);
        Assert.Equal(FaceOn.Down, DescentDraw.Figure(mff, 10.0, true, 4f)![BlockFigure.HeadIndex].Face);
        // a civilian has no weapon part
        Assert.Equal(armed - 1, BlockFigure.Descending(DescentPose.UnderCanopy, false).Length);
    }

    /// <summary>⭐ CE-1119 (§6w AB-V3, AB-V4) — the canopy: none on a rope, at the exit or in free fall; a thin streamer opening to a 10 m round
    /// dome 6.5 m above his shoulders with eight lines (or a three-cell 8.5 m ram-air wing with three lines a side), both with risers; on landing
    /// it falls flat on the ground behind him and the lines go.</summary>
    [Fact]
    public void CE1119_TheCanopy_OpensFromAStreamer_FliesAboveHim_AndFallsFlatBehindHim()
    {
        Assert.Equal(-1f, DescentDraw.Opening(Descending(Fdp.Toolkit.Motion.DescentKind.FastRope, Fdp.Toolkit.Motion.DescentPhase.Slide, 0), 1, 4f));
        Assert.Equal(-1f, DescentDraw.Opening(Descending(Fdp.Toolkit.Motion.DescentKind.FreeFall, Fdp.Toolkit.Motion.DescentPhase.FreeFalling, 0), 1, 4f));
        Assert.Equal(-1f, DescentDraw.Opening(Descending(Fdp.Toolkit.Motion.DescentKind.StaticLine, Fdp.Toolkit.Motion.DescentPhase.Exit, 0), 1, 4f));
        var deploying = Descending(Fdp.Toolkit.Motion.DescentKind.StaticLine, Fdp.Toolkit.Motion.DescentPhase.Deploying, 10.0);
        Assert.True(DescentDraw.Opening(deploying, 11.0, 4f) < DescentDraw.Opening(deploying, 13.0, 4f));
        Assert.Equal(1f, DescentDraw.Opening(deploying, 20.0, 4f));
        Assert.Equal(0f, DescentDraw.Collapse(Descending(Fdp.Toolkit.Motion.DescentKind.StaticLine, Fdp.Toolkit.Motion.DescentPhase.Canopy, 0), 5));
        Assert.Equal(1f, DescentDraw.Collapse(Descending(Fdp.Toolkit.Motion.DescentKind.StaticLine, Fdp.Toolkit.Motion.DescentPhase.Landed, 0), 5));

        var pieces = new List<CanopyPiece>();
        var lines = new List<(Vector3 A, Vector3 B)>();
        DescentDraw.Canopy(Fdp.Toolkit.Motion.CanopyKind.Round, 1f, 0f, pieces, lines);
        var dome = pieces[0].Local;
        Assert.Equal(2f * DescentDraw.RoundRadius, dome.M11, 2);                                  // 10 m across
        Assert.Equal(DescentDraw.ShoulderL.Z + DescentDraw.RoundLines, dome.Translation.Z, 2);     // its skirt 6.5 m above his shoulders
        Assert.Equal(2 + 8, lines.Count);                                                          // two risers, eight lines
        Assert.All(lines.Skip(2), l => Assert.True(l.B.Z > l.A.Z + 5f, "every line runs up from his risers to the skirt"));

        DescentDraw.Canopy(Fdp.Toolkit.Motion.CanopyKind.Round, 0.05f, 0f, pieces, lines);
        Assert.True(pieces[0].Local.M11 < 1f, "opening: a thin streamer");

        DescentDraw.Canopy(Fdp.Toolkit.Motion.CanopyKind.RamAir, 1f, 0f, pieces, lines);
        Assert.Equal(3, pieces.Count);
        Assert.Equal(2 + 6, lines.Count);
        var left = pieces.OrderByDescending(p => p.Local.Translation.Y).First().Local.Translation;
        Assert.True(left.Z < pieces[0].Local.Translation.Z, "the wing's tips arch down");
        Assert.True(left.Y > DescentDraw.RamAirSpan / 4f, "its tip cell out at the side");

        DescentDraw.Canopy(Fdp.Toolkit.Motion.CanopyKind.Round, 1f, 1f, pieces, lines);
        Assert.Empty(lines);
        Assert.True(pieces[0].Local.Translation.Z < 0.5f && pieces[0].Local.Translation.X < -2f, "landed: lying flat on the ground behind him");
    }

    /// <summary>⭐ CE-1119 (§6w AB-V5) — a rope hangs from its top to the surface under it, the spare coiled there; a rope too short for the
    /// hover hangs free at its length; a stick runs exactly from one end to the other.</summary>
    [Fact]
    public void CE1119_ARope_HangsFromItsTop_ToTheSurface_OrFreeAtItsLength()
    {
        var top = new Vector3(5f, 6f, 20f);
        var (bottom, spare) = DescentDraw.Rope(top, 8f, 18f);   // a 12 m hover over an 8 m roof
        Assert.Equal(new Vector3(5f, 6f, 8f), bottom);
        Assert.Equal(6f, spare, 3);
        (bottom, spare) = DescentDraw.Rope(top, 0f, 18f);       // 20 m up on an 18 m rope
        Assert.Equal(new Vector3(5f, 6f, 2f), bottom);
        Assert.Equal(0f, spare);
        var stick = DescentDraw.Stick(top, bottom, DescentDraw.RopeThickness);
        Assert.True(Vector3.Distance(Vector3.Transform(new Vector3(0.5f, 0f, 0f), stick), bottom) < 1e-3f
                    || Vector3.Distance(Vector3.Transform(new Vector3(-0.5f, 0f, 0f), stick), bottom) < 1e-3f);
        Assert.True(Vector3.Distance(Vector3.Transform(new Vector3(0.5f, 0f, 0f), stick), top) < 1e-3f
                    || Vector3.Distance(Vector3.Transform(new Vector3(-0.5f, 0f, 0f), stick), top) < 1e-3f);
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
