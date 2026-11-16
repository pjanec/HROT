using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Actions;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Selection;
using Xunit;

namespace Hrot.Presentation.Tests.Selection;

/// <summary>
/// ⭐ CE-5006 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-17 / TE-18; R-5036 … R-5038) — PLACED terrain is selectable only while the Terrain toolbar is
/// open: with it closed a block / feature / placed obstacle is not selected, not hovered, gets no menu, and a block draws no authoring outline;
/// units are untouched, and deselecting always works. 🔴 Red-proofs (compiling mutations): drop the guard in <c>EcsSelectionState.Write</c> ⇒
/// <c>Closed_*</c> red on selection; drop the one in <c>MapMenuActions.JsonFor</c> ⇒ red on the menu; drop the one in <c>TerrainBlockGizmo</c> ⇒ red on the outline.
/// </summary>
public sealed class TerrainLockTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private static readonly Guid GuidA = Guid.Parse("b2000000-0000-4000-8000-000000000002");

    public TerrainLockTests()
    {
        _w.RegisterComponent<SimTransform>(); _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<TerrainBlock>(); _w.RegisterComponent<TerrainFeature>();
        _w.RegisterComponent<StaticObstacle>(); _w.RegisterComponent<EntityGuid>(); _w.RegisterComponent<SelectionState>();
        _w.RegisterManagedComponent<LiveTerrainBakeInfo>();
        _w.SetSingletonManaged(LiveTerrainBakeInfo.From(1, new[] { new OverlayPart(GuidA + "/0", 1, new Vector2(80, 90), new Vector2(140, 130)) }, null));
    }

    public void Dispose() => _w.Dispose();

    private Entity Make(int net, Action<Entity> with)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(100, 100, 0), Rotation = Quaternion.Identity });
        _w.AddComponent(e, new NetworkIdentity(net));
        with(e);
        return e;
    }

    private Entity Block() => Make(77, e => { _w.AddComponent(e, new TerrainBlock { Recipe = new Fdp.Core.FixedString32("hamlet"), Seed = 1, Fit = TerrainBlock.FitAll }); _w.AddComponent(e, new EntityGuid(GuidA)); });
    private Entity Tree() => Make(78, e => _w.AddComponent(e, new TerrainFeature { Kind = TerrainFeature.Tree }));
    private Entity Sandbags() => Make(79, e => _w.AddComponent(e, new StaticObstacle()));
    private Entity Unit() => Make(80, _ => { });

    private int OutlinePrimitives(Entity block)
    {
        var b = new DebugPrimitiveBuffer();
        new TerrainBlockGizmo().Draw(_w, block, b);
        return b.GetFrame().ToArray().Length;
    }

    [Fact]
    public void Closed_APlacedBlockTreeOrObstacle_IsNotSelectedHoveredOrGivenAMenu_AndTheBlockDrawsNoOutline()
    {
        var sel = new EcsSelectionState(_w);
        foreach (var e in new[] { Block(), Tree(), Sandbags() })
        {
            sel.PrimarySelected = e;
            Assert.False(sel.IsSelected(e));
            sel.HoveredEntity = e;
            Assert.Null(sel.HoveredEntity);
            Assert.Equal("[]", MapMenuActions.JsonFor(_w, e));
        }
        Assert.Equal(0, OutlinePrimitives(Block()));
    }

    [Fact]
    public void Open_TheSameThings_AreSelectableHoverableWithTheirMenu_AndTheBlockDrawsItsOutline()
    {
        TerrainLock.SetOpen(_w, true);
        var sel = new EcsSelectionState(_w);
        var block = Block();
        sel.PrimarySelected = block;
        Assert.True(sel.IsSelected(block));
        sel.HoveredEntity = block;
        Assert.Equal(block, sel.HoveredEntity);
        Assert.NotEqual("[]", MapMenuActions.JsonFor(_w, block));
        Assert.True(OutlinePrimitives(block) > 0);
    }

    [Fact]
    public void Closed_AUnitIsUntouched_AndAnObstacleKeepsItsSymbol_ButAFeatureOrBlockIsAnAuthoringMark()
    {
        var sel = new EcsSelectionState(_w);
        var unit = Unit();
        sel.PrimarySelected = unit;
        Assert.True(sel.IsSelected(unit));
        Assert.False(TerrainLock.IsLocked(_w, unit));
        Assert.False(TerrainLock.IsAuthoringMark(_w, Sandbags()));   // an order may still target it: its symbol stays
        Assert.True(TerrainLock.IsAuthoringMark(_w, Tree()));
        Assert.True(TerrainLock.IsAuthoringMark(_w, Block()));
    }

    [Fact]
    public void ClosingTheToolbar_KeepsDeselectingPossible()
    {
        TerrainLock.SetOpen(_w, true);
        var sel = new EcsSelectionState(_w);
        var tree = Tree();
        sel.PrimarySelected = tree;
        Assert.True(sel.IsSelected(tree));
        TerrainLock.SetOpen(_w, false);
        sel.PrimarySelected = null;
        Assert.False(sel.IsSelected(tree));
    }
}
