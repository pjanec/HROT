using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-1169</c> (docs/DESIGN_People_Display.md §5 U8) — the exercise-control layer <b>"Concealed (ExCon)"</b>: an item carried in a
    /// man's HIDDEN station (a worn charge, a concealed pistol) is never drawn on the figure (Civilians AW-13 / AW-15, per-observer
    /// identification); for the INSTRUCTOR this layer, off by default, outlines it at his hip — a dashed magenta ring — and names it on his
    /// card. Reads only the replicated passenger list (<see cref="CarryStations.IsConcealed"/>), so every node draws it. Auto-discovered on
    /// every map host (<c>GizmoReflectionRegistrar</c>).
    /// </summary>
    [GizmoProjector(typeof(IsEmbarkedTag))]
    public sealed class ConcealedItemGizmo : IStatelessGizmo
    {
        /// <summary>Magenta: an exercise-control mark, never a game colour.</summary>
        public static readonly Rgba32 Colour = new(235, 70, 235, 235);

        /// <summary>The ring's centre above his feet and its radius (m).</summary>
        public const float HipZ = 1.0f, Radius = 0.35f;

        /// <summary>The item's row on the carrier's card: below the mark (<see cref="OverheadMarkGizmo.Row"/>).</summary>
        public const byte Row = 46;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Concealed) || view is not EntityRepository repo) return;
            if (!Of(repo, entity, out var carrier, out var at)) return;
            draw.DrawSphere(at, Radius, Colour, thickness: 2f, sizeMode: SizeMode.WorldMeters, layer: DebugTraceLayers.Concealed,
                style: LineStyle.Dashed);
            if (repo.IsComponentTypeRegistered<NetworkIdentity>() && repo.HasComponent<NetworkIdentity>(carrier)
                && repo.GetComponentRO<NetworkIdentity>(carrier).Value is var net and not 0)
                draw.Card(net, DebugTraceLayers.Concealed).Row(Row).Text("hidden: " + NameOf(repo, entity), Colour, 12f);
        }

        /// <summary>Where to outline <paramref name="thing"/>: carried in a CONCEALED station (only a person's carry stations have one). Public
        /// for the rail.</summary>
        public static bool Of(EntityRepository repo, Entity thing, out Entity carrier, out Vector3 at)
        {
            at = default;
            carrier = Entity.Null;
            if (!CarryStations.IsConcealed(repo, thing)) return false;
            CarryStations.SeatOf(repo, thing, out carrier);
            if (carrier.IsNull || !repo.IsAlive(carrier) || !repo.HasComponent<SimTransform>(carrier))   // a HIDDEN station is a person's by type
                return false;
            at = repo.GetComponentRO<SimTransform>(carrier).Position + new Vector3(0f, 0f, HipZ);
            return true;
        }

        private static string NameOf(EntityRepository repo, Entity thing)
        {
            if (repo.IsComponentTypeRegistered<TkbIdentity>() && repo.HasComponent<TkbIdentity>(thing)
                && repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()
                && repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } tkb
                && tkb.TryGetByType(repo.GetComponentRO<TkbIdentity>(thing).TkbType, out var t) && !string.IsNullOrEmpty(t.Name))
                return t.Name;
            return "item";
        }
    }
}
