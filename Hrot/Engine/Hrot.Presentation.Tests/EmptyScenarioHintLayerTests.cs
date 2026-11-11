using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.Map2D;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ L-12 / CE-4311 (Scenario lifecycle LC-1) — the empty scenario's map hint: shown while a terrain is loaded, the station edits and nothing is placed;
/// gone after the first entity, in a run and with no terrain; 0 B per steady check. 🔴 Red-proof (compiles): <c>Shows</c> skipping the entity check ⇒ the
/// "gone after the first entity" row ⇒ red.
/// </summary>
public sealed class EmptyScenarioHintLayerTests
{
    private static EntityRepository WithTerrain()
    {
        var w = new EntityRepository();
        w.SetSingletonManaged(new TerrainDefinition { ResolvedName = "cqb-village" });
        w.SetSingletonManaged(new TerrainWorld { BoundsMin = Vector2.Zero, BoundsMax = new Vector2(330, 232) });
        w.RegisterComponent<EntityInfo>();
        Hrot.Common.Editing.StationMode.Install(w, () => Hrot.Common.Editing.EditingMode.Edit);
        return w;
    }

    [Fact]
    public void L12_TheHintShowsOnAnEmptyEditedScenario_AndGoesWithTheFirstEntity()
    {
        var w = WithTerrain();
        var layer = new EmptyScenarioHintLayer(() => w);
        Assert.True(layer.Shows());

        var e = w.CreateEntity();
        w.AddComponent(e, new EntityInfo());
        Assert.False(layer.Shows());                                          // the first entity: gone

        Assert.False(new EmptyScenarioHintLayer(() => new EntityRepository()).Shows());   // no terrain loaded: nothing to say
        var live = WithTerrain();
        Hrot.Common.Editing.StationMode.Install(live, () => Hrot.Common.Editing.EditingMode.Live);
        Assert.False(new EmptyScenarioHintLayer(() => live).Shows());          // a run is not authoring
    }

    /// <summary>⭐ U-12 / CE-4129 — a Preview or Live run over a world with NO entity says so (it used to say nothing: "Pressed play. No entities."); the edit hint stays Edit's; both go with the first entity, and a run with no terrain says nothing.
    /// 🔴 Red-proof (compiles): <c>ShowsRunNotice</c> always false ⇒ red.</summary>
    [Fact]
    public void CE4129_ARunOverAnEmptyWorld_SaysSo()
    {
        foreach (var mode in new[] { Hrot.Common.Editing.EditingMode.Preview, Hrot.Common.Editing.EditingMode.Live })
        {
            var w = WithTerrain();
            Hrot.Common.Editing.StationMode.Install(w, () => mode);
            var layer = new EmptyScenarioHintLayer(() => w);
            Assert.True(layer.ShowsRunNotice(), $"{mode}: an empty world says so");
            Assert.False(layer.Shows());                                       // the Edit hint ("right-click ▸ Add Entity") is not for a run
            var e = w.CreateEntity(); w.AddComponent(e, new EntityInfo());
            Assert.False(layer.ShowsRunNotice());                              // the first entity: gone
        }
        var edit = WithTerrain();
        Assert.False(new EmptyScenarioHintLayer(() => edit).ShowsRunNotice());   // Edit has its own hint
        var noTerrain = new EntityRepository();
        Hrot.Common.Editing.StationMode.Install(noTerrain, () => Hrot.Common.Editing.EditingMode.Live);
        Assert.False(new EmptyScenarioHintLayer(() => noTerrain).ShowsRunNotice());
    }

    /// <summary>⭐ Terrain authoring U-3 (the coordinator's finding on the overview frame) — placed terrain blocks and features ARE content: the "Empty scenario" hint must not draw over them, even for one that carries no
    /// <c>EntityInfo</c> (a file from before the picks added it). 🔴 Red-proof (compiles): the emptiness test back on <c>EntityInfo</c> alone ⇒ red.</summary>
    [Fact]
    public void U3_APlacedTerrainBlock_OrFeature_IsContent_TheHintGoes()
    {
        var w = WithTerrain();
        w.RegisterComponent<Fdp.Toolkit.Terrain.TerrainBlock>();
        w.RegisterComponent<Fdp.Toolkit.Terrain.TerrainFeature>();
        var layer = new EmptyScenarioHintLayer(() => w);
        Assert.True(layer.Shows());
        var b = w.CreateEntity(); w.AddComponent(b, new Fdp.Toolkit.Terrain.TerrainBlock { Seed = 1 });
        Assert.False(layer.Shows());                                           // a block with no EntityInfo at all

        var w2 = WithTerrain();
        w2.RegisterComponent<Fdp.Toolkit.Terrain.TerrainFeature>();
        var layer2 = new EmptyScenarioHintLayer(() => w2);
        Assert.True(layer2.Shows());
        var f = w2.CreateEntity(); w2.AddComponent(f, new Fdp.Toolkit.Terrain.TerrainFeature { Seed = 1 });
        Assert.False(layer2.Shows());
    }

    [Fact]
    public void L12_ASteadyCheckAllocatesNothing()
    {
        var w = WithTerrain();
        var layer = new EmptyScenarioHintLayer(() => w);
        layer.Shows(); layer.Shows();                                          // the query is built once
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) layer.Shows();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
