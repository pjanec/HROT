using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Diagnostics.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ CE-8607 (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-11; docs/UX/DESIGN_Unit_Symbols.md P-5) — a UNIT's card: the strength bar in row 10 in the
/// SITREP colour with ticks at 70 / 90 %, a thin ammo bar in row 13, and ONE info line in row 40 (present/authorized in the strength colour · what it does in teal ·
/// the worst alarm) — for a PHYSICAL squad (from its roster) and a STOCHASTIC platoon (from its slot table, a violet ~ first); all on the Unit cards layer; 0 B.
/// 🔴 Red-proof (compiles): <c>WorstAlarm</c> ignoring broken men ⇒ no "BROKEN 1" ⇒ red.
/// </summary>
public sealed class UnitCardGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();

    public UnitCardGizmoTests()
    {
        _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<EntityInfo>(); _w.RegisterComponent<UnitEntity>(); _w.RegisterComponent<UnitRoster>();
        _w.RegisterComponent<Health>(); _w.RegisterComponent<InjuryThresholds>(); _w.RegisterComponent<WeaponAmmo>(); _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.Stress>();
        _w.RegisterComponent<AggregateUnit>(); _w.RegisterComponent<UnitSlotTable>(); _w.RegisterComponent<SimTransform>();
    }

    public void Dispose() => _w.Dispose();

    private DebugPrimitive[] Card(int net, byte row) => _draw.GetFrame().ToArray().Where(p => p.Space == CoordinateSpace.EntityCard && p.AnchorIndex == net && p.ZIndex == row).ToArray();
    private string[] Texts(int net, byte row) => Card(net, row).Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToArray();

    private Entity Squad(int net, int men, int dead, bool oneBroken)
    {
        var sq = _w.CreateEntity();
        _w.AddComponent(sq, new NetworkIdentity(net)); _w.AddComponent(sq, new EntityInfo { Name = new FixedString64("1 Sqn"), ForceId = ForceId.Friend });
        _w.AddComponent(sq, new UnitEntity()); _w.AddComponent(sq, new UnitRoster()); _w.AddComponent(sq, new SimTransform());
        for (int i = 0; i < men; i++)
        {
            var m = _w.CreateEntity();
            _w.AddComponent(m, new Health { Current = i < dead ? 0f : 100f, Max = 100f });
            _w.AddComponent(m, new WeaponAmmo { Count = 1, Ammo0 = 90, Max0 = 120 });
            if (oneBroken && i == men - 1) _w.AddComponent(m, new Fdp.Toolkit.Behavior.Components.Stress { Level = 1f, Broken = 1 });
            UnitRoster.Add(ref _w.GetComponentRW<UnitRoster>(sq), m);
        }
        return sq;
    }

    [Fact]
    public void CE8607_APhysicalSquadsCard_StrengthBarTicksAmmoBar_AndOneInfoLine()
    {
        var sq = Squad(70, men: 9, dead: 2, oneBroken: true);
        new UnitCardGizmo().Draw(_w, sq, _draw);
        UnitCardFacts.InfoRow(_draw.Card(70, DebugTraceLayers.Labels, unitCard: true), _w, sq, "Defend");
        var bar = Card(70, EntityCardRows.UnitStrengthBar);
        Assert.Contains(bar, p => p.Color.Equals(UnitCardFacts.Amber));          // 7 of 9 in the fight = 78 % — AMBER (≥ 70 %, < 90 %)
        Assert.True(bar.Length >= 4, "the bar (back + fill) and the two threshold ticks");
        Assert.All(_draw.GetFrame().ToArray().Where(p => p.Space == CoordinateSpace.EntityCard), p => Assert.Equal(DebugTraceLayers.UnitCards, p.DebugLayer));
        Assert.NotEmpty(Card(70, EntityCardRows.UnitAmmoBar));                 // the men's rounds: 90 / 120 = 75 % — green
        Assert.Equal(new[] { "7/9", "· Defend", "BROKEN 1" }, Texts(70, EntityCardRows.UnitInfo));
    }

    [Fact]
    public void CE8607_AStochasticPlatoonsCard_ReadsItsSlotTable_AViolettTildeFirst_SuppressedAlarm_0B()
    {
        var plt = _w.CreateEntity();
        _w.AddComponent(plt, new NetworkIdentity(80)); _w.AddComponent(plt, new EntityInfo { Name = new FixedString64("2 Plt"), ForceId = ForceId.Friend });
        _w.AddComponent(plt, new UnitEntity()); _w.AddComponent(plt, new SimTransform());
        _w.AddComponent(plt, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Echelon = 1, SuppressedShare = 0.35f });
        var t = new UnitSlotTable { Count = 30 };
        for (int i = 0; i < 30; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)(i < 4 ? 0f : 25f) };
        _w.AddComponent(plt, t);
        var g = new UnitCardGizmo();
        g.Draw(_w, plt, _draw);
        UnitCardFacts.InfoRow(_draw.Card(80, DebugTraceLayers.Labels, unitCard: true), _w, plt, "StochasticUnitDefend");
        Assert.Contains(Card(80, EntityCardRows.UnitStrengthBar), p => p.Color.Equals(UnitCardFacts.Amber));   // 26/30 = 87 % — AMBER
        Assert.Empty(Card(80, EntityCardRows.UnitAmmoBar));                                                 // a stochastic unit's rounds have no capacity — no bar
        Assert.Equal(new[] { "~", "26/30", "· StochasticUnitDefe", "SUPP 35%" }, Texts(80, EntityCardRows.UnitInfo));
        Assert.Contains(Card(80, EntityCardRows.UnitInfo), p => p.TextContent.ToString() == "~" && p.Color.Equals(UnitCardFacts.Violet));

        // 0 B on a steady frame
        var card = _draw.Card(80, DebugTraceLayers.Labels, unitCard: true);
        for (int k = 0; k < 3; k++) { g.Draw(_w, plt, _draw); UnitCardFacts.InfoRow(card, _w, plt, "Defend"); _draw.Clear(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 20; k++) { g.Draw(_w, plt, _draw); UnitCardFacts.InfoRow(card, _w, plt, "Defend"); _draw.Clear(); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
