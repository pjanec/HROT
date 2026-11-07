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
