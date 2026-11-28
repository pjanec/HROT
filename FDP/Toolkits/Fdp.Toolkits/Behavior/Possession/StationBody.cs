using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Behavior.Possession
{
    /// <summary>
    /// ⭐⭐ Capability components C3 (docs/DESIGN_Capability_Components.md §4.3, CC-10) — THE ONE answer to "which entity is the station's BODY".
    /// <see cref="PossessedBy"/> is CONTROL ("a station's input drives this entity"); the body is a MODE of that control (<see cref="IsBodyMode"/>),
    /// so every reader that means the body asks here instead of walking the holds itself. Two questions, two answers:
    /// <list type="bullet">
    /// <item><see cref="Driven"/> — what the station DRIVES (the camera, the HUD, the hand-over): the newest-<see cref="PossessedBy.Epoch"/> body
    /// hold that is not <see cref="PossessionMode.Riding"/> (a vehicle while he drives it, else the man).</item>
    /// <item><see cref="Own"/> — the human's OWN body (his side, his briefing, who he is): the <see cref="PossessionMode.Riding"/> soldier, else
    /// <see cref="Driven"/>.</item>
    /// </list>
    /// <para>📐 Before C3 these were computed six ways (<c>DriveHandOver.HeldHere</c>, <c>StationClaims.TrySideOf</c>, <c>EntityOrders.ReleaseOtherFps</c>,
    /// <c>PlayerBriefing</c>, <c>DebriefWindows</c>, <c>PossessionSystem.ReleaseKilled</c>) — and the briefing / debrief took the FIRST hold in the world,
    /// whichever station held it (CC-13).</para>
    /// </summary>
    public static class StationBody
    {
        /// <summary>Is <paramref name="mode"/> a hold of the station's body? Today EVERY mode is: <see cref="PossessionMode.Fps"/>, <see cref="PossessionMode.Riding"/> and
        /// <see cref="PossessionMode.None"/> (a hold written without a mode — <c>Possess</c> stores it as Fps; the pre-C3 readers treated it as driven). A later
        /// non-body mode (an operated device) is the one this test exists to exclude.</summary>
        public static bool IsBodyMode(PossessionMode mode) => mode == PossessionMode.None || mode == PossessionMode.Fps || mode == PossessionMode.Riding;

        // ⭐ R-2084 — 0 B per call: the holds query is built once per world (per thread — the brain phase may ask from a worker)
        [System.ThreadStatic] private static EntityRepository? _queryFor;
        [System.ThreadStatic] private static EntityQuery? _holds;
        private static EntityQuery Holds(EntityRepository w)
        {
            if (_holds == null || !ReferenceEquals(w, _queryFor)) { _holds = w.Query().With<PossessedBy>().Build(); _queryFor = w; }
            return _holds;
        }

        /// <summary>
        /// The station whose holds count as "this station's" for <paramref name="entity"/> — <paramref name="station"/> when given, else THIS node's
        /// (the entity's <see cref="NetworkAuthority.LocalNodeId"/>, 0 without one). ⭐ The one rule <c>EntityOrders.StationFor</c> delegates to.
        /// </summary>
        public static int StationOf(EntityRepository w, Entity entity, int? station)
            => station ?? (w.IsComponentTypeRegistered<NetworkAuthority>() && w.HasComponent<NetworkAuthority>(entity)
                ? w.GetComponentRO<NetworkAuthority>(entity).LocalNodeId : 0);

        /// <summary>What the station DRIVES (see the class summary), or null. <paramref name="liveOnly"/> skips a killed unit (<see cref="PossessionSystem.Alive"/>).</summary>
        public static Entity Driven(EntityRepository w, int? station, bool liveOnly = false)
        {
            if (!w.IsComponentTypeRegistered<PossessedBy>()) return Entity.Null;
            Entity best = Entity.Null;
            uint bestEpoch = 0;
            foreach (var unit in Holds(w))
            {
                if (!w.IsAlive(unit) || (liveOnly && !PossessionSystem.Alive(w, unit))) continue;
                var held = w.GetComponentRO<PossessedBy>(unit);
                if (!IsBodyMode(held.Mode) || held.Mode == PossessionMode.Riding || held.StationNodeId != StationOf(w, unit, station)) continue;
                if (best.IsNull || held.Epoch > bestEpoch) { best = unit; bestEpoch = held.Epoch; }
            }
            return best;
        }

        /// <summary>The human's OWN body (see the class summary), or null.</summary>
        public static Entity Own(EntityRepository w, int? station, bool liveOnly = false)
        {
            if (!w.IsComponentTypeRegistered<PossessedBy>()) return Entity.Null;
            foreach (var unit in Holds(w))
            {
                if (!w.IsAlive(unit) || (liveOnly && !PossessionSystem.Alive(w, unit))) continue;
                var held = w.GetComponentRO<PossessedBy>(unit);
                if (held.Mode == PossessionMode.Riding && held.StationNodeId == StationOf(w, unit, station)) return unit;
            }
            return Driven(w, station, liveOnly);
        }
    }
}
