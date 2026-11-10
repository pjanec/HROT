#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Terrain.Tests;
using Xunit;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// ⭐⭐ <c>CE-4391</c> S-3 (docs/DESIGN_Terrain_Import_UTM.md §8.1 P3-a / P3-b, §8.2 B-10) — the navmesh follows the damage: a passage baked through the overlay is walkable and a path
/// uses it; each TILE carries a version that moves only when its content changed; a path stamped with the change sequence it was planned at is replanned when a tile it crosses moved —
/// and not otherwise — by the ONE replan (<c>NavigationExecutionSystem.RequestReplan</c>), with a zero-allocation check.
/// </summary>
public sealed class BuildingNavTests
{
    // a 60 x 60 world, a brick house 10 x 8 at (20, 20), no door: closed, so its inside is an island until a wall is breached
    private const string BoundedHouse = """
        { "type": "FeatureCollection", "hrot": { "schemaVersion": 1, "bounds": [0, 0, 60, 60], "groundZ": 0 }, "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H",
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" }, { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                  { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" }, { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

    private static readonly Vector3 Outside = new(25f, 10f, 0f), Inside = new(25f, 24f, 0f);

    private static (TerrainWorld World, int South, BuildingDamageTable Passage) Scene()
    {
        var w = TerrainWorldParser.Parse(BoundedHouse, "range");
        int south = BreachFixtures.SouthWall(w);
        var repo = BreachFixtures.World(w);
        BreachFixtures.Damage(repo, w, 0, new WallBreach(south, 4f, 6.2f, 0f, 2.2f, BreachKind.Passage));
        return (w, south, DoorStates.Of(repo, w).Damage!);
    }

    private static RecastNavmeshFactory Factory() => new() { Layers = NavLayerMask.Infantry, TileCache = new NavTileCache() };   // memory only, one per test

    /// <summary>
    /// ⭐ A passage becomes WALKABLE after the bake — the inside was an island, the baked derived world lets a path in, and the path goes THROUGH the gap. 🔴 Red-proof: have
    /// <c>TerrainObstacles.With</c> ignore the damage (return <c>terrain</c>) ⇒ the second bake still has no way in.
    /// </summary>
    [Fact]
    public void APassage_IsWalkableAfterTheBake_AndAPathUsesIt()
    {
        var (w, _, passage) = Scene();
        var factory = Factory();
        var before = factory.Build(w)!;
        Assert.True(before.IsWalkable(Inside));                                                    // the interior is ground, but an island
        Assert.False(before.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry));

        var after = factory.Build(TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), passage))!;
        Assert.True(after.PathExists(Outside, Inside, (uint)NavLayerMask.Infantry));
        Span<NavWaypoint> wps = stackalloc NavWaypoint[64];
        int n = after.PlanPath(Outside, Inside, wps, (uint)NavLayerMask.Infantry);
        Assert.True(n >= 2);
        bool throughGap = false;
        for (int i = 0; i < n; i++) throughGap |= wps[i].Position.Y is > 19f and < 21f && wps[i].Position.X is > 24f and < 26.2f;
        for (int i = 1; i < n; i++)   // …or a segment that crosses the wall line inside the gap
            if ((wps[i - 1].Position.Y - 20f) * (wps[i].Position.Y - 20f) <= 0f)
            {
                float t = (20f - wps[i - 1].Position.Y) / (wps[i].Position.Y - wps[i - 1].Position.Y + 1e-6f);
                float x = wps[i - 1].Position.X + t * (wps[i].Position.X - wps[i - 1].Position.X);
                throughGap |= x is > 24f and < 26.2f;
            }
        Assert.True(throughGap, "the path enters through the passage (x 24 .. 26.2 at y = 20)");
    }

    /// <summary>
    /// ⭐ P3-a — a tile's version moves ONLY when its content key changed: the first publish stamps every tile; the rebake with the passage changes the house's tiles and leaves a far tile with the
    /// first version. 🔴 Red-proof: bump every tile on a publish ⇒ the far tile's version is the second one.
    /// </summary>
    [Fact]
    public void TileVersions_MoveOnlyForTheTilesWhoseContentChanged()
    {
        var (w, _, passage) = Scene();
        var factory = Factory();
        var node = new SwitchableNavmeshProvider();
        Assert.Equal(0u, node.ChangeSequence);

        node.Publish(factory.Build(w));
        uint first = node.ChangeSequence;
        Assert.True(first >= 1);
        float m = node.TileMetres;
        Assert.True(m > 0f);
        int Tile(float v) => (int)MathF.Floor(v / m);
        Assert.Equal(first, node.TileVersion(Tile(25f), Tile(20f)));
        Assert.Equal(first, node.TileVersion(Tile(55f), Tile(55f)));

        node.Publish(factory.Build(w));                                                           // the same world again: nothing changed
        Assert.Equal(first, node.ChangeSequence);

        node.Publish(factory.Build(TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), passage)));
        uint second = node.ChangeSequence;
        Assert.True(second > first);
        Assert.Equal(second, node.TileVersion(Tile(25f), Tile(20f)));                              // the tile holding the breached wall
        Assert.Equal(first, node.TileVersion(Tile(55f), Tile(55f)));                               // a tile nothing touched
        Assert.Equal(first, node.TileVersion(Tile(5f), Tile(55f)));
    }

    private static (SwitchableNavmeshProvider Node, uint First, uint Second) TwoPublishes()
    {
        var (w, _, passage) = Scene();
        var factory = Factory();
        var node = new SwitchableNavmeshProvider();
        node.Publish(factory.Build(w)); uint first = node.ChangeSequence;
        node.Publish(factory.Build(TerrainObstacles.With(w, Array.Empty<TerrainPrism>(), passage)));
        return (node, first, node.ChangeSequence);
    }

    private static int Register(TrajectoryPoolManager pool, params Vector3[] p) => pool.RegisterTrajectory(p);

    /// <summary>
    /// ⭐ P3-b — a path through the changed tiles is stale, one that is not through them is not, one already past them is not, and one planned at the newest sequence is not. 🔴 Red-proof: make
    /// <c>CrossesChangedTile</c> return true / false unconditionally ⇒ the opposite assertion fails.
    /// </summary>
    [Fact]
    public void ARoute_IsStaleExactlyWhenATileItCrossesChanged()
    {
        var (node, first, second) = TwoPublishes();
        var pool = new TrajectoryPoolManager();
        var through = pool.TryGetTrajectory(Register(pool, new Vector3(25f, 2f, 0f), new Vector3(25f, 40f, 0f)), out var t1) ? t1 : default;
        var far = pool.TryGetTrajectory(Register(pool, new Vector3(50f, 50f, 0f), new Vector3(58f, 58f, 0f)), out var t2) ? t2 : default;
        var behind = pool.TryGetTrajectory(Register(pool, new Vector3(25f, 2f, 0f), new Vector3(25f, 40f, 0f), new Vector3(58f, 58f, 0f)), out var t3) ? t3 : default;

        Assert.True(NavigationExecutionSystem.CrossesChangedTile(in through, 0f, node, first));
        Assert.False(NavigationExecutionSystem.CrossesChangedTile(in far, 0f, node, first));
        Assert.False(NavigationExecutionSystem.CrossesChangedTile(in through, 0f, node, second));          // planned on the newest mesh
        Assert.False(NavigationExecutionSystem.CrossesChangedTile(in behind, 45f, node, first));           // already past the house (progress 45 m of a 38 + 41 m path)
        Assert.True(NavigationExecutionSystem.CrossesChangedTile(in behind, 0f, node, first));
    }

    /// <summary>
    /// ⭐⭐ P3-b through the real system: two movers on paths planned at the first mesh — one through the breached house's tiles, one far from them — the mesh is rebaked, and ONLY the first is
    /// asked to replan (the one <c>RequestReplan</c>: a <c>PathReplannedEvent</c> and a new request); it is judged once per sequence. 🔴 Red-proof: drop the sequence compare (always walk) is invisible;
    /// make <c>CrossesChangedTile</c> true ⇒ the far mover replans too; drop the <c>RequestReplan</c> call ⇒ nobody does.
    /// </summary>
    [Fact]
    public void OnlyThePathThroughTheTouchedTiles_IsReplanned_AndOnceperSequence()
    {
        var (node, first, second) = TwoPublishes();
        var pool = new TrajectoryPoolManager();
        using var repo = Fdp.Toolkit.Navigation.Tests.NavigationTestWorldFactory.Create();
        repo.SetSingletonManaged<INavmeshProvider>(node);

        Entity Mover(int trajectory, Vector3 at, uint plannedAt)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new SimTransform { Position = at });
            repo.AddComponent(e, new SimVelocity { Linear = new Vector3(0f, 5f, 0f) });
            repo.AddComponent(e, new NavigationIntent { Mode = NavigationMode.PathToPoint, IntentId = 1, FinalDestination = new Vector3(at.X, 40f, 0f), ArrivalRadius = 1f });
            repo.AddComponent(e, new NavigationStatus { IntentId = 1, Result = NavigationResult.InProgress });
            repo.AddComponent(e, new FrustrationTicks());
            repo.AddComponent(e, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = trajectory, TargetSpeed = 5f });
            repo.AddComponent(e, new NavigationCorridorMuscle { RouteHandle = trajectory, TileSequence = plannedAt });
            return e;
        }

        var near = Mover(Register(pool, new Vector3(25f, 2f, 0f), new Vector3(25f, 40f, 0f)), new Vector3(25f, 2f, 0f), first);
        var farOne = Mover(Register(pool, new Vector3(50f, 50f, 0f), new Vector3(58f, 58f, 0f)), new Vector3(50f, 50f, 0f), first);
        var sys = new NavigationExecutionSystem(pool);

        sys.Execute(repo, 0.016f);
        repo.Bus.SwapBuffers();
        var replanned = repo.Bus.Read<PathReplannedEvent>().ToArray();
        Assert.Equal(new[] { near }, replanned.Select(e => e.Target).ToArray());
        Assert.Equal(1, sys.TileReplans);
        Assert.Equal(second, repo.GetComponentRO<NavigationCorridorMuscle>(near).TileSequence);            // judged for this sequence
        Assert.Equal(second, repo.GetComponentRO<NavigationCorridorMuscle>(farOne).TileSequence);          // …and so was the far one: walked, found clean, never walked again

        sys.Execute(repo, 0.016f);
        repo.Bus.SwapBuffers();
        Assert.Empty(repo.Bus.Read<PathReplannedEvent>().ToArray());                                        // once per sequence
        Assert.Equal(1, sys.TileReplans);
    }

    /// <summary>
    /// ⭐ The warm stamp check allocates 0 B: the compare that ends it (nothing changed) and the walk of a long path through changed tiles. 🔴 Red-proof: allocate in <c>CrossesChangedTile</c> (a list of
    /// tiles) ⇒ the byte count is not 0.
    /// </summary>
    [Fact]
    public void TheStampCheck_AllocatesNothing()
    {
        var (node, first, second) = TwoPublishes();
        var pool = new TrajectoryPoolManager();
        pool.TryGetTrajectory(Register(pool, new Vector3(2f, 2f, 0f), new Vector3(25f, 40f, 0f), new Vector3(58f, 58f, 0f)), out var path);
        bool a = false, b = false;
        for (int i = 0; i < 3; i++) { a |= NavigationExecutionSystem.CrossesChangedTile(in path, 0f, node, first); b |= NavigationExecutionSystem.CrossesChangedTile(in path, 0f, node, second); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { a |= NavigationExecutionSystem.CrossesChangedTile(in path, 0f, node, first); b |= NavigationExecutionSystem.CrossesChangedTile(in path, 0f, node, second); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(a); Assert.False(b);
    }
}
