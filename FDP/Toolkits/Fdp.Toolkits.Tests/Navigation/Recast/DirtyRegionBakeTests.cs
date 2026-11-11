using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Terrain.Tests;
using Xunit;

namespace Fdp.Toolkit.Navigation.Recast.Tests;

/// <summary>
/// ⭐ O-7 A (CE-4418, DESIGN_Terrain_Authoring §18e A-1) — the dirty-region rebake: tiles outside the dirty rectangles are taken by their PREVIOUS key (no gather, no hash). The rail's meaning: a dirty bake's
/// tile-key map equals a full bake's whenever the dirty region covers the change — and DIFFERS when it does not (so the rail can see a wrong region).
/// </summary>
public sealed class DirtyRegionBakeTests : IDisposable
{
    private readonly OverlayFixture _f = new();
    public void Dispose() => _f.Dispose();

    private static RecastNavmeshFactory Factory(NavTileCache? cache = null) => new() { TileCache = cache ?? new NavTileCache() };

    private TerrainPrism Wall(TerrainWorld w, Vector2 at)
        => TerrainObstacles.PrismOf(new Vector3(at.X, at.Y, 0f), 0f, new ObstacleShape { Length = 5f, Width = 0.3f, Height = 2f }, w.Materials.Get("concrete", "t"), "obstacle:1");

    private static (Vector2, Vector2) Box(TerrainPrism p) => (p.Min, p.Max);

    private static Dictionary<(NavLayerMask, int, int), NavTileKey> Keys(RecastNavmeshFactory f) => f.LastKeys.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Full bake of <paramref name="w"/> by a fresh factory sharing <paramref name="cache"/> (every tile a hit): the reference key map.</summary>
    private static Dictionary<(NavLayerMask, int, int), NavTileKey> FullKeys(TerrainWorld w, NavTileCache cache)
    {
        var f = Factory(cache); f.Build(w); return Keys(f);
    }

    /// <summary>
    /// (a) A MOVED WALL: a dirty bake of the moved world has the same tile-key map as a full bake, and reused tiles outside the dirty box. 🔴 Red-proof: have <c>MeetsDirty</c> answer false (reuse everything) ⇒ the maps differ;
    /// have the baker ignore <c>Dirty</c> ⇒ <c>Reused</c> is 0.
    /// </summary>
    [Fact]
    public void AMovedWall_DirtyBakeKeysEqualAFullBakes_AndTilesAreReused()
    {
        var baseWorld = _f.BaseWorld();
        var w1 = TerrainObstacles.With(baseWorld, new[] { Wall(baseWorld, new Vector2(250, 250)) });
        var w2 = TerrainObstacles.With(baseWorld, new[] { Wall(baseWorld, new Vector2(252, 250)) });
        var cache = new NavTileCache();
        var f = Factory(cache);
        f.Build(w1);
        var dirty = new List<(Vector2, Vector2)> { Box(w1.Prisms[^1]), Box(w2.Prisms[^1]) };
        f.Build(w2, dirty);
        Assert.True(f.LastStats.Reused > 0, "tiles outside the dirty box are reused");
        Assert.InRange(f.LastStats.Baked, 1, f.LastStats.Tiles / 20);   // only the wall's own tiles are new
        Assert.Equal(FullKeys(w2, cache), Keys(f));
    }

    /// <summary>(a, hamlet) A MOVED HAMLET (a merged overlay world): the dirty region = the old and new block's parts; keys equal the full bake's.</summary>
    [Fact]
    public void AMovedHamlet_DirtyBakeKeysEqualAFullBakes()
    {
        var baseWorld = _f.BaseWorld();
        var assets = TerrainAssets.ForFolder(_f.TerrainFolder);
        var recipe = _f.Recipe("village");
        TerrainWorld Overlay(float dx)
        {
            var x = FeatureExpander.Expand(recipe, 21, new Vector2(300 + dx, 300), 0f, TerrainBlock.FitAll, baseWorld, "g1", assets);
            string json = "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"groundZ\":0,\"bounds\":[0,0,400,400]},\"features\":[" + string.Join(",", x.Features.Select(ft => ft.ToJsonString())) + "]}";
            return TerrainWorldParser.Parse(json, "base-t", assets);
        }
        var o1 = Overlay(0f); var o2 = Overlay(20f);
        var m1 = TerrainWorldMerge.Merge(baseWorld, o1); var m2 = TerrainWorldMerge.Merge(baseWorld, o2);
        var cache = new NavTileCache();
        var f = Factory(cache);
        f.Build(m1);
        var dirty = new List<(Vector2, Vector2)>();
        foreach (var o in new[] { o1, o2 })
        {
            foreach (var p in o.Prisms) dirty.Add((p.Min, p.Max));
            foreach (var w in o.Walkables) dirty.Add((w.Min, w.Max));
            foreach (var s in o.Surfaces) dirty.Add((s.Min, s.Max));
        }
        f.Build(m2, dirty);
        Assert.True(f.LastStats.Reused > 0);
        Assert.Equal(FullKeys(m2, cache), Keys(f));
    }

    /// <summary>
    /// (b) The meaning of (a): a dirty rectangle that MISSES the old position (a wall moved 30 m, dirty = the new box only) leaves the old place's tiles with their stale keys — the maps DIFFER. 🔴 Red-proof: none needed
    /// (it IS the red case); if this passed with equal maps the rail could not see a wrong region.
    /// </summary>
    [Fact]
    public void AShrunkDirtyRect_MissingTheOldPosition_MakesTheKeysDiffer()
    {
        var baseWorld = _f.BaseWorld();
        var w1 = TerrainObstacles.With(baseWorld, new[] { Wall(baseWorld, new Vector2(250, 250)) });
        var w2 = TerrainObstacles.With(baseWorld, new[] { Wall(baseWorld, new Vector2(280, 250)) });
        var cache = new NavTileCache();
        var f = Factory(cache);
        f.Build(w1);
        f.Build(w2, new List<(Vector2, Vector2)> { Box(w2.Prisms[^1]) });   // the old position is NOT in it
        Assert.NotEqual(FullKeys(w2, cache), Keys(f));
        // and with the old place included they agree again
        var g = Factory(cache); g.Build(w1);
        g.Build(w2, new List<(Vector2, Vector2)> { Box(w1.Prisms[^1]), Box(w2.Prisms[^1]) });
        Assert.Equal(FullKeys(w2, cache), Keys(g));
    }

    /// <summary>A null dirty region is today's full bake exactly (nothing reused); a different world bounds ignores a given region (a full bake, never a wrong one).</summary>
    [Fact]
    public void NullDirty_IsTheFullBake_AndChangedBoundsIgnoreTheRegion()
    {
        var baseWorld = _f.BaseWorld();
        var w1 = TerrainObstacles.With(baseWorld, new[] { Wall(baseWorld, new Vector2(250, 250)) });
        var cache = new NavTileCache();
        var f = Factory(cache);
        f.Build(w1);
        f.Build(w1, null);
        Assert.Equal(0, f.LastStats.Reused);
        var outside = TerrainObstacles.With(baseWorld, new[] { Wall(baseWorld, new Vector2(450, 250)) });   // grows the bounds past the base's
        f.Build(outside, new List<(Vector2, Vector2)> { Box(outside.Prisms[^1]) });
        Assert.Equal(0, f.LastStats.Reused);
        Assert.Equal(FullKeys(outside, cache), Keys(f));
    }
}
