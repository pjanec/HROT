using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Terrain;
using Hrot.Look.Terrain;

namespace Hrot.Look.Tests;

/// <summary>⭐ CE-5034 GW-3 — the look of glass as pure functions (📄 docs/DESIGN_Glass_Windows.md §5, §6a): which panes stand, where a crack lands, the shards,
/// the burst — and that a renderer bursts only on a window it SAW turn broken.</summary>
public sealed class GlassLookTests
{
    private const string House = """
        { "type": "FeatureCollection", "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H",
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick", "openings": [ { "kind": "window", "at": 2, "width": 1.2 }, { "kind": "window", "at": 6, "width": 1.2 } ] },
                  { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                  { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                  { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

    private static TerrainWorld World() => TerrainWorldParser.Parse(House, "range");
    private static int South(TerrainWorld w) { for (int i = 0; i < w.Panels.Count; i++) if (w.Panels[i].Openings.Count == 2) return i; throw new InvalidOperationException(); }

    private static (EntityRepository Repo, Entity E) Damaged(Action<Func<WindowDamage>, Action<WindowDamage>> act)
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<WindowDamage>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new WindowDamage { Building = 0 });
        act(() => repo.GetComponentRO<WindowDamage>(e), d => repo.SetComponent(e, d));
        return (repo, e);
    }

    [Fact]
    public void EveryGlazedWindow_IsAPane_InItsOpening()
    {
        var w = World();
        var into = new List<PaneQuad>();
        Assert.Equal(2, GlassLook.Panes(w, null, into));
        var q = into[0];
        Assert.Equal(22f, q.A.X, 3); Assert.Equal(23.2f, q.B.X, 3);
        Assert.Equal(0.9f, q.A.Z, 3); Assert.Equal(2.1f, q.D.Z, 3);
        Assert.Equal(20f, q.A.Y, 3);
        Assert.Equal(1f, MathF.Abs(q.Normal.Y), 3);
    }

    [Fact]
    public void PointAt_IsTheInverseOf_PaneUv()
    {
        var w = World();
        int s = South(w);
        var p = GlassLook.PointAt(w, s, 1, 0.3f, 0.7f);
        var (u, v) = WindowDamages.PaneUv(w, s, 1, p);
        Assert.Equal(0.3f, u, 3);
        Assert.Equal(0.7f, v, 3);
    }

    [Fact]
    public void ABrokenWindow_HasNoPane_ButShards_AndACrackedOne_KeepsItsPane_WithAStarAtTheHit()
    {
        var w = World();
        int s = South(w);
        var (repo, _) = Damaged((get, set) =>
        {
            var d = get();
            WindowDamages.Hit(ref d, s, 0, 0.25f, 0.5f);
            WindowDamages.Break(ref d, s, 1);
            set(d);
        });
        var table = WindowTable.Fresh(repo)!;
        var doors = DoorStates.Of(repo, w);
        var panes = new List<PaneQuad>();
        Assert.Equal(1, GlassLook.Panes(w, doors, panes));
        Assert.Equal(0, panes[0].Opening);

        Assert.True(table.TryGet(s, 0, out var cracked));
        var lines = new List<GlassLine>();
        Assert.True(GlassLook.CrackStar(w, cracked, 0, lines) > 12);
        Assert.Equal(0, GlassLook.CrackStar(w, cracked, 1, lines.ToList()));   // one hit kept, no second star
        var centre = GlassLook.ImpactOf(w, cracked, 0)!.Value;
        var expect = GlassLook.PointAt(w, s, 0, cracked.U0 / 255f, cracked.V0 / 255f);
        Assert.True(Vector3.Distance(centre, expect) < 1e-4f);
        foreach (var l in lines) Assert.True(Vector3.Distance(l.To, centre) < 0.3f, "every crack stays round its hit");
        Assert.Contains(lines, l => Vector3.Dot(l.From - centre, panes[0].Normal) > 0f);   // ⭐ both faces (§5)
        Assert.Contains(lines, l => Vector3.Dot(l.From - centre, panes[0].Normal) < 0f);

        Assert.True(table.TryGet(s, 1, out var broken));
        Assert.Equal(0, GlassLook.CrackStar(w, broken, 0, lines));
        var shards = new List<GlassShard>();
        int n = GlassLook.Shards(w, s, 1, shards);
        Assert.InRange(n, 3, 5);
        var again = new List<GlassShard>();
        GlassLook.Shards(w, s, 1, again);
        Assert.Equal(shards, again);   // ⭐ seeded: the same on every renderer
    }

    [Fact]
    public void ACollapsedBuilding_DrawsNoPane()
    {
        var w = World();
        var repo = new EntityRepository();
        repo.RegisterComponent<BuildingDamage>();
        var e = repo.CreateEntity();
        var d = new BuildingDamage { Building = 0 };
        d.Collapsed = 1;
        repo.AddComponent(e, d);
        Assert.Equal(0, GlassLook.Panes(w, DoorStates.Of(repo, w), new List<PaneQuad>()));
    }

    [Fact]
    public void ShatterWatch_BurstsOnlyOnAWindowItSawTurnBroken()
    {
        var w = World();
        int s = South(w);
        var watch = new ShatterWatch();
        var into = new List<(int, int)>();

        var (first, _) = Damaged((get, set) => { var d = get(); WindowDamages.Break(ref d, s, 1); set(d); });
        Assert.Equal(0, watch.Seen(WindowTable.Fresh(first), w, null, into));   // ⛔ the first table: a late joiner plays none

        var (second, _) = Damaged((get, set) => { var d = get(); WindowDamages.Break(ref d, s, 1); WindowDamages.Break(ref d, s, 0); set(d); });
        Assert.Equal(1, watch.Seen(WindowTable.Fresh(second), w, null, into));
        Assert.Equal((s, 0), into[0]);
        Assert.Equal(0, watch.Seen(WindowTable.Fresh(second), w, null, into));   // once
    }

    [Fact]
    public void TheOutwardNormal_PointsAwayFromTheBuilding()
    {
        var w = World();
        Assert.True(GlassLook.OutwardOf(w, South(w)).Y < -0.9f);   // the south wall at y 20 of a house spanning y 20..28: out is −y
    }

    [Fact]
    public void ABurst_FliesOutAndDown_ThenEnds()
    {
        var o = new Vector3(0, 0, 2);
        var n = Vector3.UnitY;
        float outward = 0, fallen = 0;
        for (int i = 0; i < GlassLook.BurstShards; i++)
        {
            var p = GlassLook.BurstShard(o, n, 7u, i, 0.6f)!.Value;
            outward += p.Y; fallen += o.Z - p.Z;
        }
        Assert.True(outward > 0f);
        Assert.True(fallen > 0f);
        Assert.Null(GlassLook.BurstShard(o, n, 7u, 0, GlassLook.BurstSeconds + 0.1f));
        Assert.Equal(GlassLook.BurstShard(o, n, 7u, 3, 0.4f), GlassLook.BurstShard(o, n, 7u, 3, 0.4f));
    }

    [Fact]
    public void Panes_AllocateNothing_InTheSteadyState()
    {
        var w = World();
        var into = new List<PaneQuad>(8);
        GlassLook.Panes(w, null, into);
        into.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        GlassLook.Panes(w, null, into);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
