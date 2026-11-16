using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4624</c> (minefields MS-U4, docs/designs/minefields/DESIGN_Minefields.md §3a "the mine-clearing vehicle"; the acceptance spec is the mockup panel "The mine clearer",
/// <c>minefields-visuals.svg</c>, <c>scripts/gen-minefields-mockup.py clearer()</c>) — the mine clearer's 3-D kit: a tracked engineer carrier (tracks 7.0 m, hull 6.6 × 1.0, a superstructure
/// with no turret), a line-charge launcher at the rear top, two arms forward to a flail drum with chains that SPINS while the flail is on. The kit lies inside its 9.2 × 3.5 × 3.3 m box on
/// the ground; the drum turns about its lateral axis and nothing else moves; the motion that spins it allocates nothing; the one <see cref="FlailActivity"/> reader is a stand-in until
/// terrain's flail state lands; the real 3-D frame draws it (side, three-quarter, rear, from above) into <c>HROT_RAIL_SHOTS</c> as <c>clearer-*.png</c>.
/// ⚠ Terrain's type 116 / class <c>MineClearer</c> have not landed, so the frame rail PINS the look of a stand-in type (<c>EntityBodyLayer3D.PinLook</c>); at the re-bind the family maps through
/// <c>FamilyOfClass</c> and this rail draws the real type. ⛔ The frame test SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~MineClearerModelRail</c>.
/// 🔴 Red-proof: the drum role's rotation removed from <c>BodyTransform</c> fails the drum rails.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class MineClearerModelRail
{
    private static readonly Vector3 Size = ShapeKits.DefaultSize(VisualFamily.MineClearer);
    private const long StandInType = 91160L;   // not a TKB type: terrain's 116 has not landed (the look is pinned)

    private static Vector3 At(KitPart p, MotionPose motion, Vector3? local = null)
        => Vector3.Transform(local ?? Vector3.Zero, p.BodyTransform(Size, default, ArticulationPose.Neutral, motion));

    [Fact]
    public void TheFamily_HasItsOwnKit_Size_Colour_AndTheTrackedPickerGlyph()
    {
        Assert.Same(ShapeKits.MineClearer, ShapeKits.For(VisualFamily.MineClearer));
        Assert.Equal(new Vector3(9.2f, 3.5f, 3.3f), Size);
        Assert.False(ShapeKits.MineClearer.IsArticulated, "no turret and no gun");
        Assert.True(ShapeKits.MineClearer.Parts.Length >= 100, "a modelled machine: tracks, hull, launcher, arms, a drum with its chains");
        Assert.Equal("_tank", VisualFamilies.IconName(VisualFamily.MineClearer));
        var olive = EntityBodyLayer3D.DefaultColour(VisualFamily.MineClearer);
        Assert.NotEqual(EntityBodyLayer3D.DefaultColour(VisualFamily.Unknown), olive);
        Assert.True(olive.G > olive.B + 20 && olive.R < 140, $"an olive green, got {olive}");
        // ⭐ not yet bound to a class: terrain declares "MineClearer" first (CE2138 requires the class to exist in EntityClassTable)
        Assert.False(VisualFamilies.FamilyOfClass.ContainsKey("MineClearer"), "bound at the re-bind, with terrain's class — see the VisualFamily.MineClearer doc");
    }

    [Fact]
    public void TheKit_LiesInsideItsBox_OnTheGround_TheFlailDrumForwardOfTheHull_TheLauncherAtTheRearTop()
    {
        float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var part in ShapeKits.MineClearer.Parts)
        {
            var m = part.BodyTransform(Size);
            for (int i = 0; i < 8; i++)
            {
                var local = part.Shape == PartShape.Box
                    ? new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f)
                    : new Vector3(((i & 1) - 0.5f), (((i >> 1) & 1) - 0.5f), (i >> 2) & 1);
                var w = Vector3.Transform(local, m);
                minX = MathF.Min(minX, w.X); maxX = MathF.Max(maxX, w.X); maxY = MathF.Max(maxY, MathF.Abs(w.Y));
                minZ = MathF.Min(minZ, w.Z); maxZ = MathF.Max(maxZ, w.Z);
            }
        }
        const float eps = 0.06f;
        Assert.InRange(maxX, 4.3f, Size.X / 2f + eps);     // the flail chains' tips are the front of the machine
        Assert.InRange(minX, -Size.X / 2f - eps, -4.2f);   // the tracks' rear
        Assert.InRange(maxY, 1.6f, Size.Y / 2f + eps);     // the drum and the tracks' outer faces
        Assert.InRange(minZ, -0.03f, 0.03f);               // standing on the ground (the chains stop at it)
        Assert.InRange(maxZ, 3.0f, Size.Z + 0.01f);        // the launcher rail's top

        var parts = ShapeKits.MineClearer.Parts;
        var drum = parts.Single(p => p.Role == PartRole.FlailDrum && p.Shape == PartShape.Cylinder);
        var hull = parts.Where(p => p.Role == PartRole.Hull && p.Shape == PartShape.Box && p.Colour == ColourRole.Body).OrderByDescending(p => p.Extent.X).First();   // the 6.6 m hull
        Assert.Equal(6.6f, hull.Extent.X * Size.X, 2);
        float hullFront = At(hull, default).X + hull.Extent.X * Size.X / 2f;
        Assert.True(At(drum, default).X - drum.Extent.Z * Size.Z / 2f > hullFront - 1.2f && At(drum, default).X > hullFront, "the drum is forward of the hull");
        // the launcher: a Body box 1.6 m long, high on the REAR deck (the mockup's x −2.6, z 2.75 ⇒ here shifted to −3.6)
        Assert.Contains(parts, p => p.Shape == PartShape.Box && p.Colour == ColourRole.Body && MathF.Abs(p.Extent.X * Size.X - 1.6f) < 0.01f
                                    && At(p, default).X < -3f && At(p, default).Z is > 2.6f and < 2.9f);
        // and it carries NO turret: nothing is Turret / Gun
        Assert.DoesNotContain(parts, p => p.Role is PartRole.Turret or PartRole.Gun);
    }

    [Fact]
    public void TheFlail_TurnsTheDrumAndItsChainsAboutTheDrumsLateralAxis_AndNothingElseMoves()
    {
        var parts = ShapeKits.MineClearer.Parts;
        var drums = parts.Where(p => p.Role == PartRole.FlailDrum).ToArray();
        Assert.True(drums.Length >= 80, $"the drum and 5 rows × 8 chains with their tips: {drums.Length}");
        var pivot = drums.First().SwingPivot!.Value * Size;
        Assert.All(drums, p => Assert.Equal(drums[0].SwingPivot, p.SwingPivot));
        var turned = new MotionPose(0f, 0f, 0f, Flail: MathF.PI / 2f);
        var chain = drums.First(p => p.Shape == PartShape.Box);
        var before = At(chain, default); var after = At(chain, turned);
        // a quarter turn about the lateral (y) axis through the pivot: y and the radius are kept, the offset from the axis is rotated (+ = the top forward: +x → −z)
        Assert.Equal(before.Y, after.Y, 3);
        Assert.Equal(Vector2.Distance(new Vector2(before.X, before.Z), new Vector2(pivot.X, pivot.Z)), Vector2.Distance(new Vector2(after.X, after.Z), new Vector2(pivot.X, pivot.Z)), 3);
        var off = new Vector2(before.X - pivot.X, before.Z - pivot.Z);
        Assert.Equal(pivot.X + off.Y, after.X, 3);   // CreateRotationY(90°): x' = z, z' = −x
        Assert.Equal(pivot.Z - off.X, after.Z, 3);
        Assert.NotEqual(At(chain, default), At(chain, new MotionPose(0f, 0f, 0f, Flail: 0.5f)));
        // the drum cylinder itself turns on the spot (its centre stays), and every other part is exactly where it was
        var cylinder = drums.Single(p => p.Shape == PartShape.Cylinder);
        Assert.Equal(At(cylinder, default).X, At(cylinder, turned).X, 3);
        Assert.Equal(At(cylinder, default).Z, At(cylinder, turned).Z, 3);
        foreach (var p in parts.Where(p => p.Role != PartRole.FlailDrum))
            Assert.Equal(p.BodyTransform(Size), p.BodyTransform(Size, default, ArticulationPose.Neutral, new MotionPose(0f, 0f, 0f, Flail: 2.0f)));
        // and the flail OFF (angle 0) is the still kit, exactly
        foreach (var p in drums) Assert.Equal(p.BodyTransform(Size), p.BodyTransform(Size, default, ArticulationPose.Neutral, MotionPose.Still));
    }

    [Fact]
    public void TheMotion_TheDrumStandsWhileTheFlailIsOff_SpoolsUpAndTurnsWhileOn_WindsDownWhenItStops_AndAPausedClockFreezesIt()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        MotionPose Step(bool on) { sim += 0.05; return t.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.MineClearer, Size, sim, on); }
        for (int i = 0; i < 100; i++) Assert.Equal(0f, Step(false).Flail);   // off: it stands, never a stray angle
        float first = Step(true).Flail;
        Assert.InRange(first, 0.001f, 0.9f);                                // spooling up: it has begun, slowly
        for (int i = 0; i < 40; i++) Step(true);                            // 2 s ⇒ at speed
        float a = Step(true).Flail; float b = Step(true).Flail;
        float perSecond = Wrapped(b - a) / 0.05f;
        Assert.InRange(perSecond, MotionTracker.FlailRevsPerSecond * 2f * MathF.PI * 0.95f, MotionTracker.FlailRevsPerSecond * 2f * MathF.PI * 1.05f);   // ~4 turns a second
        float last = 0f;
        for (int i = 0; i < 60; i++) last = Step(false).Flail;              // off: spools down and STOPS
        Assert.Equal(last, Step(false).Flail);
        for (int i = 0; i < 40; i++) Step(true);
        // a paused sim clock (the frame clock still runs): the pace eases to 0 and the drum stands still
        var frozen = new MotionTracker();
        double s2 = 0, wall = 0;
        for (int i = 0; i < 100; i++) { s2 += 0.016; wall += 0.016; frozen.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.MineClearer, Size, s2, true, wall); }
        float spinning = frozen.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.MineClearer, Size, s2 + 0.016, true, wall + 0.016).Flail;
        s2 += 0.016; wall += 0.016;
        for (int i = 0; i < 400; i++) { wall += 0.016; frozen.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.MineClearer, Size, s2, true, wall); }   // 6 s of paused sim (the pace eases to 0 over ~PaceSeconds)
        float p1 = frozen.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.MineClearer, Size, s2, true, wall + 0.016).Flail;
        float p2 = frozen.Advance(e, Vector3.Zero, Quaternion.Identity, VisualFamily.MineClearer, Size, s2, true, wall + 0.032).Flail;
        Assert.NotEqual(0f, spinning);
        Assert.Equal(p1, p2);
    }

    [Fact]
    public void TheMotion_AllocatesNothing_InTheSteadyState_AndTheFlailReaderIsAStandInUntilTerrainBindsIt()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        for (int i = 0; i < 200; i++) { sim += 0.016; t.Advance(e, new Vector3(i * 0.02f, 0, 0), Quaternion.Identity, VisualFamily.MineClearer, Size, sim, true, sim); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { sim += 0.016; t.Advance(e, new Vector3(i * 0.02f, 0, 0), Quaternion.Identity, VisualFamily.MineClearer, Size, sim, i % 400 < 300, sim); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B

        using var world = new EntityRepository();
        var ent = world.CreateEntity();
        Assert.False(FlailActivity.IsOn(world, ent), "the stand-in: off until terrain's flail-active state is bound in FlailActivity.IsOn");
        FlailActivity.Override = (w, x) => ReferenceEquals(w, world) && x == ent;
        try
        {
            Assert.True(FlailActivity.IsOn(world, ent));
            Assert.False(FlailActivity.IsOn(world, world.CreateEntity()));
            before = GC.GetAllocatedBytesForCurrentThread();
            bool on = true;
            for (int i = 0; i < 2000; i++) on &= FlailActivity.IsOn(world, ent);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(on);
        }
        finally { FlailActivity.Override = null; }
    }

    private static float Wrapped(float d)
    {
        d %= MathF.PI * 2f;
        if (d < 0) d += MathF.PI * 2f;
        return d > MathF.PI * 1.5f ? d - MathF.PI * 2f : d;
    }

    // ── the real 3-D frame ────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void TheMineClearer_IsDrawn_ByTheRealFrame_Side_ThreeQuarter_Rear_FromAbove_TheDrumSpinningVsStill()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = OpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        var scale = new List<Entity>();
        Entity Put(long type, float dx, float dy, bool forScale = false)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            if (forScale) scale.Add(e);
            return e;
        }
        var clearer = Put(StandInType, 0f, 0f);
        Put(TkbEntityTypes.Tank_T72, 0f, 12f, forScale: true);
        Put(TkbEntityTypes.Infantry_Rifleman, 6f, -5f, forScale: true);
        bool flailOn = false;
        FlailActivity.Override = (w, x) => ReferenceEquals(w, world) && flailOn && x == clearer;
        try
        {
            using var frame = UiFrameHarness.Begin(1280, 800);
            LitShader.Reset();
            var canvas = new MapCanvas(input: null);
            var two = new MapCamera();
            canvas.Camera = two;
            var bodies = new EntityBodyLayer3D(() => world, () => tkb);
            bodies.PinLook(StandInType, new EntityBodyLayer3D.TypeLook(VisualFamily.MineClearer, Size, EntityBodyLayer3D.DefaultColour(VisualFamily.MineClearer), false));
            var sw = new MapViewSwitch(canvas, two);
            sw.Camera3D.GroundHeight = (x, y) => G(x, y);
            canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
            canvas.AddLayer(bodies);
            sw.Set(true, animate: false);

            float ground = G(spot.X, spot.Y);
            void Look(float lookUp, float dist, float yaw, float pitch)
                => sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + lookUp), dist, yaw, pitch);
            void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
            string Shot(string name) { Render(); var path = Path.Combine(shots, name); frame.Screenshot(path); return path; }
            const float Side = MathF.PI * 0.5f;

            Look(1.6f, 18f, Side, -0.08f);
            Shot("clearer-1-side.png");
            Assert.Equal(3, bodies.BodiesDrawn);
            Look(1.6f, 18f, Side + 0.75f, -0.30f);
            Shot("clearer-2-front34.png");
            Look(1.6f, 18f, Side - 0.75f, -0.30f);
            Shot("clearer-3-rear34.png");
            Look(1.0f, 20f, Side, -1.2f);
            Shot("clearer-4-above.png");

            // the drum: flail OFF is a still picture whatever the clock says; flail ON turns — only the drum's pixels change (the front of the machine, the right of the picture in the side view)
            Look(1.6f, 13f, Side, -0.06f);
            foreach (var e in scale) world.SetComponent(e, new SimTransform { Position = new Vector3(spot.X + 400f, spot.Y + 400f, 0f), Rotation = Quaternion.Identity });
            var empty = ScreenOf(frame, canvas, () => world.SetComponent(clearer, new SimTransform { Position = new Vector3(spot.X + 5000f, spot.Y, ground), Rotation = Quaternion.Identity }), Render);
            world.SetComponent(clearer, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
            flailOn = false;
            Clock(0.1); var off1 = ScreenOf(frame, canvas, null, Render, Path.Combine(shots, "clearer-5-flail-off.png"));
            Clock(2.0); var off2 = ScreenOf(frame, canvas, null, Render);
            Assert.Equal(0, Diff(off1, off2).Count);
            var vehicle = Diff(empty, off1);
            Assert.True(vehicle.Count > 3000, $"the clearer is on screen: {vehicle.Count} px");
            flailOn = true;
            for (int i = 0; i < 6; i++) { Clock(0.5); Render(); }   // spool up (1 s) and run
            var spun = new List<Pixels>();
            for (int i = 0; i < 4; i++) { Clock(0.043); spun.Add(ScreenOf(frame, canvas, null, Render, i < 2 ? Path.Combine(shots, $"clearer-6-flail-on-{i + 1}.png") : null)); }
            var changed = spun.Select(s => Diff(off1, s)).Where(df => df.Count > 0).ToList();
            Assert.True(changed.Count >= 3, "flail On: the drum is turning — frames differ from the still one");
            Assert.True(Diff(spun[0], spun[1]).Count > 0 || Diff(spun[1], spun[2]).Count > 0, "and it keeps turning between frames");
            // ⭐ the chains lash only at the drum: the rear half of the picture's vehicle is unchanged
            foreach (var df in changed)
                Assert.True(df.Count < vehicle.Count / 4, $"{df.Count} changed px of {vehicle.Count}: the drum, not the machine");
            // the gallery picture, to the scratchpad only
            foreach (var e in scale) world.DestroyEntity(e);
            flailOn = false;
            Look(1.5f, 15f, Side + 0.7f, -0.26f);
            Shot("clearer-7-thumb.png");
            Assert.Equal(1, bodies.BodiesDrawn);
        }
        finally { FlailActivity.Override = null; }
    }

    // ── pixels ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Pixels(Color[] Colours, int W, int H);

    private sealed record DiffStat(int Count, int MinY, int MaxY);

    private static Pixels ScreenOf(UiFrameSession frame, MapCanvas canvas, Action? before, Action render, string? save = null)
    {
        before?.Invoke();
        render();
        if (save != null) frame.Screenshot(save);
        var image = Raylib.LoadImageFromScreen();
        try
        {
            var all = new Color[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) all[y * image.Width + x] = Raylib.GetImageColor(image, x, y);
            return new Pixels(all, image.Width, image.Height);
        }
        finally { Raylib.UnloadImage(image); }
    }

    private static DiffStat Diff(Pixels a, Pixels b)
    {
        int n = 0, minY = int.MaxValue, maxY = -1;
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= 6) continue;
            n++;
            int y = i / a.W;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return new DiffStat(n, minY, maxY);
    }

    private static Vector2 OpenGround(Fdp.Toolkit.Terrain.TerrainWorld terrain, IWorldQuery query)
    {
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        for (float r = 0f; r < 400f; r += 6f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8f;
                var p = mid + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                bool open = true;
                for (float dx = -30f; dx <= 15f && open; dx += 3f)
                    for (float dy = -12f; dy <= 14f && open; dy += 3f)
                        open = MathF.Abs(query.SurfaceZ(p.X + dx, p.Y + dy, 0) - query.GroundHeightAt(p.X + dx, p.Y + dy)) < 0.2f;
                if (open) return p;
            }
        return mid;
    }
}
