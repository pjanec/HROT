using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>Q94 C</c> (R-3018, R-2037 — 📄 <c>docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md</c> §5.1) — WHICH SIDE of a
    /// door to walk to, decided on the node that moves the body (the door geometry lives there): <see cref="ApproachMetres"/> off the
    /// doorway's centre, on the side of the wall the mover stands on. Moved here from <c>DoorNodes.ApproachPoint</c> (it ran on the brain).
    /// ⭐ Called through <c>NavigationDestination.Of</c> — the one place a destination becomes a point.
    /// </summary>
    public static class DoorApproach
    {
        /// <summary>How far off the doorway centre (m, on the mover's side) the walk aims.</summary>
        public const float ApproachMetres = 1.0f;

        /// <summary>The near side of the door with network id <paramref name="doorNetworkId"/> for a mover at <paramref name="from"/>;
        /// false when that id is not a door on this node.</summary>
        public static bool TryPoint(EntityRepository repo, long doorNetworkId, Vector3 from, out Vector3 point)
        {
            point = default;
            var door = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(repo, doorNetworkId);
            if (door.IsNull || !repo.IsComponentTypeRegistered<DoorState>() || !repo.HasComponent<DoorState>(door)
                || !repo.HasComponent<SimTransform>(door))
                return false;
            point = Point(repo, door, new Vector2(from.X, from.Y));
            return true;
        }

        /// <summary>The point <see cref="ApproachMetres"/> off <paramref name="door"/>'s doorway centre, on <paramref name="from"/>'s side
        /// of its wall (by the terrain's door record); a door the terrain does not know: straight back towards <paramref name="from"/>.</summary>
        public static Vector3 Point(EntityRepository repo, Entity door, Vector2 from)
        {
            var d = repo.GetComponentRO<SimTransform>(door).Position;
            var c = new Vector2(d.X, d.Y);
            var terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            int i = terrain != null && repo.HasManagedComponent<TerrainObjectKey>(door)
                ? terrain.DoorIndexOf(repo.GetManagedComponentRO<TerrainObjectKey>(door).Key) : -1;
            Vector2 n;
            if (i >= 0)
            {
                var def = terrain!.Doors[i];
                var panel = terrain.Panels[def.Panel];
                var along = panel.B - panel.A;
                n = along.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(-along.Y, along.X)) : Vector2.UnitY;
                c = def.Center;
                if (Vector2.Dot(from - c, n) < 0f) n = -n;
            }
            else
            {
                var away = from - c;
                n = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.UnitY;
            }
            var p = c + n * ApproachMetres;
            return new Vector3(p.X, p.Y, d.Z);
        }

        /// <summary>⭐ <c>CE-4040</c> — has a walker at <paramref name="at"/> gone through terrain door <paramref name="doorIndex"/>, coming from
        /// <paramref name="from"/>: is he on the other side of its wall (by more than 5 cm)?</summary>
        public static bool Through(TerrainWorld terrain, int doorIndex, Vector2 from, Vector2 at)
        {
            var def = terrain.Doors[doorIndex];
            var panel = terrain.Panels[def.Panel];
            var along = panel.B - panel.A;
            if (along.LengthSquared() < 1e-6f) return false;
            var u = Vector2.Normalize(along);
            var n = new Vector2(-u.Y, u.X);
            float side = Vector2.Dot(from - def.Center, n);
            float now = Vector2.Dot(at - def.Center, n);
            return side * now < 0f && System.MathF.Abs(now) > 0.05f;
        }

        /// <summary>How far off the doorway's axis (m, along the wall) a walker may be and still walk straight through.</summary>
        public const float AxisToleranceMetres = 0.2f;

        /// <summary>
        /// ⭐ <c>CE-4040</c> — the DOOR FUNNEL: where a walker in reach of terrain door <paramref name="doorIndex"/>, not yet through it,
        /// aims. Off the doorway's axis he first steps onto it on his own side (no nearer the wall than half a metre); on it, he aims
        /// straight through, a metre beyond the centre. 📌 Live in ua-civ-shelter civilians waiting their turn stood beside the doorway;
        /// aiming at their path's next corner took them diagonally into the jamb, and they stood pinned there. False for a door the
        /// terrain gives no wall panel.
        /// </summary>
        public static bool Funnel(TerrainWorld terrain, int doorIndex, Vector2 at, out Vector2 aim)
            => Funnel(terrain, doorIndex, at, at, out aim);

        /// <summary>
        /// ⭐ <c>CE-2259</c> — as <see cref="Funnel(TerrainWorld, int, Vector2, out Vector2)"/>, "his side" being the side his path comes
        /// FROM (<paramref name="from"/>), not the side he stands on. 📌 bc-house-clear: an entry man a few centimetres past the wall's plane
        /// (not yet through — <see cref="Through"/> wants 5 cm) read the room as his side and was aimed a metre back OUT; he rocked on the
        /// plane and the drill never cleared the room.
        /// </summary>
        public static bool Funnel(TerrainWorld terrain, int doorIndex, Vector2 from, Vector2 at, out Vector2 aim)
        {
            aim = default;
            var def = terrain.Doors[doorIndex];
            var panel = terrain.Panels[def.Panel];
            var along = panel.B - panel.A;
            if (along.LengthSquared() < 1e-6f) return false;
            var u = Vector2.Normalize(along);
            var n = new Vector2(-u.Y, u.X);
            var c = def.Center;
            if (Vector2.Dot(from - c, n) < 0f) n = -n;   // n points to the side he comes from
            float depth = Vector2.Dot(at - c, n);         // ≤ 0: on the plane or past it — straight through
            float lateral = Vector2.Dot(at - c, u);
            aim = System.MathF.Abs(lateral) > AxisToleranceMetres && depth > 0.1f
                ? c + n * System.Math.Clamp(depth, 0.5f, ApproachMetres)   // onto the axis, on his side
                : c - n * ApproachMetres;                                   // through
            return true;
        }
    }
}
