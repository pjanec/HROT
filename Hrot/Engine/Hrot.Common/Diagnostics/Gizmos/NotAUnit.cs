using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-1165</c> (civilian U-4, docs/DESIGN_Civilians_And_Crowd_Control.md §8.8 U4-f) — the ONE answer to "is this entity a thing
    /// on the map, or something drawn by its own layer?": the map CAMERA (the view itself, R-260), a replicated ROUND (its projector draws
    /// it, R-271) and a gas or smoke CLOUD (<c>EffectLayer</c> draws it, CE-3219) get no symbol, anchor, pick box, label or heading arrow.
    /// </summary>
    public static class NotAUnit
    {
        /// <summary>True when <paramref name="entity"/> is drawn by its own layer, never as a unit.</summary>
        public static bool Is(ISimulationView view, Entity entity)
        {
            if (Has<Fdp.Toolkit.Combat.AreaEffectCloud>(view, entity) || Has<LightweightRound>(view, entity)) return true;
            // ⭐ CE-4624 (live check, 2026-11-16) — a MINEFIELD / its strikes / its knowledge record: MinefieldGizmo draws the field; the field entity is no unit (it drew a generic 2-D box at its anchor)
            if (Has<Fdp.Toolkit.Terrain.Minefield>(view, entity) || Has<Fdp.Toolkit.Terrain.MinefieldStrikes>(view, entity) || Has<Fdp.Toolkit.Terrain.MinefieldKnowledge>(view, entity)) return true;
            if (Has<Fdp.Toolkit.Motion.AirArena>(view, entity)) return true;   // ⭐ CE-4647 — an air ARENA: AirArenaProjector draws it, it is no unit
            return Has<TkbIdentity>(view, entity) && Hrot.Core.Tkb.MapCameraTkb.Is(view.GetComponentRO<TkbIdentity>(entity).TkbType);
        }

        private static bool Has<T>(ISimulationView view, Entity entity) where T : unmanaged
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(entity);
    }
}
