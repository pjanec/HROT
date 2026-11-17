using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Tkb;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ CE-8610 (docs/UX/DESIGN_Unit_Symbols.md P-1…P-4, R-8602) — what each switch draws for a UNIT, read from the primitives the gizmos emit:
/// with Unit cards on, the symbol is the card's left cell (row 0, the card's layer, the derived code in the side's frame); the NATO row off ⇒ nothing; the
/// standalone symbol is an anchored 2-D-only icon on the NATO layer, drawn by the projector of the unit's echelon band only, the squad one only while no card
/// shows it; a selected unit adds its subordinate units'; a man never gets one; the bands tile the zoom with no gap; 0 B steady.
/// 🔴 Red-proof (compiles): <c>BandOf</c> answering 0 for a platoon ⇒ the platoon projector draws nothing ⇒ red.
/// </summary>
public sealed class UnitSymbolGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly Entity _platoon, _squadA, _squadB, _man;

    public UnitSymbolGizmoTests()
    {
        _w.RegisterComponent<UnitEntity>(); _w.RegisterComponent<NetworkIdentity>(); _w.RegisterComponent<TkbIdentity>(); _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<SelectionState>(); _w.RegisterComponent<UnitRoster>();
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        _w.SetSingletonManaged<ITkbDatabase>(db);
        Entity Make(long type, int net, ForceId side, bool unit)
        {
            var e = _w.CreateEntity();
            if (unit) _w.AddComponent(e, new UnitEntity());
            _w.AddComponent(e, new NetworkIdentity { Value = net });
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            _w.AddComponent(e, new EntityInfo { Name = new FixedString64("u" + net), ForceId = side });
            return e;
        }
        _platoon = Make(TkbEntityTypes.Unit_InfantryPlatoon, 10, ForceId.Friend, true);
        _squadA = Make(TkbEntityTypes.Unit_InfantrySquad, 11, ForceId.Friend, true);
        _squadB = Make(TkbEntityTypes.Unit_InfantrySquad, 12, ForceId.Friend, true);
        _man = Make(TkbEntityTypes.Infantry_Rifleman, 13, ForceId.Friend, true);   // ⚠ even flagged a unit: a man's type has no symbol
        var roster = new UnitRoster { Count = 2 };
        roster.SubordinateEntities[0] = _squadA; roster.SubordinateEntities[1] = _squadB;
        _w.AddComponent(_platoon, roster);
        _w.AddComponent(_platoon, new SelectionState { IsSelected = true });
    }

    public void Dispose() => _w.Dispose();

    private static LayerMask256 Mask(params byte[] off) { var m = new LayerMask256(); for (int i = 0; i < 256; i++) if (Array.IndexOf(off, (byte)i) < 0) m.SetBit(i); return m; }

    private DebugPrimitive[] Run(IStatelessGizmo g, Entity e, LayerMask256 mask)
    {
        var b = new DebugPrimitiveBuffer(256);
        b.SetActiveLayers(mask);
        g.Draw(_w, e, b);
        return b.GetFrame().ToArray().Where(p => p.Shape == DebugPrimitiveShape.Icon).ToArray();
    }

    [Fact]
    public void CE8610_WithUnitCards_TheSymbolIsTheCardsLeftCell_AndTheNatoRowTurnsItOff()
    {
        var g = new UnitSymbolCardGizmo();
        var cell = Assert.Single(Run(g, _platoon, Mask()));
        Assert.Equal(CoordinateSpace.EntityCard, cell.Space);
        Assert.Equal(0, cell.ZIndex);                                         // row 0 = card-wide: the left cell
        Assert.Equal(DebugTraceLayers.UnitCards, cell.DebugLayer);
        Assert.Equal(10, cell.AnchorIndex);
        Assert.Equal("SFGPUCI----D---", cell.TextContent.ToString());        // friend infantry platoon, derived (CE-8609)
        Assert.Empty(Run(g, _platoon, Mask(DebugTraceLayers.UnitSymbols)));   // the NATO row off
        Assert.Empty(Run(g, _platoon, Mask(DebugTraceLayers.UnitCards)));     // no card, no cell
        Assert.Empty(Run(g, _man, Mask()));                                   // never a man's
    }

    [Fact]
    public void CE8610_TheSymbolAlone_IsDrawnByItsEchelonsBandOnly_TheSquadsOnlyWhenNoCardShowsIt()
    {
        var platoonAlone = Assert.Single(Run(new UnitSymbolPlatoonGizmo(), _platoon, Mask()));
        Assert.Equal(CoordinateSpace.EntityLocal, platoonAlone.Space);       // anchored: it follows the unit
        Assert.Equal(10, platoonAlone.AnchorIndex);
        Assert.Equal(DebugTraceLayers.UnitSymbols, platoonAlone.DebugLayer);
        Assert.Equal(PipelineTarget.Map2D | PipelineTarget.FlatOnly, platoonAlone.TargetView);   // P-4: never in 3-D
        Assert.Empty(Run(new UnitSymbolCompanyGizmo(), _platoon, Mask()));  // another band's projector
        Assert.Empty(Run(new UnitSymbolSquadGizmo(), _platoon, Mask(DebugTraceLayers.UnitCards)));

        Assert.Empty(Run(new UnitSymbolSquadGizmo(), _squadA, Mask()));                         // cards on: the card shows it
        Assert.Single(Run(new UnitSymbolSquadGizmo(), _squadA, Mask(DebugTraceLayers.UnitCards)));
        Assert.Single(Run(new UnitSymbolSquadPastCardsGizmo(), _squadA, Mask()));               // past the cards' zoom
        Assert.Empty(Run(new UnitSymbolSquadPastCardsGizmo(), _squadA, Mask(DebugTraceLayers.UnitCards)));
        Assert.Empty(Run(new UnitSymbolPlatoonGizmo(), _platoon, Mask(DebugTraceLayers.UnitSymbols)));

        var children = Run(new UnitSymbolSelectedChildrenGizmo(), _platoon, Mask());           // a selected unit: its squads too
        Assert.Equal(new[] { 11, 12 }, children.Select(p => p.AnchorIndex).ToArray());
        Assert.All(children, p => Assert.Equal("SFGPUCI----B---", p.TextContent.ToString()));
    }

    [Fact]
    public void CE8610_TheZoomBands_TileTheScale_WithNoGapAndNoOverlap()
    {
        static (float Min, float Max) Band(Type t) { var a = t.GetCustomAttribute<GizmoProjectorAttribute>()!; return (a.MinMetresPerPixel, a.MaxMetresPerPixel); }
        Assert.Equal((0f, UnitSymbols.SquadMaxMpp), Band(typeof(UnitSymbolSquadGizmo)));
        Assert.Equal((CardZoom.FrameMaxMetresPerPixel, UnitSymbols.SquadMaxMpp), Band(typeof(UnitSymbolSquadPastCardsGizmo)));
        Assert.Equal(Band(typeof(UnitSymbolSquadGizmo)).Max, Band(typeof(UnitSymbolPlatoonGizmo)).Min);
        Assert.Equal(Band(typeof(UnitSymbolPlatoonGizmo)).Max, Band(typeof(UnitSymbolCompanyGizmo)).Min);
        Assert.Equal(Band(typeof(UnitSymbolCompanyGizmo)).Max, Band(typeof(UnitSymbolBattalionGizmo)).Min);
        Assert.Equal(0f, Band(typeof(UnitSymbolBattalionGizmo)).Max);
        Assert.Equal(CardZoom.FrameMaxMetresPerPixel, Band(typeof(UnitSymbolCardGizmo)).Max);   // the cell lives exactly as long as the card frame
    }

    [Fact]
    public void CE8610_ASteadyFrame_AllocatesNothing()
    {
        var b = new DebugPrimitiveBuffer(1024);
        b.SetActiveLayers(Mask());
        var card = new UnitSymbolCardGizmo(); var alone = new UnitSymbolPlatoonGizmo(); var kids = new UnitSymbolSelectedChildrenGizmo();
        for (int k = 0; k < 3; k++) { b.Clear(); card.Draw(_w, _platoon, b); alone.Draw(_w, _platoon, b); kids.Draw(_w, _platoon, b); }
        b.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        card.Draw(_w, _platoon, b); alone.Draw(_w, _platoon, b); kids.Draw(_w, _platoon, b);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
