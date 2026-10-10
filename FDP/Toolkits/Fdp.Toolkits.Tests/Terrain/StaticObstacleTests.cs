#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3136</c> P-7a (D11, O1/O3/O4; R-242, R-243) — a static obstacle is TERRAIN: once its box prism is in the world, the ONE
/// terrain readers — bullets (<see cref="TerrainPenetration"/>), sight (<see cref="TerrainWorld.SegmentBlocked"/>), cover
/// (<see cref="TerrainCoverProvider"/>) and the navmesh — all respect it with no per-system code, and the entity's own collider is out
/// of the collider paths so nothing counts twice. 📄 docs/DESIGN_Peek_And_Fire.md §9.
/// </summary>
public sealed class StaticObstacleTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public StaticObstacleTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    // A 60×60 m flat world (the navmesh rail needs ground); the obstacle stands in the middle of it.
    private const string Flat = """
        {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[]}
        """;

    private static TerrainMaterial M(string name) => TerrainMaterialLibrary.Shared.Get(name, "rail");

    private static TerrainWorld WithBox(string material, float length = 4.5f, float width = 1.8f, float height = 1.5f, float yaw = 0f)
        => TerrainObstacles.With(TerrainWorldParser.Parse(Flat),
            new[] { TerrainObstacles.PrismOf(new Vector3(30, 30, 0), yaw, new ObstacleShape { Length = length, Width = width, Height = height }, M(material), "obstacle:1") });

    [Fact]
    public void P7a_R1_ThePrism_IsTheBoxAlongTheHeading_OfItsMaterial_AndTheBaseWorldIsUntouched()
    {
        var prism = TerrainObstacles.PrismOf(new Vector3(10, 20, 0.5f), MathF.PI / 2,
            new ObstacleShape { Length = 4f, Width = 2f, Height = 1.5f }, M("car-body"));
        Assert.Equal(4, prism.Footprint.Length);
        Assert.Equal(new Vector2(9f, 18f), prism.Min, new V2Eq(1e-4f));   // turned 90°: the 4 m length runs north
        Assert.Equal(new Vector2(11f, 22f), prism.Max, new V2Eq(1e-4f));
        Assert.Equal(0.5f, prism.BaseZ);
        Assert.Equal(2.0f, prism.TopZ, 4);
        Assert.Equal("car-body", prism.Material!.Name);

        var baseWorld = TerrainWorldParser.Parse(Flat);
        var withIt = TerrainObstacles.With(baseWorld, new[] { prism });
        Assert.Empty(baseWorld.Prisms);
        Assert.Single(withIt.Prisms);
        Assert.Same(baseWorld.Materials, withIt.Materials);
    }

    [Fact]
    public void P7a_R2_ARoundCrossesAnObstacle_ByTheWallRule_ConcreteStops_ACrateLetsItThrough()
    {
        // Across the 1 m width of a concrete block: 1 500 mm RHA — a rifle round stops.
        var concrete = WithBox("concrete", length: 2f, width: 1f, height: 1f);
        float damage = 1f, pen = 100f;
        Assert.True(TerrainPenetration.Carry(new TerrainWorldQuery(concrete), new Vector3(30, 20, 0.5f), new Vector3(30, 40, 0.5f), ref damage, ref pen, out float stopT));
        Assert.InRange(stopT, 0.45f, 0.5f);

        // A crate (fence-wood, 1 m): 60 mm — 100 mm goes through with its damage, 40 mm left.
        var crate = WithBox("fence-wood", length: 1.2f, width: 1f, height: 1f);
        damage = 1f; pen = 100f;
        var log = new List<ShotCrossing>();
        Assert.False(TerrainPenetration.Carry(new TerrainWorldQuery(crate), new Vector3(30, 20, 0.5f), new Vector3(30, 40, 0.5f), ref damage, ref pen, out _, log));
        Assert.Equal(1f, damage, 3);
        Assert.InRange(pen, 39f, 41f);

        // Over the top of the block: nothing crossed.
        damage = 1f; pen = 100f;
        Assert.False(TerrainPenetration.Carry(new TerrainWorldQuery(concrete), new Vector3(30, 20, 1.6f), new Vector3(30, 40, 1.6f), ref damage, ref pen, out _));
        Assert.Equal(1f, damage, 3);
    }

    [Fact]
    public void P7a_R3_SightIsBlockedByTheCarBody_BelowItsRoof_NotAbove()
    {
        var car = WithBox("car-body");
        Assert.True(car.SegmentBlocked(new Vector3(30, 20, 1.0f), new Vector3(30, 40, 1.0f)));
        Assert.False(car.SegmentBlocked(new Vector3(30, 20, 1.7f), new Vector3(30, 40, 1.7f)));
    }

    [Fact]
    public void P7a_R4_TheCoverDatabase_HasPointsAroundTheObstacle_AtItsStance()
    {
        var cover = TerrainCoverProvider.Build(WithBox("car-body"));
        Span<CoverPoint> found = stackalloc CoverPoint[64];
        int n = cover.GetCoverPointsInRadius(new Vector2(30, 30), 5f, found);
        Assert.True(n >= 4, $"expected cover points round the car, got {n}");
        for (int i = 0; i < n; i++)
            Assert.Equal(2, found[i].StanceHeight);   // the cover rule (EQS §19.5): ≥ 1.5 m hides a standing man
    }

    [Fact]
    public void P7a_R5_ThePathGoesRoundTheObstacle()
    {
        var nav = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry }.Build(WithBox("concrete", length: 12f, width: 2f, height: 1.5f, yaw: MathF.PI / 2))
                  ?? throw new InvalidOperationException("bake produced no mesh");
        Assert.True(nav.IsWalkable(new Vector3(20, 30, 0)));
        // The footprint is not ground: the nearest mesh there is the wall's 1.5 m top (a roof, like a building's — CE-3133 rail),
        // which is not connected to the ground (above the climb height), so the path below must go round.
        Assert.True(nav.ProjectToNavmesh(new Vector3(30, 30, 0), out var snapped));
        Assert.True(snapped.Z > 1.2f, $"the obstacle footprint must not be ground (snapped to {snapped})");

        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = nav.PlanPath(new Vector3(20, 30, 0), new Vector3(40, 30, 0), wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 3, $"expected a detour, got {n} waypoint(s)");
        for (int i = 0; i < n; i++)
        {
            var p = wps[i].Position;
            Assert.False(p.X > 29.2f && p.X < 30.8f && p.Y > 24.2f && p.Y < 35.8f, $"waypoint {i} {p} is inside the obstacle");
        }
    }

    [Fact]
    public void P7a_R6_TheTranslator_StampsMarkerShapeAndCollider_AndAnInstanceSizeWins()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<StaticObstacle>();
        repo.RegisterComponent<ObstacleShape>();
        repo.RegisterComponent<PhysicsCollider>();
        var template = new TkbTemplate("Sandbag wall", 8807);
        template.AddDescriptor(new StaticObstacleDto { Length = 3f, Width = 0.6f, Height = 1f, Material = "sandbags" });

        var plain = repo.CreateEntity();
        new ObstacleTkbTranslator().Inject(repo, plain, template);
        Assert.Equal("sandbags", repo.GetComponent<StaticObstacle>(plain).Material.ToString());
        Assert.Equal(3f, repo.GetComponent<ObstacleShape>(plain).Length);
        Assert.Equal(1f, repo.GetComponent<PhysicsCollider>(plain).Height);

        var longer = repo.CreateEntity();
        repo.AddComponent(longer, new ObstacleShape { Length = 9f, Width = 0.6f, Height = 1f });   // a scenario's per-instance size
        new ObstacleTkbTranslator().Inject(repo, longer, template);
        Assert.Equal(9f, repo.GetComponent<ObstacleShape>(longer).Length);
        Assert.True(repo.GetComponent<PhysicsCollider>(longer).Radius > 4.5f);
    }

    [Fact]
    public void P7a_R7_TheSightOccluder_SkipsAStaticObstacle_ItIsTerrain()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<PhysicsCollider>();
        repo.RegisterComponent<StaticObstacle>();
        var car = repo.CreateEntity();
        repo.AddComponent(car, new SimTransform { Position = new Vector3(30, 30, 0) });
        repo.AddComponent(car, new PhysicsCollider { Radius = 2.4f, Height = 1.5f, CollisionLayer = 1 });
        var truck = repo.CreateEntity();
        repo.AddComponent(truck, new SimTransform { Position = new Vector3(10, 10, 0) });
        repo.AddComponent(truck, new PhysicsCollider { Radius = 2.4f, Height = 3f, CollisionLayer = 1 });

        var occ = new ColliderOcclusion();
        occ.Build(repo, (v, e) => v.GetComponentRO<PhysicsCollider>(e).Radius, (v, e) => v.GetComponentRO<PhysicsCollider>(e).Height);
        Assert.Equal(2, occ.Count);

        repo.AddComponent(car, new StaticObstacle { Material = new FixedString32("car-body") });
        occ.Build(repo, (v, e) => v.GetComponentRO<PhysicsCollider>(e).Radius, (v, e) => v.GetComponentRO<PhysicsCollider>(e).Height);
        Assert.Equal(1, occ.Count);   // only the (moving) truck — the parked car is terrain now
    }

    [Fact]
    public void P7a_R8_TheLifecycle_HoldsAnObstacleType_UntilTheTerrainParticipantAcks()
    {
        var tkb = new TkbDatabase();
        var obstacle = new TkbTemplate("Car", 8806);
        obstacle.AddDescriptor(new StaticObstacleDto { Length = 4.5f, Width = 1.8f, Height = 1.5f, Material = "car-body" });
        tkb.Register(obstacle);
        tkb.Register(new TkbTemplate("Rifleman", 2001));

        var elm = new EntityLifecycleModule(tkb, Array.Empty<int>());
        elm.RegisterTemplateRequirement(t => t.GetDescriptor<StaticObstacleDto>() != null, TerrainObstacles.LifecycleModuleId);
        var cmd = new EntityCommandBuffer();

        var car = new Entity(10, 1);
        var man = new Entity(11, 1);
        elm.BeginConstruction(car, 8806, 1, cmd);
        elm.BeginConstruction(man, 2001, 1, cmd);
        elm.DrainInstantComplete(cmd, 2);
        Assert.Equal(1, elm.GetStatistics().pending);   // the man needs nobody; the car waits for the terrain

        elm.ProcessConstructionAck(new ConstructionAck { Entity = car, ModuleId = TerrainObstacles.LifecycleModuleId, Success = true }, 3, cmd);
        Assert.Equal(0, elm.GetStatistics().pending);
    }

    /// <summary>
    /// ⭐ P7a_R9 — the bake measured on the real <c>test-town</c> (§9.2 O2's risk): an obstacle bake through the per-tile cache re-bakes
    /// ONLY the tiles the obstacles touch; the world + cover + navmesh time is printed (the design records it). 🔴 Red-proof: give the
    /// second bake a fresh <see cref="NavTileCache"/> and <c>Baked</c> equals every tile.
    /// </summary>
    [Fact]
    public void P7a_R9_OnTestTown_AnObstacleBakeRebakesOnlyTheTouchedTiles()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Hrot", "Subsystems"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var path = System.IO.Path.Combine(dir!.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain", "test-town", "test-town.world.geojson");
        var town = TerrainWorldParser.Parse(System.IO.File.ReadAllText(path), "test-town");

        var factory = new RecastNavmeshFactory { TileCache = new NavTileCache() };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.NotNull(factory.Build(town));
        long baseMs = sw.ElapsedMilliseconds;
        var baseStats = factory.LastStats;

        var obstacles = new[]
        {
            TerrainObstacles.PrismOf(new Vector3(town.BoundsMin.X + 30, town.BoundsMin.Y + 30, 0), 0f, new ObstacleShape { Length = 4.5f, Width = 1.8f, Height = 1.5f }, M("car-body"), "obstacle:1"),
            TerrainObstacles.PrismOf(new Vector3(town.BoundsMin.X + 36, town.BoundsMin.Y + 30, 0), 0.5f, new ObstacleShape { Length = 3f, Width = 0.6f, Height = 1f }, M("sandbags"), "obstacle:2"),
            TerrainObstacles.PrismOf(new Vector3(town.BoundsMin.X + 30, town.BoundsMin.Y + 36, 0), 0f, new ObstacleShape { Length = 2f, Width = 1f, Height = 1f }, M("concrete"), "obstacle:3"),
        };
        sw.Restart();
        var withThem = TerrainObstacles.With(town, obstacles);
        long worldMs = sw.ElapsedMilliseconds;
        Assert.NotNull(factory.Build(withThem));
        long navMs = sw.ElapsedMilliseconds - worldMs;
        var st = factory.LastStats;
        TerrainCoverProvider.Build(withThem);
        long totalMs = sw.ElapsedMilliseconds;

        _out.WriteLine($"[P7a_R9] test-town: base bake {baseMs} ms ({baseStats.Tiles} tiles, {baseStats.Baked} baked); "
            + $"3 obstacles: world {worldMs} ms, navmesh {navMs} ms ({st.Baked} baked, {st.FromCache} from cache), total with cover {totalMs} ms");
        Assert.Equal(baseStats.Tiles, st.Tiles);
        Assert.InRange(st.Baked, 1, 8);                 // the touched tiles (per layer), not the town
        Assert.True(st.FromCache >= st.Tiles - 8, $"expected the rest from the cache, got {st.FromCache}/{st.Tiles}");
    }

    // ── CE-3142 (P-7a O5) — a vehicle is cover read live ───────────────────────────────────────────────────────────

    private static EntityRepository VehicleWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<SimVelocity>();
        repo.RegisterComponent<PhysicsCollider>();
        repo.RegisterComponent<StaticObstacle>();
        repo.RegisterComponent<global::CarKinem.Core.VehicleParams>();
        repo.RegisterComponent<global::CarKinem.Core.VehicleState>();
        repo.RegisterManagedComponent<TerrainWorld>();
        repo.RegisterManagedComponent<ICoverProvider>();
        var flat = TerrainWorldParser.Parse(Flat);
        repo.SetSingletonManaged(flat);
        repo.SetSingletonManaged<ICoverProvider>(TerrainCoverProvider.Build(flat));
        return repo;
    }

    private static Entity Vehicle(EntityRepository repo, float x, float y, float speed = 0f,
        global::CarKinem.Core.VehicleClass cls = global::CarKinem.Core.VehicleClass.PersonalCar)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
        repo.AddComponent(e, new PhysicsCollider { Radius = 2.4f, Height = 1.5f, CollisionLayer = 1 });
        repo.AddComponent(e, new global::CarKinem.Core.VehicleParams { Class = cls, Length = 4.5f, Width = 1.8f });
        repo.AddComponent(e, new global::CarKinem.Core.VehicleState { Speed = speed });
        return e;
    }

    private static Entity Point(EntityRepository repo, float x, float y)
    {
        var e = repo.CreateEntity();
        repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0) });
        return e;
    }

    /// <summary>
    /// ⭐⭐ <c>P7a_R10</c> (O5 ①) — the AI's sight sees a vehicle's BOX: a car hides a man below its roof and not above it, a viewer
    /// whose eye is inside a vehicle (its own) is not hidden by it, and a person's collider is not cover. 🔴 Red-proof: drop the
    /// <c>vehicles</c> argument in <c>EqsTerrainSight.Sight</c> and the first assertion fails.
    /// </summary>
    [Fact]
    public void P7a_R10_TheAiSight_SeesAVehiclesBox_NotAPerson()
    {
        using var repo = VehicleWorld();
        Vehicle(repo, 30, 30);
        var los = EqsTerrainSight.Sight(repo, null)!;
        Assert.False(los.HasLineOfSight(new Vector3(30, 20, 1.0f), new Vector3(30, 40, 1.0f)), "a car hides at 1.0 m");
        Assert.True(los.HasLineOfSight(new Vector3(30, 20, 1.7f), new Vector3(30, 40, 1.7f)), "not over its 1.5 m roof");
        Assert.True(los.HasLineOfSight(new Vector3(30, 30.5f, 1.0f), new Vector3(30, 40, 1.0f)), "its own vehicle never hides a viewer inside it");
        // 1.65 m off the car's middle across its 1.8 m width — inside its 2.4 m collider circle, outside its box: a man at its side is hidden
        Assert.False(los.HasLineOfSight(new Vector3(30, 31.65f, 1.0f), new Vector3(30, 20, 1.0f)), "a man beside the car is hidden from the far side");

        using var people = VehicleWorld();
        Vehicle(people, 30, 30, cls: global::CarKinem.Core.VehicleClass.Pedestrian);
        Assert.True(EqsTerrainSight.Sight(people, null)!.HasLineOfSight(new Vector3(30, 20, 1.0f), new Vector3(30, 40, 1.0f)), "a person is not cover");
    }

    /// <summary>
    /// ⭐⭐ <c>P7a_R11</c> (O5 ②) — the cover generator adds points round a STANDING vehicle (all four sides, stance by its 1.5 m height:
    /// stand), none round a moving one, none round a static obstacle's collider (that one is terrain already, R-243).
    /// </summary>
    [Fact]
    public void P7a_R11_CoverPoints_RoundAStandingVehicle_NotAMovingOne()
    {
        using var repo = VehicleWorld();
        var self = Point(repo, 30, 22);
        var sensor = new EqsSensor { SearchRadius = 15f, ContextSlot0 = self };
        var c = new EqsResult[64];

        Assert.Equal(0, new CoverPointsGenerator().Generate(Entity.Null, ref sensor, repo, c));   // a flat world: no cover at all

        var car = Vehicle(repo, 30, 30);
        int n = new CoverPointsGenerator().Generate(Entity.Null, ref sensor, repo, c);
        Assert.Equal(6, n);   // ⭐ CE-3143 — 2 per 4.5 m side + 1 per 1.8 m end (rounded up; was 4)
        for (int i = 0; i < n; i++)
        {
            float d = Vector2.Distance(new Vector2(c[i].PositionX, c[i].PositionY), new Vector2(30, 30));
            Assert.InRange(d, 1.6f, 3.1f);   // 0.75 m off the 4.5 × 1.8 box
            Assert.True(c[i].TryGetStance(out var st));
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, st);
        }
        Assert.Equal(0, new CoverPointsGenerator { Kind = CoverKind.WindowFiring }.Generate(Entity.Null, ref sensor, repo, c));

        repo.SetComponent(car, new global::CarKinem.Core.VehicleState { Speed = 5f });
        Assert.Equal(0, new CoverPointsGenerator().Generate(Entity.Null, ref sensor, repo, c));   // driving: not cover

        repo.SetComponent(car, new global::CarKinem.Core.VehicleState { Speed = 0f });
        repo.AddComponent(car, new StaticObstacle { Material = new FixedString32("car-body") });
        Assert.Equal(0, new CoverPointsGenerator().Generate(Entity.Null, ref sensor, repo, c));   // terrain already: the provider owns it
    }

    private sealed class V2Eq : IEqualityComparer<Vector2>
    {
        private readonly float _eps;
        public V2Eq(float eps) => _eps = eps;
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= _eps;
        public int GetHashCode(Vector2 v) => 0;
    }
}
