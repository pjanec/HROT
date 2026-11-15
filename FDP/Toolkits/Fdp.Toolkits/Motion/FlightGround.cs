using Fdp.Core;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐ <c>CE-2436</c> AU-5 (DESIGN_Assault_Under_Fire §9.6) — the ONE answer to "is this aircraft on the ground?": its motion model put it down
    /// (<see cref="FlightState.Airborne"/> 0), or it stands within <see cref="GroundClearanceM"/> of the ground and does not move (a drone PLACED
    /// beside its operator reads <c>Airborne</c> 1 — the translator's default — until a flying order runs its model; measured live).
    /// </summary>
    public static class FlightGround
    {
        public const float GroundClearanceM = 1f;
        public const float StillMps = 0.5f;

        public static bool OnTheGround(EntityRepository repo, Entity e)
        {
            if (repo.IsComponentTypeRegistered<FlightState>() && repo.HasComponent<FlightState>(e) && repo.GetComponentRO<FlightState>(e).Airborne == 0)
                return true;
            if (!repo.HasComponent<SimTransform>(e)) return false;
            var at = repo.GetComponentRO<SimTransform>(e).Position;
            var world = Fdp.Toolkit.World.WorldQuery.Of(repo);
            float ground = world?.GroundHeightAt(at.X, at.Y) ?? 0f;
            if (at.Z - ground > GroundClearanceM) return false;
            return !Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, e, out _, out var v) || v.Length() < StillMps;
        }
    }
}
