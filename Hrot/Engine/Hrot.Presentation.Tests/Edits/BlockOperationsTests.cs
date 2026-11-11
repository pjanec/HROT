using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.Presentation.Edits;
using Xunit;

namespace Hrot.ScenarioEditor.Tests.Edits;

/// <summary>
/// ⭐⭐ T-4 (CE-4431, docs/DESIGN_Terrain_Authoring.md §18h H-1…H-4) — <see cref="BlockOperations"/>: Re-roll, Change recipe and Unpack are each ONE step of the editor's ONE <see cref="EditHistory"/> (the recorder of
/// <c>Edits.Submit</c>'s brackets), undo restores the block whole, and an unpacked block expands to the SAME geometry, materials and order as before — only the keys change — with its doors re-keyed and their state kept.
/// </summary>
public sealed class BlockOperationsTests : IDisposable
{
    private const string Hut = """{"footprint":[[0,0],[10,0],[10,8],[0,8]],"storeys":[{"height":3,"walls":[{"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}""";
    private static readonly Guid BlockGuid = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private readonly string _staging = Path.Combine(Path.GetTempPath(), "t4-" + Guid.NewGuid().ToString("N")[..10]);
    private readonly EntityRepository _world = new();
    private readonly EditHistory _history = new();
    private readonly WorldUndoRecorder _recorder;
    private readonly BlockExpansionContext _ctx;
    private readonly string _folder;
    private double _now;

    public BlockOperationsTests()
    {
        _folder = Path.Combine(_staging, "Terrain", "base-t");
        Directory.CreateDirectory(Path.Combine(_folder, "buildings")); Directory.CreateDirectory(Path.Combine(_staging, "terrain-recipes"));
        File.WriteAllText(Path.Combine(_folder, "terrain.json"), """{"schemaVersion":2,"name":"base-t","world":"w.geojson"}""");
        File.WriteAllText(Path.Combine(_folder, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,400,400],"groundZ":0},"features":[]}""");
        File.WriteAllText(Path.Combine(_folder, "buildings", "hut.building.json"), Hut);
        File.WriteAllText(Path.Combine(_staging, "terrain-recipes", "village.recipe.json"), """{"id":"village","houses":{"templates":["hut"],"count":[12,12],"radius":60,"minSpacing":4},"woods":{"count":[2,2],"size":[8,12]}}""");
        File.WriteAllText(Path.Combine(_staging, "terrain-recipes", "woodlot.recipe.json"), """{"id":"woodlot","woods":{"count":[3,3],"size":[8,12]}}""");
        File.WriteAllText(Path.Combine(_staging, "terrain-recipes", "mixed.recipe.json"), """
            {"id":"mixed","radius":60,"woods":{"count":[1,1],"size":[8,10]},"elements":[
              {"type":"lane","from":[-40,0],"to":[40,0],"width":4,"bend":3},
              {"type":"row","from":[-35,0],"to":[35,0],"templates":["hut"],"count":[6,6],"offset":10,"garden":{"material":"hedge"}},
              {"type":"cluster","templates":["hut"],"count":[2,2],"center":[0,30],"radius":8},
              {"type":"enclosure","center":[0,30],"size":[30,20],"material":"fence","gap":4},
              {"type":"enclosure","center":[0,-30],"size":[20,10],"material":"wall"},
              {"type":"group","items":[{"type":"car","at":[[10,-6,20]]},{"type":"sandbag-wall","line":{"from":[0,6],"to":[12,6],"count":[3,3]}}]},
              {"type":"orchard","center":[0,-20],"rows":2,"cols":3,"spacing":5},
              {"type":"fields","center":[0,50],"size":[40,20],"cols":2,"rows":2,"gate":0.5}]}
            """);
        string def = Path.Combine(_folder, "terrain.json");
        var baseWorld = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "base-t")!;
        _ctx = new BlockExpansionContext(baseWorld, TerrainAssets.ForFolder(_folder), new List<string?>(TerrainRecipe.RootsFor(_folder)), null);

        HrotSharedComponentRegistry.RegisterAll(_world);
        _recorder = new WorldUndoRecorder(_history, new WorldSnapshotRing(), () => true, () => _now);
        EditSteps.Install(_world, _recorder);
    }

    public void Dispose()
    {
        _world.Dispose();
        try { Directory.Delete(_staging, recursive: true); } catch { }
    }

    private Entity Block(string recipe = "village", uint seed = 7, float heading = 20f)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new SimTransform { Position = new Vector3(250, 250, 0), Rotation = Quaternion.Identity });
        _world.AddComponent(e, new TerrainBlock { Recipe = new FixedString32(recipe), Seed = seed, HeadingDeg = heading, Fit = TerrainBlock.FitAll });
        _world.AddComponent(e, new EntityGuid(BlockGuid));
        _world.Tick();
        return e;
    }

    private Entity Door(string key, TerrainDoorState state)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new DoorState { State = state });
        _world.SetManagedComponent(e, new TerrainObjectKey { Key = key });
        _world.Tick();
        return e;
    }

    private Entity FindBlock()
    {
        foreach (var e in _world.Query().With<TerrainBlock>().WithLifecycle(EntityLifecycle.All).Build()) return e;
        return Entity.Null;
    }

    private int Features() { int n = 0; foreach (var _ in _world.Query().With<TerrainFeature>().WithLifecycle(EntityLifecycle.All).Build()) n++; return n; }

    private LiveOverlayResult Expand()
    {
        var live = new LiveOverlay();
        return live.Expand(live.Snapshot(_world), _ctx.Base, 1, _folder)!;
    }

    /// <summary>What the live expansion made, with the keys left out: every building (template, place, turn, footprint), every surface and wall panel (type / material, outline) and each part's box, in ORDER.</summary>
    private static string Shape(LiveOverlayResult r)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in r.World.Buildings) sb.Append($"B {b.Template} {b.Position} {b.RotationDegrees} {b.BaseZ} {string.Join(';', b.Footprint)}\n");
        foreach (var s in r.World.Surfaces) sb.Append($"S {s.Type} {s.Density} {string.Join(';', s.Polygon)}\n");
        foreach (var p in r.World.Panels) sb.Append($"P {p.Material?.Name} {p.A} {p.B} {p.Thickness} {p.BaseZ} {p.TopZ} {p.Openings.Count}\n");
        foreach (var p in r.Parts) sb.Append($"X {p.Min} {p.Max}\n");
        sb.Append($"D {r.World.Doors.Count}");
        return sb.ToString();
    }

    private void Tick() => _now += 5;   // later than the coalescing window: the next action is its own step

    /// <summary>
    /// ⭐ (H-3) unpack → the recompile gives the same geometry, materials and ORDER as before the unpack (keys aside), and the door of a house keeps its state under the house's new key. 🔴 Red-proof: make the new guids
    /// DESCEND (<c>PartGuid</c> uses <c>0xFFFFFFFF − index</c>) ⇒ the parts' order reverses ⇒ red; drop the door re-key ⇒ the door rail reds.
    /// </summary>
    [Fact]
    public void T4_Unpack_ExpandsToTheSameShape_InTheSameOrder_AndTheDoorsFollowTheirHouses()
    {
        var block = Block();
        string oldKey = $"base-t/{BlockGuid:D}/3/front";
        var door = Door(oldKey, TerrainDoorState.Locked);
        var before = Expand();
        Assert.True(before.World.DoorIndexOf(oldKey) >= 0, "the door exists in the block's expansion");
        string shapeBefore = Shape(before);
        Assert.Equal(12, before.World.Buildings.Count);

        var r = BlockOperations.Unpack(_world, block, _ctx);
        Assert.True(r.Done, r.Why);
        Assert.False(_world.IsAlive(block));
        Assert.Equal(14, Features());   // 12 houses + 2 woods

        var after = Expand();
        Assert.Equal(shapeBefore, Shape(after));

        // the door: re-keyed to the house that is now a feature, state kept, and the recompiled world has a door by that key
        string newKey = _world.GetComponent<TerrainObjectKey>(door).Key;
        Assert.NotEqual(oldKey, newKey);
        Assert.True(after.World.DoorIndexOf(newKey) >= 0, $"the re-keyed door '{newKey}' is a door of the recompiled world");
        Assert.Equal(TerrainDoorState.Locked, _world.GetComponentRO<DoorState>(door).State);
        Assert.Equal(-1, after.World.DoorIndexOf(oldKey));
    }

    /// <summary>
    /// ⭐ undo after unpack restores the block in ONE step (and takes the features and the door key back); redo unpacks again, to the same keys. 🔴 Red-proof: close the bracket before <c>DestroyEntity</c> and open another
    /// ⇒ two steps ⇒ red.
    /// </summary>
    [Fact]
    public void T4_UndoAfterUnpack_RestoresTheBlockInOneStep_RedoUnpacksAgain()
    {
        var block = Block();
        string oldKey = $"base-t/{BlockGuid:D}/3/front";
        var door = Door(oldKey, TerrainDoorState.Locked);
        string shapeBefore = Shape(Expand());
        Tick();
        Assert.True(BlockOperations.Unpack(_world, block, _ctx).Done);
        Assert.Equal(1, _history.Count);
        Assert.Equal(BlockOperations.UnpackLabel, _history.UndoLabel);
        var keysAfter = Keys();

        Assert.True(_history.Undo());
        var back = FindBlock();
        Assert.NotEqual(Entity.Null, back);
        var tb = _world.GetComponentRO<TerrainBlock>(back);
        Assert.Equal("village", tb.Recipe.ToString()); Assert.Equal(7u, tb.Seed); Assert.Equal(20f, tb.HeadingDeg);
        Assert.Equal(BlockGuid, _world.GetComponentRO<EntityGuid>(back).Value);   // the restored block is the SAME block: its scenario key came back (🔴 red-proof: drop EntityGuid from the undo capture's identity set)
        AssertAt(back);
        Assert.Equal(0, Features());
        Assert.Equal(shapeBefore, Shape(Expand()));
        // ⭐ CE-3439 — the door comes back WITH its key and its state (DoorState / TerrainObjectKey are in the undo capture now)
        Assert.Equal(oldKey, _world.GetComponent<TerrainObjectKey>(door).Key);
        Assert.Equal(TerrainDoorState.Locked, _world.GetComponentRO<DoorState>(door).State);

        Assert.True(_history.Redo());
        Assert.Equal(14, Features());
        Assert.Equal(keysAfter, Keys());   // the same guids: an unpack that is undone and done again makes the same keys
        Assert.Equal(Entity.Null, FindBlock());
        Assert.Equal($"base-t/{BlockOperations.PartGuid(BlockGuid, 5):D}/0/front", _world.GetComponent<TerrainObjectKey>(door).Key);   // (the part label order is ordinal: G/0 G/1 G/10 G/11 G/2 G/3 → "G/3" is the 6th) redo: the door is on its house's new key again
        Assert.Equal(TerrainDoorState.Locked, _world.GetComponentRO<DoorState>(door).State);
    }

    private void AssertAt(Entity e) { var p = _world.GetComponentRO<SimTransform>(e).Position; Assert.Equal(250f, (float)p.X); Assert.Equal(250f, (float)p.Y); }

    private List<string> Keys()
    {
        var l = new List<string>();
        foreach (var e in _world.Query().With<TerrainFeature>().WithLifecycle(EntityLifecycle.All).Build()) l.Add(_world.GetComponentRO<EntityGuid>(e).Value.ToString("D"));
        l.Sort(StringComparer.Ordinal);
        return l;
    }

    /// <summary>
    /// ⭐ (H-1) a re-roll is one step that moves the seed to <see cref="BlockOperations.NextSeed"/>; undo → the old seed, redo → the same new seed, and doing it again after the undo gives the SAME seed (derived, not random).
    /// 🔴 Red-proof: mix <c>Environment.TickCount64</c> into <c>NextSeed</c> ⇒ the second roll differs ⇒ red.
    /// </summary>
    [Fact]
    public void T4_Reroll_IsOneStep_UndoRedoGiveTheSameSeeds_AndRollingAgainIsTheSameSeed()
    {
        var block = Block();
        string layout0 = Shape(Expand());
        Assert.True(BlockOperations.Reroll(_world, block).Done);
        uint s1 = _world.GetComponentRO<TerrainBlock>(block).Seed;
        Assert.Equal(1, _history.Count);
        Assert.Equal(BlockOperations.NextSeed(7), s1);
        Assert.NotEqual(7u, s1);
        Assert.NotEqual(layout0, Shape(Expand()));   // another layout

        Assert.True(_history.Undo());
        Assert.Equal(7u, _world.GetComponentRO<TerrainBlock>(FindBlock()).Seed);
        Assert.True(_history.Redo());
        Assert.Equal(s1, _world.GetComponentRO<TerrainBlock>(FindBlock()).Seed);

        Assert.True(_history.Undo());
        Tick();
        Assert.True(BlockOperations.Reroll(_world, FindBlock()).Done);
        Assert.Equal(s1, _world.GetComponentRO<TerrainBlock>(FindBlock()).Seed);   // the same next seed, twice
        Assert.NotEqual(BlockOperations.NextSeed(s1), s1);
    }

    /// <summary>
    /// ⭐ (H-2) change recipe keeps the place, the heading, the seed and the fit; one step; undo brings the old recipe back. 🔴 Red-proof: reset <c>HeadingDeg</c> in <c>ChangeRecipe</c> ⇒ red.
    /// </summary>
    [Fact]
    public void T4_ChangeRecipe_KeepsPositionAndHeading_InOneStep()
    {
        var block = Block();
        Assert.True(BlockOperations.ChangeRecipe(_world, block, "woodlot").Done);
        var b = _world.GetComponentRO<TerrainBlock>(block);
        Assert.Equal("woodlot", b.Recipe.ToString());
        Assert.Equal(20f, b.HeadingDeg); Assert.Equal(7u, b.Seed); Assert.Equal(TerrainBlock.FitAll, b.Fit);
        AssertAt(block);
        Assert.Equal(1, _history.Count);
        Assert.True(_history.Undo());
        Assert.Equal("village", _world.GetComponentRO<TerrainBlock>(FindBlock()).Recipe.ToString());
    }

    private sealed class Observer : IEditGate { public string? Refuse(EntityRepository repo, EditOp op) => "this station is an observer"; }

    /// <summary>⭐ a station whose gate refuses (or a block that is not one) changes NOTHING and records no step — the checks <c>Edits.Submit</c> makes.</summary>
    [Fact]
    public void T4_ARefusingGate_ChangesNothing_AndASeedOfAnotherEntityIsRefused()
    {
        var block = Block();
        EditGate.Install(_world, new Observer());
        var r = BlockOperations.Reroll(_world, block);
        Assert.False(r.Done); Assert.Equal("this station is an observer", r.Why);
        Assert.False(BlockOperations.Unpack(_world, block, _ctx).Done);
        Assert.False(BlockOperations.ChangeRecipe(_world, block, "woodlot").Done);
        Assert.Equal(7u, _world.GetComponentRO<TerrainBlock>(block).Seed);
        Assert.Equal(0, _history.Count);
        Assert.Equal(0, Features());

        var notABlock = _world.CreateEntity();
        Assert.False(BlockOperations.Reroll(_world, notABlock).Done);
    }

    /// <summary>
    /// ⭐ T-3 — a block of EVERY element kind (lane, row with gardens, cluster, fence and wall enclosures, clutter, orchard trees, hedgerow fields, a wood) unpacks to the same shape in the same order: each part kind has a feature kind
    /// (Lane, Hedge / Fence / Wall, Tree, Clutter, Wood, House). 🔴 Red-proof: drop the hedge case in <c>TryRead</c> ⇒ the unpack is refused ⇒ red.
    /// </summary>
    [Fact]
    public void T3_ABlockOfEveryElementKind_UnpacksToTheSameShape()
    {
        var block = Block("mixed", 5, 15f);
        string before = Shape(Expand());
        Assert.Contains("S Open", before); Assert.Contains("P hedge", before);
        var r = BlockOperations.Unpack(_world, block, _ctx);
        Assert.True(r.Done, r.Why);
        Assert.Equal(before, Shape(Expand()));
    }

    /// <summary>
    /// ⭐ CE-3439 — an undo does NOT strip a door of its state and key: re-roll, then undo, then redo, and the door keeps its <c>TerrainObjectKey</c> and <c>DoorState</c> every time. 🔴 Red-proof: drop <c>DoorState</c> from the undo capture's identity set
    /// (<c>WorldKeyframes.IsReplicationIdentity</c>) ⇒ the door has no state after the undo ⇒ red; drop <c>TerrainObjectKey</c> ⇒ no key ⇒ red.
    /// </summary>
    [Fact]
    public void CE3439_AnUndoKeepsADoorsStateAndKey()
    {
        var block = Block();
        var door = Door("base-t/x/3/front", TerrainDoorState.Locked);
        Assert.True(BlockOperations.Reroll(_world, block).Done);
        Assert.True(_history.Undo());
        Assert.Equal("base-t/x/3/front", _world.GetComponent<TerrainObjectKey>(door).Key);
        Assert.Equal(TerrainDoorState.Locked, _world.GetComponentRO<DoorState>(door).State);
        Assert.True(_history.Redo());
        Assert.Equal("base-t/x/3/front", _world.GetComponent<TerrainObjectKey>(door).Key);
        Assert.Equal(TerrainDoorState.Locked, _world.GetComponentRO<DoorState>(door).State);
    }
}
