using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using GizmoMap.Contracts.Symbology;
using Hrot.Core.Tkb;
using Hrot.Map.Common.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-8610</c> (docs/UX/DESIGN_Unit_Symbols.md P-1…P-4, R-8602; docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §6) — a UNIT's NATO symbol: its
    /// TYPE's derived symbol (<see cref="UnitSymbolDeriver"/>, CE-8609) in its side's frame. Physical and stochastic units alike (both keep their composite type);
    /// a man or a vehicle never has one (S-8). Reads the TKB database singleton — on a world without one (a bare test host) nothing is drawn. 0 B once derived.
    /// </summary>
    public static class UnitSymbols
    {
        /// <summary>The standalone symbol's frame height, in pixels (the card cell's is <see cref="CellHeightPx"/>).</summary>
        public const float AloneHeightPx = 20f, CellHeightPx = 22f;

        /// <summary>P-3's zoom bands (metres per pixel): a squad's symbol up to 1.5, a platoon's to 6, a company's to 25, then the battalions'.</summary>
        public const float SquadMaxMpp = 1.5f, PlatoonMaxMpp = 6f, CompanyMaxMpp = 25f;

        public static bool TryOf(ISimulationView view, Entity unit, out SymbolCode symbol)
        {
            symbol = default;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<TkbIdentity>() || !repo.HasComponent<TkbIdentity>(unit)
                || !repo.HasSingletonManaged<ITkbDatabase>() || repo.GetSingletonManaged<ITkbDatabase>() is not { } db) return false;
            if (!UnitSymbolDeriver.TryOfType(db, (long)repo.GetComponentRO<TkbIdentity>(unit).TkbType, out var derived)) return false;
            var side = repo.HasComponent<EntityInfo>(unit) ? repo.GetComponentRO<EntityInfo>(unit).ForceId : ForceId.Neutral;
            symbol = UnitSymbolDeriver.WithSide(derived, side);
            return true;
        }

        /// <summary>The band an echelon's standalone symbol is drawn in (P-3): 0 squad and smaller, 1 platoon, 2 company, 3 battalion and larger.</summary>
        public static int BandOf(SymbolEchelon e) => e switch
        {
            SymbolEchelon.Platoon => 1, SymbolEchelon.Company => 2,
            >= SymbolEchelon.Battalion => 3, _ => 0,
        };

        /// <summary>The symbol ALONE at the unit's point (2-D map only — P-4: in 3-D it lives only in the card), anchored to the unit so it follows it.</summary>
        public static void DrawAlone(IDebugDrawBuilder draw, int networkId, in SymbolCode symbol)
        {
            var p = default(DebugPrimitive);
            p.Shape = DebugPrimitiveShape.Icon;
            p.Space = CoordinateSpace.EntityLocal;
            p.AnchorIndex = networkId;
            p.TargetView = PipelineTarget.Map2D | PipelineTarget.FlatOnly;
            p.DebugLayer = DebugTraceLayers.UnitSymbols;
            p.Color = new Rgba32(255, 255, 255, 255);
            p.ThicknessU16 = (ushort)(AloneHeightPx * 10f);
            System.Span<char> code = stackalloc char[SymbolCode.MaxLength];
            int n = symbol.Format(code);
            p.TextContent = new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32(code.Slice(0, n));
            draw.EmitRaw(in p);
        }

        internal static bool CardsShow(IDebugDrawBuilder draw) => draw.IsLayerActive(DebugTraceLayers.UnitCards);
    }

    /// <summary>⭐ <c>CE-8610</c> (P-2, case A / E) — with the unit cards on, the symbol is the card's LEFT CELL (spanning its rows).</summary>
    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), typeof(TkbIdentity), MaxMetresPerPixel = CardZoom.FrameMaxMetresPerPixel)]
    public sealed class UnitSymbolCardGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.UnitSymbols)) return;
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity) || !UnitSymbols.TryOf(view, entity, out var symbol)) return;
            draw.Card(net, DebugTraceLayers.Labels, unitCard: true).Row(0).Symbol(symbol, UnitSymbols.CellHeightPx);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-8610</c> (P-2 / P-3, cases C / D / G) — the symbol ALONE at the unit's point when no card shows it, ONE echelon per zoom band. The band is the
    /// projector's zoom gate (<see cref="GizmoProjectorAttribute.MinMetresPerPixel"/> … <c>Max</c>), so a unit outside its band costs nothing.
    /// </summary>
    public abstract class UnitSymbolAloneGizmo : IStatelessGizmo
    {
        protected abstract int Band { get; }
        /// <summary>True ⇒ draws only while the unit cards are on (past their zoom); false ⇒ only while they are off.</summary>
        protected virtual bool? WhenCards => null;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.UnitSymbols)) return;
            if (WhenCards is { } cards && cards != UnitSymbols.CardsShow(draw)) return;
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0 || !Seeable.OnMap(view, entity) || !UnitSymbols.TryOf(view, entity, out var symbol) || UnitSymbols.BandOf(symbol.Echelon) != Band) return;
            UnitSymbols.DrawAlone(draw, net, symbol);
        }
    }

    /// <summary>Squads with the cards OFF: from the closest zoom to <see cref="UnitSymbols.SquadMaxMpp"/>.</summary>
    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), typeof(TkbIdentity), MaxMetresPerPixel = UnitSymbols.SquadMaxMpp)]
    public sealed class UnitSymbolSquadGizmo : UnitSymbolAloneGizmo { protected override int Band => 0; protected override bool? WhenCards => false; }

    /// <summary>Squads with the cards ON: only past the cards' zoom (the card shows the symbol closer in).</summary>
    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), typeof(TkbIdentity), MinMetresPerPixel = CardZoom.FrameMaxMetresPerPixel, MaxMetresPerPixel = UnitSymbols.SquadMaxMpp)]
    public sealed class UnitSymbolSquadPastCardsGizmo : UnitSymbolAloneGizmo { protected override int Band => 0; protected override bool? WhenCards => true; }

    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), typeof(TkbIdentity), MinMetresPerPixel = UnitSymbols.SquadMaxMpp, MaxMetresPerPixel = UnitSymbols.PlatoonMaxMpp)]
    public sealed class UnitSymbolPlatoonGizmo : UnitSymbolAloneGizmo { protected override int Band => 1; }

    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), typeof(TkbIdentity), MinMetresPerPixel = UnitSymbols.PlatoonMaxMpp, MaxMetresPerPixel = UnitSymbols.CompanyMaxMpp)]
    public sealed class UnitSymbolCompanyGizmo : UnitSymbolAloneGizmo { protected override int Band => 2; }

    [GizmoProjector(typeof(UnitEntity), typeof(NetworkIdentity), typeof(TkbIdentity), MinMetresPerPixel = UnitSymbols.CompanyMaxMpp)]
    public sealed class UnitSymbolBattalionGizmo : UnitSymbolAloneGizmo { protected override int Band => 3; }

    /// <summary>
    /// ⭐ <c>CE-8610</c> (P-3, case G) — a SELECTED unit also shows its subordinate units' symbols, one level down, past the cards' zoom (whatever the band).
    /// </summary>
    [GizmoProjector(typeof(SelectionState), typeof(UnitRoster), MinMetresPerPixel = CardZoom.FrameMaxMetresPerPixel)]
    public sealed class UnitSymbolSelectedChildrenGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.UnitSymbols) || !view.GetComponentRO<SelectionState>(entity).IsSelected) return;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(entity);
            for (int i = 0; i < roster.Count; i++)
            {
                var child = roster.SubordinateEntities[i];
                if (child.IsNull || !view.IsAlive(child) || !view.HasComponent<UnitEntity>(child) || !view.HasComponent<NetworkIdentity>(child)) continue;
                int net = view.GetComponentRO<NetworkIdentity>(child).Value;
                if (net != 0 && UnitSymbols.TryOf(view, child, out var symbol)) UnitSymbols.DrawAlone(draw, net, symbol);
            }
        }
    }
}
