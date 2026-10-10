using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Road;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>One route to plan: the ends, how much the actor wants the roads, an optional forced backend and the navmesh layer.</summary>
    public readonly struct RouteQuery
    {
        public readonly Vector3 Start;
        public readonly Vector3 End;
        public readonly RoadUse RoadUse;
        public readonly NavigationBackend Force;
        public readonly uint LayerMask;

        public RouteQuery(Vector3 start, Vector3 end, RoadUse roadUse, NavigationBackend force = NavigationBackend.Auto, uint layerMask = 0xFFFFFFFFu)
        {
            Start = start; End = end; RoadUse = roadUse; Force = force; LayerMask = layerMask == 0 ? 0xFFFFFFFFu : layerMask;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3128</c> (§5.2a D2/D3) — the ONE route planner of the ground: the path solver and the danger-along-route sensor
    /// both plan here, so the sensor watches the route the unit will drive.
    /// <para>⭐ The road is taken on COST (R-230), not geometry: walking direct (<see cref="INavmeshProvider.PathCost"/>) against
    /// navmesh → road → navmesh, the road metres weighted by the actor's <see cref="RoadUse"/>. The comparison uses costs only;
    /// the winning route alone is materialised. <see cref="RoadUse.Never"/> never looks at the roads.</para>
    /// <para>⭐ One instance per caller thread: the waypoint list and the router's search arrays are reused (R-220).</para>
    /// 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a.
    /// </summary>
    public sealed class RoutePlanner
    {
        /// <summary>
        /// ⭐ CE-1034 H3 (<c>DESIGN_Terrain_Height.md</c> TH-E/TH-F, R-248) — the world the plan is over, for the HEIGHT of points that
        /// arrive 2-D: road nodes, road entry/exit. ⚠ A production caller that has a world MUST set it before <see cref="Plan"/>
        /// (CLAUDE.md "the silent-default pattern"); null = no world (the point's own Z / the navmesh search at 0, as before).
        /// </summary>
        public Fdp.Toolkit.World.IWorldQuery? World { get; set; }

        /// <summary>The farthest a route leaves or joins the road network (m).</summary>
        public const float MaxAccessMeters = 500f;

        /// <summary>A move or leg shorter than this (m) is the two points as asked, costing 0: a real navmesh reports no path
        /// between coincident points.</summary>
        public const float ShortMoveMeters = 0.5f;

        /// <summary>Waypoints a single navmesh leg may produce.</summary>
        public const int MaxNavWaypoints = 128;

        private readonly RoadGraphRouter _router = new();
        private readonly List<Vector3> _points = new();
        private readonly List<byte> _traversals = new();
        private bool _anyTraversal;

        /// <summary>The weight of a road metre for <paramref name="use"/>; <see cref="float.PositiveInfinity"/> for Never.</summary>
        public static float RoadFactor(RoadUse use) => use switch
        {
            RoadUse.Never          => float.PositiveInfinity,
            RoadUse.Prefer         => 0.5f,
            RoadUse.StronglyPrefer => 0.25f,
            _                      => 1f,   // Neutral, and Unspecified when nobody resolved it
        };

        /// <summary>The planned route's waypoints (valid until the next <see cref="Plan"/>).</summary>
        public IReadOnlyList<Vector3> Points => _points;

        /// <summary>Per-waypoint traversal marks (door crossings from the navmesh legs), or null when there are none.</summary>
        public byte[]? TraversalsOrNull() => _anyTraversal ? _traversals.ToArray() : null;

        /// <summary>
        /// Plans <paramref name="q"/>. Returns the backend that produced it — <see cref="NavigationBackend.Navmesh"/> (direct),
        /// <see cref="NavigationBackend.Hybrid"/> (spliced through the roads), <see cref="NavigationBackend.NavRoadGraph"/> (forced
        /// onto the roads, or a road-only map) — or null when no route exists. <paramref name="distance"/> is the arc length.
        /// </summary>
        public NavigationBackend? Plan(in RouteQuery q, in RoadNetworkBlob roads, INavmeshProvider? navmesh,
            Fdp.Toolkit.Terrain.DoorStates? doors, out float distance)
        {
            _points.Clear(); _traversals.Clear(); _anyTraversal = false;
            distance = 0f;
            bool hasRoads = RoadGraphRouter.HasRoads(roads);
            bool forceRoad = q.Force == NavigationBackend.NavRoadGraph;
            bool forceNavmesh = q.Force == NavigationBackend.Navmesh;
            float factor = RoadFactor(q.RoadUse);

            // ── The road candidate, if the actor would consider one ─────────────────────────────────────────
            bool roadOk = false;
            RoadAccess entry = RoadAccess.None, exit = RoadAccess.None;
            float roadMetres = 0f;
            if (hasRoads && !forceNavmesh && (forceRoad || !float.IsPositiveInfinity(factor) || navmesh == null))
            {
                entry = RoadGraphRouter.NearestAccess(roads, Xy(q.Start), forceRoad || navmesh == null ? float.MaxValue : MaxAccessMeters);
                exit = RoadGraphRouter.NearestAccess(roads, Xy(q.End), forceRoad || navmesh == null ? float.MaxValue : MaxAccessMeters);
                roadOk = entry.IsValid && exit.IsValid && _router.Route(roads, entry, exit, out roadMetres);
            }

            // ── A move shorter than half a metre: the two points as asked, on any map ───────────────────────
            // ⚠ Measured live (ua-danger-crossing): a unit standing at the handle it is sent to. A real navmesh answers
            // coincident points with no path, and Append would merge them into one point — either way the move FAILED and
            // the behaviour re-issued it every tick. The pre-CE-3128 navmesh solve returned this two-point path.
            if (!forceRoad && Vector2.Distance(Xy(q.Start), Xy(q.End)) < ShortMoveMeters)
            {
                _points.Add(q.Start);
                _points.Add(q.End);
                return Finish(out distance, navmesh != null ? NavigationBackend.Navmesh : NavigationBackend.NavRoadGraph);
            }

            // ── No navmesh: the road graph is the only planner (a road-only map) ─────────────────────────────
            if (navmesh == null)
            {
                if (!roadOk || float.IsPositiveInfinity(factor) && !forceRoad) return null;
                return EmitRoadRoute(q, roads, null, doors, entry, exit, out distance, NavigationBackend.NavRoadGraph);
            }

            if (forceRoad)
                return roadOk ? EmitRoadRoute(q, roads, navmesh, doors, entry, exit, out distance, NavigationBackend.NavRoadGraph) : null;

            // ── Cost: direct against access + f·road + egress ──────────────────────────────────────────────
            float direct = navmesh.PathCost(q.Start, q.End, q.LayerMask, doors);
            if (roadOk && !float.IsPositiveInfinity(factor))
            {
                var entry3 = new Vector3(entry.Point, q.Start.Z);
                var exit3 = new Vector3(exit.Point, q.End.Z);
                float access = LegCost(navmesh, q.Start, entry3, q.LayerMask, doors);
                float egress = LegCost(navmesh, exit3, q.End, q.LayerMask, doors);
                if (access < float.MaxValue && egress < float.MaxValue)
                {
                    float viaRoad = access + factor * roadMetres + egress;
                    if (viaRoad < direct)
                        return EmitRoadRoute(q, roads, navmesh, doors, entry, exit, out distance, NavigationBackend.Hybrid);
                }
            }

            if (direct >= float.MaxValue) return null;
            return AppendNavmesh(navmesh, q.Start, q.End, q.LayerMask, doors) ? Finish(out distance, NavigationBackend.Navmesh) : null;
        }

        private NavigationBackend? EmitRoadRoute(in RouteQuery q, in RoadNetworkBlob roads, INavmeshProvider? navmesh,
            Fdp.Toolkit.Terrain.DoorStates? doors, RoadAccess entry, RoadAccess exit, out float distance, NavigationBackend backend)
        {
            distance = 0f;
            // ⭐ H3 — the road's access points stand on the surface there, at the level of the end they serve (not at that end's Z)
            var entry3 = new Vector3(entry.Point, World?.SurfaceZ(entry.Point.X, entry.Point.Y, q.Start.Z) ?? q.Start.Z);
            var exit3 = new Vector3(exit.Point, World?.SurfaceZ(exit.Point.X, exit.Point.Y, q.End.Z) ?? q.End.Z);
            // access leg
            if (navmesh != null) { if (!AppendNavmesh(navmesh, q.Start, entry3, q.LayerMask, doors)) Straight(q.Start, entry3); }
            else Straight(q.Start, entry3);
            // road leg
            uint layer = q.LayerMask;
            _router.EmitLeg(roads, _points, p => HeightAt(navmesh, p, layer));
            PadTraversals();
            // egress leg
            if (navmesh != null) { if (!AppendNavmesh(navmesh, exit3, q.End, q.LayerMask, doors)) Straight(exit3, q.End); }
            else Straight(exit3, q.End);
            return Finish(out distance, backend);
        }

        private unsafe bool AppendNavmesh(INavmeshProvider navmesh, Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
        {
            var buf = stackalloc NavWaypoint[MaxNavWaypoints];
            var span = new Span<NavWaypoint>(buf, MaxNavWaypoints);
            int count = navmesh.PlanPath(from, to, span, layerMask, doors);
            if (count < 2) return false;
            for (int k = 0; k < count; k++)
            {
                int before = _points.Count;
                RoadGraphRouter.Append(_points, span[k].Position);
                if (_points.Count == before) continue;
                PadTraversals();
                if (span[k].Traversal != TraversalKind.Walk) { _traversals[^1] = (byte)span[k].Traversal; _anyTraversal = true; }
            }
            return true;
        }

        private void Straight(Vector3 from, Vector3 to)
        {
            RoadGraphRouter.Append(_points, from);
            RoadGraphRouter.Append(_points, to);
            PadTraversals();
        }

        private void PadTraversals() { while (_traversals.Count < _points.Count) _traversals.Add(0); }

        private NavigationBackend? Finish(out float distance, NavigationBackend backend)
        {
            PadTraversals();
            distance = 0f;
            for (int i = 1; i < _points.Count; i++) distance += Vector2.Distance(Xy(_points[i - 1]), Xy(_points[i]));
            return _points.Count >= 2 ? backend : null;
        }

        /// <summary>A leg's navmesh cost; ⚠ a leg shorter than half a metre costs 0 (a real navmesh reports no path between
        /// coincident points, which would wrongly rule out a unit already standing on the road).</summary>
        private static float LegCost(INavmeshProvider navmesh, Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => Vector2.Distance(Xy(from), Xy(to)) < ShortMoveMeters ? 0f : navmesh.PathCost(from, to, layerMask, doors);

        /// <summary>A road node's height: the navmesh snapped from the GROUND there (⭐ H3 — the search box is centred on the real ground,
        /// not on 0, so it still finds the mesh on a hill), else the ground itself.</summary>
        private float HeightAt(INavmeshProvider? navmesh, Vector2 p, uint layer)
        {
            float ground = World?.GroundHeightAt(p.X, p.Y) ?? 0f;
            return navmesh != null && navmesh.ProjectToNavmesh(new Vector3(p, ground), out var snapped, layer) ? snapped.Z : ground;
        }

        private static Vector2 Xy(Vector3 v) => new(v.X, v.Y);
    }
}
