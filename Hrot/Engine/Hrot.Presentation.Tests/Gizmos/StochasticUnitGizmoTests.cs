using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ CE-8603 (exp/agg-ui, docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-2, UI-5, UI-6) — a STOCHASTIC unit draws its footprint (a side-coloured
/// translucent fill as two triangles, four DASHED edges at the rectangle's corners, an arrow off the front), a frame in the 2525 side colour with the echelon marks,
/// a label row coloured by strength, and a loss mark for 4 s when its present strength falls; a PHYSICAL unit draws nothing here; a steady frame allocates nothing.
/// 🔴 Red-proof (compiles): the resolution guard inverted (draw only physical units) ⇒ the stochastic platoon draws nothing ⇒ red.
/// </summary>
public sealed class StochasticUnitGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public StochasticUnitGizmoTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<AggregateUnit>();
        _w.RegisterComponent<UnitSlotTable>();
        _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 100.0 });
    }

    public void Dispose() => _w.Dispose();

    private Entity Unit(string name, ForceId side, AggregateResolution resolution, int men, int down)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(100, 50, 0) });
        _w.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        _w.AddComponent(e, new AggregateUnit { Resolution = resolution, Echelon = 1, Formation = UnitFormation.Line, Width = 200f, Depth = 80f, Heading = new Bearing(90f) });
        var t = new UnitSlotTable { Count = (byte)men };
        for (int i = 0; i < men; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)(i < down ? 0f : 25f) };
        _w.AddComponent(e, t);
        return e;
    }

    private DebugPrimitive[] Frame() => _draw.GetFrame().ToArray();
    private string[] Texts() => Frame().Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToArray();

    [Fact]
    public void CE8603_AStochasticPlatoon_DrawsItsFootprintFrameAndStrength_APhysicalOneNothing()
    {
        var plt = Unit("2 Plt", ForceId.Friend, AggregateResolution.Stochastic, men: 30, down: 4);
        var g = new StochasticUnitGizmo();
        g.Draw(_w, plt, _draw);
        var f = Frame();
        Assert.Equal(2, f.Count(p => p.Shape == DebugPrimitiveShape.FilledTriangle));
        var edges = f.Where(p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Dashed).ToArray();
        Assert.Equal(4, edges.Length);
        // heading east, 200 wide × 80 deep around (100, 50): x spans 60..140, y spans -50..150
        Assert.All(edges, p => { Assert.InRange(p.LineStart.X, 59.9f, 140.1f); Assert.InRange(p.LineStart.Y, -50.1f, 150.1f); });
        Assert.Single(f, p => p.Shape == DebugPrimitiveShape.Box2D && p.SizeMode == SizeMode.ScreenPixels && p.FillColor.Equals(StochasticUnitGizmo.FriendFrame));
        var texts = Texts();
        Assert.Contains("···", texts);                    // a platoon's echelon marks
        Assert.DoesNotContain(texts, t => t.Contains("26/30", StringComparison.Ordinal));   // ⭐ CE-8607 — the strength is the CARD's (UnitCardGizmo), not loose map text

        _draw.Clear();
        var phys = Unit("1 Sqn", ForceId.Friend, AggregateResolution.Physical, men: 9, down: 0);
        g.Draw(_w, phys, _draw);
        Assert.Empty(Frame());                            // a physical unit: its men are the picture
    }

    [Fact]
    public void CE8603_ALoss_ShowsAMinusNMark_ForFourSeconds_AndASteadyFrameAllocatesNothing()
    {
        var plt = Unit("3 Coy", ForceId.Hostile, AggregateResolution.Stochastic, men: 20, down: 0);
        var g = new StochasticUnitGizmo();
        g.Draw(_w, plt, _draw);
        Assert.DoesNotContain(Texts(), t => t.StartsWith("-", StringComparison.Ordinal));

        ref var t = ref _w.GetComponentRW<UnitSlotTable>(plt);
        t.Slots[0].Health = (Half)0f; t.Slots[1].Health = (Half)0f; t.Slots[2].Health = (Half)0f;
        _draw.Clear(); g.Draw(_w, plt, _draw);
        Assert.Contains("-3", Texts());
        Assert.Contains(Frame(), p => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.Equals(StochasticUnitGizmo.HostileFrame));

        _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 103.0 });
        _draw.Clear(); g.Draw(_w, plt, _draw);
        Assert.Contains("-3", Texts());                   // still within 4 s
        _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 105.0 });
        _draw.Clear(); g.Draw(_w, plt, _draw);
        Assert.DoesNotContain("-3", Texts());             // gone after 4 s

        g.Draw(_w, plt, _draw); _draw.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) { g.Draw(_w, plt, _draw); _draw.Clear(); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
