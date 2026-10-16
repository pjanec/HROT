using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐ <c>CE-1163</c> (civilian U-2; <c>docs/UX/UX_Feature_Entity_Symbology.md</c> §3.1 / §3.2 — closes <c>CE-125</c>'s defect A) — the
    /// ONE colour of an entity on the map: its <see cref="ResolvedStyle"/> tint where a host resolved one (the IG), else its LIVE side
    /// (<see cref="EntityInfo.ForceId"/>) from the one palette, <see cref="ResolvedStyleConstants"/>, else <see cref="NoSide"/> (the cyan the
    /// outline always drew). Read every frame, so a side that flips at runtime (a civilian turning hostile, R-3013 CV-4) redraws at once.
    /// </summary>
    public static class AffiliationColors
    {
        /// <summary>The colour of an entity with no side known — today's cyan, now only the fallback (Symbology §3.1).</summary>
        public static readonly Rgba32 NoSide = new(100, 220, 255, 255);

        /// <summary>A side's colour from the one palette (<see cref="ResolvedStyleConstants"/>: Friend blue, Hostile red, Neutral green).</summary>
        public static Rgba32 Of(ForceId side) => side switch
        {
            ForceId.Friend => new Rgba32(ResolvedStyleConstants.FriendTintR, ResolvedStyleConstants.FriendTintG, ResolvedStyleConstants.FriendTintB, ResolvedStyleConstants.FriendTintA),
            ForceId.Hostile => new Rgba32(ResolvedStyleConstants.HostileTintR, ResolvedStyleConstants.HostileTintG, ResolvedStyleConstants.HostileTintB, ResolvedStyleConstants.HostileTintA),
            _ => new Rgba32(ResolvedStyleConstants.NeutralTintR, ResolvedStyleConstants.NeutralTintG, ResolvedStyleConstants.NeutralTintB, ResolvedStyleConstants.NeutralTintA),
        };

        /// <summary>The colour <paramref name="entity"/> draws in on the map (see the class summary for the order).</summary>
        public static Rgba32 For(ISimulationView view, Entity entity)
        {
            if (Registered<ResolvedStyle>(view) && view.HasComponent<ResolvedStyle>(entity))
            {
                var tint = view.GetComponentRO<ResolvedStyle>(entity).Tint;
                if (tint.A != 0) return new Rgba32(tint.R, tint.G, tint.B, tint.A);
            }
            if (Registered<EntityInfo>(view) && view.HasComponent<EntityInfo>(entity))
                return Of(view.GetComponentRO<EntityInfo>(entity).ForceId);
            return NoSide;
        }

        private static bool Registered<T>(ISimulationView view) where T : unmanaged
            => view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>();
    }
}
