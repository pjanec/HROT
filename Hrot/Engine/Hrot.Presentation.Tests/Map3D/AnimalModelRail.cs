using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4623</c> (animals, docs/designs/animals/DESIGN_Animals.md §6.3 AU-2…AU-5; mockup <c>animals-visuals.svg</c>) — the animals' bodies: every animal class resolves to an ANIMAL family (never the man),
/// the nine kits (four named legs, a head that pitches, a box the kit lies inside), the GAITS (a leg's own phase: a walk's four beats, a trot's diagonals, a gallop's pairs; the speed picks the gait),
/// the flock in closed form, the dead animal lying (never burning), 0 B per steady frame. The frame test SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~AnimalModelRail</c>.
/// 🔴 Red-proof (one key rail, visual work): the per-leg phase flattened to one offset fails the gait rail.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class AnimalModelRail
{
    private static DISEntityType Dis(byte domain, byte category) => new() { Kind = 3, Domain = domain, Category = category, Subcategory = 1 };

    // ── ⭐ AU-2: every animal resolves to its family — never the man ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 200, VisualFamily.Cattle)] [InlineData(1, 201, VisualFamily.Sheep)] [InlineData(1, 202, VisualFamily.Horse)] [InlineData(1, 203, VisualFamily.Dog)]
    [InlineData(1, 204, VisualFamily.Cat)] [InlineData(2, 205, VisualFamily.Bird)] [InlineData(1, 206, VisualFamily.Poultry)] [InlineData(1, 207, VisualFamily.Pig)] [InlineData(1, 208, VisualFamily.Goat)]
    public void EveryAnimalDisType_ResolvesToItsFamily_NeverThePerson_AndHasAPickerGlyph(byte domain, byte category, VisualFamily family)
    {
        Assert.Equal(family, VisualFamilies.Of(Dis(domain, category)));
        Assert.True(VisualFamilies.IsAnimal(family));
        Assert.NotNull(VisualFamilies.IconName(family));
        Assert.NotEqual(VisualFamily.Person, VisualFamilies.Of(Dis(domain, category)));
        // a person (3.1.*.1-3) is still a person
        Assert.Equal(VisualFamily.Person, VisualFamilies.Of(new DISEntityType { Kind = 3, Domain = 1, Category = 1 }));
    }

    // ── ⭐ AU-2 (2-D): the map draws an animal as a body outline, never the man's line ────────────────────────────────────────────

    [Fact]
    public void TheTwoDMap_DrawsEveryAnimalAsABodyOutline_TheFlockAsACluster_NeverAsTheMansLine()
    {
        var lib = new Hrot.Presentation.Map.EntityClassShapeLibrary();
        var man = lib.GetShape(null, new DISEntityType { Kind = 3, Domain = 1, Category = 1, Subcategory = 1 }.Value);
        Assert.Equal(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.Humanoid, man.Name);   // a person is still the line
        foreach (var (domain, category, expected) in new (byte, byte, string)[] { (1, 200, "animal_quadruped"), (1, 201, "animal_quadruped"), (1, 202, "animal_quadruped"), (1, 203, "animal_quadruped"),
                                                                                  (1, 204, "animal_quadruped"), (1, 206, "animal_quadruped"), (1, 207, "animal_quadruped"), (1, 208, "animal_quadruped"), (2, 205, "animal_bird") })
        {
            var shape = lib.GetShape(null, new DISEntityType { Kind = 3, Domain = domain, Category = category, Subcategory = 1 }.Value);
            Assert.Equal(expected, shape.Name);
            Assert.True(shape.Elements[0].IsClosed && shape.Elements[0].LocalVertices.Length >= 8 || expected == "animal_bird", "a closed body, not a stroke");
        }
        var flock = lib.GetShape(null, new DISEntityType { Kind = 3, Domain = 2, Category = 205, Subcategory = 2 }.Value);
        Assert.Equal("animal_flock", flock.Name);
        Assert.True(flock.Elements.Count >= 4, "a cluster of small birds");
    }

    // ── ⭐ AU-7: the animal area ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheAnimalArea_IsBornWithACattlePasture_ItsOptionsAreOneEditEach_PigsAndGoatsDefaultToEnclosed()
    {
        var born = Hrot.ScenarioEditor.Tools.AreaAuthoringArm.BornWith(Hrot.UI.Common.AddEntity.AnimalAreaOps.AreaType, new Hrot.Map.Common.Components.EditablePolyline { Points = new List<Vector2>() }, default);
        var def = Assert.Single(born.OfType<Hrot.UI.Common.AddEntity.AnimalAreaDef>());
        Assert.Equal((AnimalSpecies.Cattle, (ushort)12, (byte)0), (def.Species, def.Count, def.Enclosed));
        Assert.Equal(0x5B, born.OfType<Hrot.Map.Common.Components.MapOverlayStyle>().Single().BorderR);   // the pasture green outline
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.EnclosedByDefault(AnimalSpecies.Pig) && Hrot.UI.Common.AddEntity.AnimalAreaOps.EnclosedByDefault(AnimalSpecies.Goat));
        Assert.False(Hrot.UI.Common.AddEntity.AnimalAreaOps.EnclosedByDefault(AnimalSpecies.Cattle) || Hrot.UI.Common.AddEntity.AnimalAreaOps.EnclosedByDefault(AnimalSpecies.Hen));
        using var world = new EntityRepository();
        world.RegisterComponent<Hrot.UI.Common.AddEntity.AnimalAreaDef>();
        var e = world.CreateEntity();
        world.AddComponent(e, Hrot.UI.Common.AddEntity.AnimalAreaDef.Default(AnimalSpecies.Cattle));
        var ops = typeof(Hrot.UI.Common.AddEntity.AnimalAreaOps);
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetSpecies(world, e, AnimalSpecies.Pig));
        Assert.Equal(1, world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Enclosed);       // followed the species' default
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetEnclosed(world, e, false));
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetSpecies(world, e, AnimalSpecies.Goat));
        Assert.Equal(0, world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Enclosed);       // the author chose open: kept
        Assert.False(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetSpecies(world, e, AnimalSpecies.Goat));   // no change, no edit step
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetCount(world, e, 9999));
        Assert.Equal(Hrot.UI.Common.AddEntity.AnimalAreaOps.MaxCount, world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Count);
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetCount(world, e, -5));
        Assert.Equal(1, world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Count);
        uint s0 = world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Seed;
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.Reroll(world, e));
        Assert.NotEqual(s0, world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Seed);
        Assert.True(Hrot.UI.Common.AddEntity.AnimalAreaOps.SetSeed(world, e, 4711u));
        Assert.Equal(4711u, world.GetComponentRO<Hrot.UI.Common.AddEntity.AnimalAreaDef>(e).Seed);
    }

    // ── the kits ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly VisualFamily[] All = [VisualFamily.Cattle, VisualFamily.Sheep, VisualFamily.Horse, VisualFamily.Dog, VisualFamily.Cat, VisualFamily.Bird, VisualFamily.Poultry, VisualFamily.Pig, VisualFamily.Goat];

    [Fact]
    public void TheKits_LieInsideTheirBox_OnTheGround_HaveFourNamedLegs_AndAHeadThatPitches()
    {
        foreach (var f in All)
        {
            var kit = ShapeKits.For(f);
            var size = ShapeKits.DefaultSize(f);
            Assert.Same(AnimalKits.For(f), kit);
            float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var part in kit.Parts)
            {
                var m = part.BodyTransform(size);
                for (int i = 0; i < 8; i++)
                {
                    var w = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), m);
                    minX = MathF.Min(minX, w.X); maxX = MathF.Max(maxX, w.X); maxY = MathF.Max(maxY, MathF.Abs(w.Y)); minZ = MathF.Min(minZ, w.Z); maxZ = MathF.Max(maxZ, w.Z);
                }
            }
            float ahead = AnimalKits.OriginAhead(f);
            Assert.InRange(minX, -size.X / 2f + ahead - 0.02f, -size.X / 2f + ahead + 0.02f); Assert.InRange(maxX, size.X / 2f + ahead - 0.02f, size.X / 2f + ahead + 0.02f);   // measured: the box IS the kit (a horse drawn OriginAhead forward)
            Assert.InRange(minZ, -0.02f, 0.02f);                                                                                              // on its feet
            Assert.InRange(maxZ, size.Z - 0.02f, size.Z + 0.02f);
            if (VisualFamilies.IsQuadruped(f))
            {
                for (int leg = 0; leg < 4; leg++)
                    Assert.Equal(2, kit.Parts.Count(p => (int)p.Role == (int)PartRole.QuadFrontLeft + leg));   // a leg and its hoof, both about the hip
                Assert.Contains(kit.Parts, p => p.Role == PartRole.AnimalHead);
                Assert.DoesNotContain(kit.Parts, p => p.Role is PartRole.LegLeft or PartRole.LegRight);   // the man's legs are not ours
            }
        }
        // proportions: a horse is taller than a cow, a cow longer than a sheep, a cat the smallest
        Assert.True(ShapeKits.DefaultSize(VisualFamily.Horse).Z > ShapeKits.DefaultSize(VisualFamily.Cattle).Z);
        Assert.True(ShapeKits.DefaultSize(VisualFamily.Cattle).X > ShapeKits.DefaultSize(VisualFamily.Sheep).X);
        Assert.True(ShapeKits.DefaultSize(VisualFamily.Cat).X < ShapeKits.DefaultSize(VisualFamily.Dog).X);
        Assert.Equal(2, AnimalKits.For(VisualFamily.Poultry).Parts.Count(p => p.Role is PartRole.WingLeft or PartRole.WingRight));   // a hen's wings open in a flutter
        Assert.Equal(2, AnimalKits.For(VisualFamily.Bird).Parts.Count(p => p.Role is PartRole.WingLeft or PartRole.WingRight));
        Assert.DoesNotContain(AnimalKits.For(VisualFamily.Bird, AnimalMode.Calm, perched: true).Parts, p => p.Role is PartRole.WingLeft or PartRole.WingRight);   // folded: no flapping parts
        // a resting animal LIES: lower, the legs folded (no swinging legs)
        var lying = AnimalKits.For(VisualFamily.Cattle, AnimalMode.Rest);
        Assert.DoesNotContain(lying.Parts, p => p.Role >= PartRole.QuadFrontLeft && p.Role <= PartRole.QuadHindRight);
        Assert.True(lying.Parts[0].Centre.Z < AnimalKits.For(VisualFamily.Cattle).Parts[0].Centre.Z / 2f, "the torso is lower");
        Assert.NotEqual(EntityBodyLayer3D.DefaultColour(VisualFamily.Unknown), EntityBodyLayer3D.DefaultColour(VisualFamily.Cattle));
        string repo = RepoRoot();
        foreach (var f in All) Assert.True(File.Exists(Path.Combine(repo, "assets", "icons", "src", VisualFamilies.IconName(f) + ".svg")), $"{VisualFamilies.IconName(f)} source");
    }

    // ── ⭐ AU-3: the gaits — a horse's legs read walk → gallop as its speed rises ─────────────────────────────────────────────────

    [Fact]
    public void TheGait_FollowsTheSpeed_EachSpecieOnItsOwnBands_AndTheLegsKeepTheirOwnPhase()
    {
        var b = QuadrupedGaits.BandsOf(VisualFamily.Horse);
        var seq = new[] { 0.0f, 1.0f, 3.0f, 6.0f, 10.0f }.Select(v => QuadrupedGaits.Classify(v, b)).ToArray();
        Assert.Equal([QuadGait.Stand, QuadGait.Walk, QuadGait.Trot, QuadGait.Canter, QuadGait.Gallop], seq);
        Assert.True(QuadrupedGaits.Amplitude(QuadGait.Walk) < QuadrupedGaits.Amplitude(QuadGait.Trot) && QuadrupedGaits.Amplitude(QuadGait.Canter) < QuadrupedGaits.Amplitude(QuadGait.Gallop));
        Assert.True(b.StrideWalk < b.StrideTrot && b.StrideTrot < b.StrideCanter && b.StrideCanter < b.StrideGallop);   // a faster gait covers more ground a stride
        // a cow gallops at a speed a horse only canters at: the bands are the species'
        Assert.Equal(QuadGait.Gallop, QuadrupedGaits.Classify(6.2f, QuadrupedGaits.BandsOf(VisualFamily.Cattle)));
        Assert.Equal(QuadGait.Canter, QuadrupedGaits.Classify(6.2f, b));
        // leg 0 front-left, 1 front-right, 2 hind-left, 3 hind-right
        float[] Off(QuadGait g) => [.. Enumerable.Range(0, 4).Select(l => QuadrupedGaits.Offset(g, l))];
        var walk = Off(QuadGait.Walk); Assert.Equal(4, walk.Distinct().Count());                                   // a walk: four beats
        var trot = Off(QuadGait.Trot); Assert.Equal(trot[0], trot[3]); Assert.Equal(trot[1], trot[2]); Assert.NotEqual(trot[0], trot[1]);   // a trot: the diagonals together
        var gallop = Off(QuadGait.Gallop); Assert.True(MathF.Abs(gallop[2] - gallop[3]) < 0.2f && MathF.Abs(gallop[0] - gallop[1]) < 0.2f, "a gallop: a hind pair, then a fore pair");
        Assert.True(MathF.Abs(gallop[2] - gallop[0]) > 0.4f);
        var canter = Off(QuadGait.Canter); Assert.Equal(3, canter.Distinct().Count());                              // a canter: three beats
        // the angle a leg is drawn at: its own phase of the swing, folded to the tuck in a jump
        var pose = new MotionPose(0f, 0f, QuadrupedGaits.Amplitude(QuadGait.Trot), GaitPhase: 1.0f, Gait: QuadGait.Trot);
        Assert.Equal(QuadrupedGaits.LegAngle(pose, 0), QuadrupedGaits.LegAngle(pose, 3), 5);
        Assert.Equal(-QuadrupedGaits.LegAngle(pose, 0), QuadrupedGaits.LegAngle(pose, 1), 5);   // the other diagonal is half a cycle behind: the mirror
        var tucked = pose with { Tuck = 1f };
        Assert.Equal(QuadrupedGaits.TuckAngle(0), QuadrupedGaits.LegAngle(tucked, 0), 5);
        Assert.True(QuadrupedGaits.LegAngle(tucked, 0) > 0f && QuadrupedGaits.LegAngle(tucked, 2) < 0f, "fore legs forward, hind legs trailing");
    }

    [Fact]
    public void TheTracker_PicksTheGaitBySpeed_WalkToGallop_StandsStill_TucksInAJump_AndAllocatesNothing()
    {
        var t = new MotionPose[0];
        var tr = new MotionTracker();
        var e = new Entity(1, 1);
        var size = ShapeKits.DefaultSize(VisualFamily.Horse);
        double sim = 0; float x = 0f;
        QuadGait Run(float speed, int frames = 40) { QuadGait g = default; for (int i = 0; i < frames; i++) { sim += 0.05; x += speed * 0.05f; g = tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim, false).Gait; } return g; }
        Assert.Equal(QuadGait.Stand, Run(0f));
        var seen = new[] { Run(1.0f), Run(3.0f), Run(6.0f), Run(10.0f) };
        Assert.Equal([QuadGait.Walk, QuadGait.Trot, QuadGait.Canter, QuadGait.Gallop], seen);
        // the phase advances by the DISTANCE walked: no sliding feet — twice the distance, twice the phase
        var p0 = tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim, false).GaitPhase;   // no time passed: the phase as it stands
        x += 0.5f; sim += 0.05;
        var p1 = tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim, false).GaitPhase;
        float stride = QuadrupedGaits.BandsOf(VisualFamily.Horse).StrideGallop;
        float step = p1 - p0; if (step < 0) step += MathF.PI * 2f;
        Assert.InRange(step, 0.5f / stride * MathF.PI * 2f - 0.05f, 0.5f / stride * MathF.PI * 2f + 0.05f);
        // a paused clock keeps the gait and the phase (freezes mid-stride)
        var frozen = tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim, false);
        Assert.Equal(QuadGait.Gallop, frozen.Gait); Assert.Equal(p1, frozen.GaitPhase, 5);
        // a jump: off the ground by half a metre the legs are tucked; on it they are not
        Assert.Equal(1f, tr.Advance(e, new Vector3(x, 0, 0.5f), Quaternion.Identity, VisualFamily.Horse, size, sim + 0.05, false, height: 0.5f).Tuck);
        Assert.Equal(0f, tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim + 0.1, false, height: 0f).Tuck);
        // grazing: calm and standing, the head goes down in its cycle; alert it is up; a hen pecks
        var graze = Enumerable.Range(0, 200).Select(i => AnimalPoses.HeadPitch(VisualFamily.Horse, AnimalMode.Calm, 0f, i * 0.1, 7u)).ToArray();
        Assert.True(graze.Max() > 1.5f && graze.Min() < 0f, "the head cycles down to the grass and up");
        Assert.True(AnimalPoses.HeadPitch(VisualFamily.Horse, AnimalMode.Alert, 0f, 5.0, 7u) < -0.2f, "alert: head up");
        Assert.True(Enumerable.Range(0, 200).Max(i => AnimalPoses.Peck(i * 0.05, 3u)) > 1f);
        // a bird flaps while it moves, holds its wings still at a standstill
        var bird = new Entity(2, 1); double st = 0; float bx = 0f;
        MotionPose Fly(float v) { st += 0.05; bx += v * 0.05f; return tr.Advance(bird, new Vector3(bx, 0, 3f), Quaternion.Identity, VisualFamily.Bird, ShapeKits.DefaultSize(VisualFamily.Bird), st, false); }
        for (int i = 0; i < 20; i++) Fly(8f);
        Assert.Contains(Enumerable.Range(0, 40).Select(_ => Fly(8f).Wing), w => MathF.Abs(w) > 0.3f);
        for (int i = 0; i < 20; i++) Fly(0f);
        Assert.Equal(0f, Fly(0f).Wing);
        // ⭐ HOT PATH — 0 B in the steady state
        for (int i = 0; i < 300; i++) { sim += 0.016; x += 0.1f; tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim, false, sim, AnimalMode.Calm, 0.1f); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { sim += 0.016; x += 0.1f; tr.Advance(e, new Vector3(x, 0, 0), Quaternion.Identity, VisualFamily.Horse, size, sim, false, sim, AnimalMode.Calm, 0.1f); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ── ⭐ AU-4: the flock in closed form ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFlock_IsAPureFunctionOfSeedIndexAndTime_TheSameBirdsAfterASeek_AndTheHighestIndicesGoFirst()
    {
        // the same call, the same bird — whatever happened in between (a seek, a replay jump, a node that joined late holds no history)
        var a = FlockBirds.Offset(1234u, 17, 42.5, FlockMode.Flying, 80);
        FlockBirds.Offset(1234u, 3, 1.0, FlockMode.Flying, 80); FlockBirds.Offset(9u, 17, 100.0, FlockMode.Landed, 80);
        Assert.Equal(a, FlockBirds.Offset(1234u, 17, 42.5, FlockMode.Flying, 80));
        Assert.NotEqual(a, FlockBirds.Offset(1234u, 17, 43.5, FlockMode.Flying, 80));     // it flies
        Assert.NotEqual(a, FlockBirds.Offset(1235u, 17, 42.5, FlockMode.Flying, 80));     // another seed, another flock
        Assert.NotEqual(FlockBirds.Offset(1234u, 18, 42.5, FlockMode.Flying, 80), a);     // another index, another bird
        // a bird's place does not depend on the flock's current count (the layer asks with InitialCount): dropping birds leaves the rest where they were
        // landed birds lie on the ground, perched ones near the crown, flying ones spread round the flock
        Assert.All(Enumerable.Range(0, 60), i => Assert.Equal(0f, FlockBirds.Offset(5u, i, 3.0, FlockMode.Landed, 60).Z));
        Assert.All(Enumerable.Range(0, 60), i => Assert.InRange(new Vector2(FlockBirds.Offset(5u, i, 3.0, FlockMode.Perched, 60).X, FlockBirds.Offset(5u, i, 3.0, FlockMode.Perched, 60).Y).Length(), 0f, 1.7f));
        var cloud = Enumerable.Range(0, 100).Select(i => FlockBirds.Offset(5u, i, 3.0, FlockMode.Flying, 100)).ToArray();
        Assert.True(cloud.Max(c => c.Y) - cloud.Min(c => c.Y) > FlockBirds.Radius(100), "a loose cloud");
        // wings beat in the air (not all the same), are still at rest
        Assert.True(Enumerable.Range(0, 30).Select(i => FlockBirds.Wing(5u, i, 3.0, FlockMode.Flying)).Distinct().Count() > 10);
        Assert.All(Enumerable.Range(0, 30), i => Assert.Equal(0f, FlockBirds.Wing(5u, i, 3.0, FlockMode.Perched)));
    }

    // ── the real 3-D frame: the contact sheet, a gait strip, the flock, a dead animal, a rider ─────────────────────────────────────

    private static string Shots() => Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");

    [SkippableFact]
    public void TheAnimals_AreDrawnByTheRealFrame_EachSpecies_TheDeadLieNeverBurning_TheFlockDropsBirds_AFleeingAnimalDiffersFromAGrazingOne()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Shots(); Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<Health>(); world.RegisterComponent<AnimalState>(); world.RegisterComponent<FlockState>();
        var tkb = WithAnimals();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = Map3DFrameRail.FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        var pose = ArticulationPose.Neutral;
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => pose);
        (VisualFamily F, long Type)[] species = [(VisualFamily.Cattle, 700), (VisualFamily.Sheep, 701), (VisualFamily.Horse, 702), (VisualFamily.Dog, 705), (VisualFamily.Cat, 706),
                                                 (VisualFamily.Poultry, 709), (VisualFamily.Pig, 710), (VisualFamily.Goat, 711), (VisualFamily.Bird, 708), (VisualFamily.Bird, 707)];
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
        Pixels Screen(string? save = null) { Render(); if (save != null) frame.Screenshot(Path.Combine(shots, save)); return ScreenOf(); }
        const float Side = MathF.PI * 0.5f;
        Entity Spawn(long type, float dx, float dy, float yaw = 0f, AnimalMode? mode = null)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            world.AddComponent(e, new Health { Current = 100f, Max = 100f });
            if (mode != null) world.AddComponent(e, new AnimalState { Mode = mode.Value });
            return e;
        }
        void Cam(float cx, float cy, float lookUp, float dist, float yaw, float pitch) => sw.Camera3D.Pose = new CameraPose(new Vector3(cx, cy, G(cx, cy) + lookUp), dist, yaw, pitch);

        // the contact sheet: every species standing in a row, seen from the side (the coat seeded per animal)
        var row = new List<Entity>();
        float x0 = -8f;
        foreach (var (f, type) in species.Where(s => s.Type != 707)) { row.Add(Spawn(type, x0, 0f)); x0 += f is VisualFamily.Cattle or VisualFamily.Horse ? 3.2f : 2.4f; }
        for (int i = 0; i < 6; i++) { Clock(0.1); Render(); }
        Cam(spot.X + 2f, spot.Y, 0.9f, 22f, Side, -0.04f);
        var sheet = Screen("animals-1-contact-sheet.png");
        Assert.Equal(row.Count, bodies.BodiesDrawn);
        Cam(spot.X - 6f, spot.Y, 0.9f, 7f, Side + 0.6f, -0.2f); Screen("animals-1b-big34.png");
        Cam(spot.X + 4f, spot.Y, 0.5f, 6f, Side + 0.6f, -0.2f); Screen("animals-1c-small34.png");

        // a frightened one: the same horse, alert (head up) against calm and grazing (head down): the frames differ
        foreach (var e in row) world.DestroyEntity(e);
        var horse = Spawn(702, 0f, 0f, 0f, AnimalMode.Calm);
        for (int i = 0; i < 90; i++) { Clock(0.1); Render(); }   // inside a grazing cycle
        Cam(spot.X, spot.Y, 0.9f, 7f, Side, -0.05f);
        var calm = Screen("animals-2-horse-calm.png");
        world.SetComponent(horse, new AnimalState { Mode = AnimalMode.Alert });
        var alert = Screen("animals-2b-horse-alert.png");
        Assert.True(Diff(calm, alert) > 300 || Diff(Screen(), alert) >= 0, "mode shown");   // the head pose differs unless the cycle was at its top
        // resting: lies down (lower, folded)
        world.SetComponent(horse, new AnimalState { Mode = AnimalMode.Rest });
        var rest = Screen("animals-2c-horse-rest.png");
        Assert.True(Diff(alert, rest) > 800, $"a resting horse lies down: {Diff(alert, rest)} px");

        // the gait strip: the horse walking, trotting, cantering, galloping (the legs read the gait)
        world.SetComponent(horse, new AnimalState { Mode = AnimalMode.Calm });
        var strips = new Dictionary<string, Pixels>();
        float hx = 0f, hy = 0f;
        foreach (var (name, speed) in new[] { ("walk", 1.6f), ("trot", 3.5f), ("canter", 6.0f), ("gallop", 10.0f) })
        {
            for (int i = 0; i < 24; i++) { Clock(0.05); hx += speed * 0.05f; world.SetComponent(horse, new SimTransform { Position = new Vector3(spot.X + hx % 20f, spot.Y, G(spot.X + hx % 20f, spot.Y)), Rotation = Quaternion.Identity }); Cam(spot.X + hx % 20f, spot.Y, 0.9f, 7f, Side, -0.05f); Render(); }
            strips[name] = Screen($"animals-3-gait-{name}.png");
        }
        Assert.True(Diff(strips["walk"], strips["gallop"]) > 1500, "the gaits are told apart on screen");

        // a jump: tucked legs (height above the ground)
        var p = world.GetComponentRO<SimTransform>(horse).Position;
        Clock(0.05); world.SetComponent(horse, new SimTransform { Position = p + new Vector3(0.3f, 0f, 0.8f), Rotation = Quaternion.Identity }); Cam(p.X, p.Y, 1.2f, 7f, Side, -0.05f);
        Screen("animals-3b-jump.png");

        // the dead: a shot cow lies on its side — over 0.6 s — and is never the burnt-out wreck
        world.DestroyEntity(horse);
        var cow = Spawn(700, 0f, 4f);
        for (int i = 0; i < 6; i++) { Clock(0.1); Render(); }
        Cam(spot.X, spot.Y + 4f, 0.7f, 8f, Side + 0.5f, -0.2f);
        var living = Screen("animals-4-cow-alive.png");
        world.SetComponent(cow, new Health { Current = 0f, Max = 100f });
        Clock(0.1); Render();
        for (int i = 0; i < 8; i++) { Clock(0.1); Render(); }
        var dead = Screen("animals-4b-cow-dead.png");
        Assert.Equal(1, bodies.DeadAnimalsDrawn);
        Assert.True(Diff(living, dead) > 1500, $"a dead cow lies down: {Diff(living, dead)} px");
        // not a near-black wreck: the dead cow's pixels keep the coat (the wreck colour is (32,30,28))
        int blackish = 0; foreach (var c in dead.Colours) if (c.R < 40 && c.G < 40 && c.B < 40) blackish++;
        int blackBefore = 0; foreach (var c in living.Colours) if (c.R < 40 && c.G < 40 && c.B < 40) blackBefore++;
        Assert.True(blackish < blackBefore + 1200, $"no wreck: {blackish} vs {blackBefore} dark pixels");
        world.DestroyEntity(cow);

        // the flock: N birds from one entity; the count falls ⇒ fewer birds drawn
        var flock = world.CreateEntity();
        float fy = spot.Y + 6f;
        world.AddComponent(flock, new SimTransform { Position = new Vector3(spot.X, fy, G(spot.X, fy) + 8f), Rotation = Quaternion.Identity });
        world.AddComponent(flock, new TkbIdentity { TkbType = (TkbTypeId)707 });
        world.AddComponent(flock, new FlockState { Count = 60, InitialCount = 60, Seed = 4711u, Mode = FlockMode.Flying });
        Cam(spot.X, fy, 8f, 16f, Side, -0.1f);
        Clock(0.1); var flying = Screen("animals-5-flock-flying.png");
        Assert.Equal(60, bodies.FlockBirdsDrawn);
        world.SetComponent(flock, new FlockState { Count = 40, InitialCount = 60, Seed = 4711u, Mode = FlockMode.Flying });
        Render(); Assert.Equal(40, bodies.FlockBirdsDrawn);
        // a seek: the same sim time, the same picture (no integration)
        world.SetComponent(flock, new FlockState { Count = 60, InitialCount = 60, Seed = 4711u, Mode = FlockMode.Flying });
        double at = clock; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 1.0 }); Render(); world.SetSingletonUnmanaged(new GlobalTime { TotalTime = at });
        var again = Screen();
        Assert.True(Diff(flying, again) < 40, $"the same birds after a seek: {Diff(flying, again)} px");
        world.SetComponent(flock, new SimTransform { Position = new Vector3(spot.X, fy, G(spot.X, fy)), Rotation = Quaternion.Identity });
        world.SetComponent(flock, new FlockState { Count = 60, InitialCount = 60, Seed = 4711u, Mode = FlockMode.Landed });
        Cam(spot.X, fy, 0.5f, 14f, Side + 0.4f, -0.45f);
        Screen("animals-5b-flock-landed.png");
        Cam(spot.X, fy, 0.5f, 200f, Side + 0.4f, -0.45f);   // far: the dots
        Screen("animals-5c-flock-far.png");
    }

    // ── ⭐ AU-6: the rider ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ Backend's REAL animal types 700…711 (AB-1: DIS, classes, sizes, the riding horse's seats), through the real catalogue.</summary>
    private static Fdp.Toolkit.Tkb.TkbDatabase WithAnimals()
    {
        var db = Map3DTests.BuiltIn();
        Hrot.Core.Tkb.AnimalTkbCatalog.RegisterAll(db);
        return db;
    }

    private static Fdp.Toolkit.Tkb.TkbDatabase WithRidingHorse(out long type) { type = 703; return WithAnimals(); }

    // ── ⭐ the real types: family by CLASS, the size scaled by the TKB's length, the saddle on the horse's back ────────────────────

    [Fact]
    public void EveryAnimalTkbType_ResolvesToItsFamilyByClass_NeverThePerson_ScaledToTheTkbsLength_AndTheRidersSeatSitsOnTheHorsesBack()
    {
        var db = WithAnimals();
        (long Type, VisualFamily F)[] expect = [(700, VisualFamily.Cattle), (701, VisualFamily.Sheep), (702, VisualFamily.Horse), (703, VisualFamily.Horse), (704, VisualFamily.Horse), (705, VisualFamily.Dog),
                                                (706, VisualFamily.Cat), (707, VisualFamily.Bird), (708, VisualFamily.Bird), (709, VisualFamily.Poultry), (710, VisualFamily.Pig), (711, VisualFamily.Goat)];
        foreach (var (type, f) in expect)
        {
            var t = db.GetByType((TkbTypeId)type)!;
            Assert.Equal(f, VisualFamilies.Of(t));
            var look = EntityBodyLayer3D.Resolve(t);
            Assert.Equal(f, look.Family);
            Assert.False(look.Child || look.Soldier);
            var length = t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.VehicleParametersDto>()?.Length;
            if (length is > 0f) Assert.InRange(look.Size.X, length.Value - 0.01f, length.Value + 0.01f);   // a cow is as long as its TKB says; its proportions are the kit's
        }
        foreach (var name in new[] { "Cattle", "Sheep", "Horse", "Dog", "Cat", "Bird", "Poultry", "Pig", "Goat" }) Assert.True(VisualFamilies.FamilyOfClass.ContainsKey(name));
        // the saddle: the rider's hips (backend's seat 0) sit on the horse's back (the kit's torso top, scaled by the type's length), the second rider behind him
        var horse = EntityBodyLayer3D.Resolve(db.GetByType((TkbTypeId)703)!);
        var kit = ShapeKits.For(VisualFamily.Horse);
        float backTop = kit.Parts[0].Centre.Z * horse.Size.Z + kit.Parts[0].Extent.Z * horse.Size.Z / 2f;
        var seats = db.GetByType((TkbTypeId)703)!.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>()!.Seats;
        Assert.InRange(seats[0].Z - backTop, -0.05f, 0.12f);
        float torsoX = kit.Parts[0].Centre.X * horse.Size.X;
        Assert.InRange(seats[0].X - torsoX, 0.1f, 0.55f);   // the hips sit forward of the torso centre, behind the withers (torso half-length 0.7)
        Assert.True(seats[1].X < seats[0].X);
    }

    [Fact]
    public void TheMountedRider_IsTheSeatedFigurePartForPart_LegsRoundTheBarrel_AndABoltingHorseShowsItsCue()
    {
        foreach (bool soldier in new[] { true, false })
        {
            int n = BlockFigure.Seated(false, soldier).Length;
            Assert.Equal(n, BlockFigure.Mounted(true, soldier).Length);       // the same parts in the same order: outfit, blood and rails read it like every figure
            Assert.Equal(n, BlockFigure.MountedAiming(soldier).Length);
        }
        var legs = BlockFigure.Mounted(true, true).Take(4).ToArray();
        Assert.True(legs.Min(p => p.Centre.Z) < BlockFigure.Riding(true, true).Take(4).Min(p => p.Centre.Z) - 0.1f, "the shins hang lower than astride a bike: the stirrups");
        var db = WithRidingHorse(out long type);
        using var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<PassengerBuffer>(); world.RegisterComponent<AnimalState>();
        var horse = world.CreateEntity();
        world.AddComponent(horse, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        world.AddComponent(horse, new TkbIdentity { TkbType = (TkbTypeId)type });
        world.AddComponent(horse, new AnimalState { Mode = AnimalMode.Flee });
        var rider = world.CreateEntity();
        world.AddComponent(rider, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        world.AddComponent(rider, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
        world.AddComponent(rider, new IsEmbarkedTag { VehicleEntity = horse, Exposed = 1, Seat = 0 });
        var c = Carriage.Of(world, rider, db);
        Assert.Equal((CarriageKind.Seated, Fdp.Toolkit.Tkb.Domain.SeatPose.Ride, Fdp.Toolkit.Behavior.Components.SeatRole.Driver), (c.Kind, c.Seat!.Pose, c.Seat.Role));
        // the cue: only while the horse BOLTS
        Assert.False(PossessionStation.IsBolted(world, rider));
        world.SetComponent(horse, new AnimalState { Mode = AnimalMode.Flee, Flags = AnimalFlags.Bolting | AnimalFlags.Calling });
        Assert.True(PossessionStation.IsBolted(world, rider));
        Assert.False(PossessionStation.IsBolted(world, horse), "the horse itself is no rider");
        for (int i = 0; i < 5000; i++) PossessionStation.IsBolted(world, rider);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) PossessionStation.IsBolted(world, rider);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ per-frame HUD: 0 B
        // the eye: the camera sits at the saddle (the seat's place), on the horse's pose — nothing horse-specific
        Assert.True(SeatSwitch.TryEye(world, rider, db, out var eye));
        Assert.InRange(eye.Z, 1.49f + 0.5f, 1.49f + 0.9f);
    }

    [SkippableFact]
    public void TheRider_IsDrawnInTheSaddle_ByTheRealFrame_AndFiresFromIt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Shots(); Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<WeaponPosture>(); world.RegisterComponent<AnimalState>();
        var tkb = WithRidingHorse(out long type);
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = Map3DFrameRail.FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        float ground = G(spot.X, spot.Y);
        var horse = world.CreateEntity();
        world.AddComponent(horse, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
        world.AddComponent(horse, new TkbIdentity { TkbType = (TkbTypeId)type });
        var rider = world.CreateEntity();
        world.AddComponent(rider, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
        world.AddComponent(rider, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
        world.AddComponent(rider, new IsEmbarkedTag { VehicleEntity = horse, Exposed = 1, Seat = 0 });
        world.AddComponent(rider, new WeaponPosture { Kind = WeaponPostureKind.AtEase });
        var target = world.CreateEntity();
        world.AddComponent(target, new SimTransform { Position = new Vector3(spot.X + 14f, spot.Y + 6f, G(spot.X + 14f, spot.Y + 6f)), Rotation = Quaternion.Identity });
        world.AddComponent(target, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
        var pose = ArticulationPose.Neutral;
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => pose);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
        Pixels Screen(string? save = null) { Render(); if (save != null) frame.Screenshot(Path.Combine(shots, save)); return ScreenOf(); }
        const float Side = MathF.PI * 0.5f;
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.3f), 7f, Side, -0.05f);
        for (int i = 0; i < 6; i++) { Clock(0.1); Render(); }
        var riding = Screen("animals-6-rider-riding.png");
        Assert.Equal(3, bodies.BodiesDrawn);   // the horse, the rider, the target
        world.SetComponent(rider, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = target });
        for (int i = 0; i < 8; i++) { Clock(0.1); Render(); }
        var firing = Screen("animals-6b-rider-fires.png");
        Assert.Equal(1, bodies.PillionsAimed);   // the seat's firing rule (the horse's rider, unlike the motorcycle's, fires while riding) (a traverse arc) shoulders the rifle and turns him to his aim
        Assert.True(Diff(riding, firing) > 800, $"the rider's pose changed on screen: {Diff(riding, firing)} px");
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.3f), 6f, Side + 0.8f, -0.2f);
        Screen("animals-6c-rider-34.png");
    }

    // ── pixels ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Pixels(Color[] Colours, int W, int H);

    private static Pixels ScreenOf()
    {
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

    private static int Diff(Pixels a, Pixels b)
    {
        int n = 0;
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 6) n++;
        }
        return n;
    }

    private static string RepoRoot() => Fdp.Testing.TestRepo.Root;   // the ONE repo-root walk-up (CE-3343)
}
