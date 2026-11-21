using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐⭐ <c>CE-3072</c> B3 (R-213, B1″) — the solve of one danger-area sensor on the node that holds the navmesh: the route
    /// it watches, then <see cref="DangerAlongRouteClassifier"/> over the road graph. Called by <c>EqsSolverSystem</c> for a
    /// sensor whose template is <see cref="DangerAreaChildSensor.TemplateId"/>. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.7.
    /// <list type="bullet">
    ///   <item>⭐ the route's END: the sensor's <c>ContextPoint1</c> when it carries one (<see cref="DangerRouteSource.ToPoint"/>);
    ///     otherwise the unit's own move — its <see cref="NavigationIntent.FinalDestination"/> (<see cref="DangerRouteSource.OwnMove"/>).</item>
    ///   <item>the route's START: the unit, each solve (so the areas behind it drop out on their own);</item>
    ///   <item>⭐⭐ <c>CE-3128</c> (Nav v2 §5.2a D2) — the route: the SAME <see cref="RoutePlanner"/> the path solver uses, with the
    ///     unit's <see cref="RoadUse"/> resolved the same way (<see cref="PathRequests.ResolveRoadUse"/>), so the sensor watches the
    ///     route the unit will drive — navmesh, or navmesh → road → navmesh. A straight leg when nothing can plan it.</item>
    /// </list>
    /// </summary>
    public static class DangerAlongRouteSolve
    {
        /// <summary>⭐ <c>CE-3227</c> — at most this many tear-gas clouds are judged per solve (the nearest are not chosen — the first found).</summary>
        public const int MaxGasClouds = 16;

        /// <summary>
        /// The areas along <paramref name="carrier"/>'s route, written into <paramref name="areas"/>; returns how many. 0 =
        /// a clear route, no road graph, or no route to watch (the unit is not moving and no point was given).
        /// <paramref name="planner"/> is the caller thread's planner; <paramref name="roads"/> the node's road graph carrier.
        /// </summary>
        public static int Solve(EntityRepository repo, Entity carrier, in EqsSensor sensor, Span<DangerAreaDescriptor> areas,
            RoutePlanner planner, global::CarKinem.Road.RoadNetworkHolder? roads)
        {
            if (planner == null) return 0;
            if (!EqsContext.SelfPosition(repo, carrier, sensor, out var start)) return 0;
            if (!TryRouteEnd(repo, carrier, in sensor, out var end, out var roadUse)) return 0;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(repo);   // ⭐ CE-1035 Q2
            planner.World = terrain;                                // ⭐ CE-1034 H3 — the route's road legs on the real ground

            // ⭐ CE-3227 (§7.3d-B G-2/G-4) — the tear gas this node holds; with gas on the map a node with no road graph still answers
            uint layer = EqsContext.SelfLayer(repo, carrier, sensor);
            byte sidePlus1 = Fdp.Toolkit.Navigation.NavLayerSelection.SidePlus1Of(layer);
            // ⭐ C6 — lease the graph for the whole plan + classify: a terrain commit may publish a new one meanwhile.
            using var lease = roads?.Borrow();
            var graph = lease?.Value ?? default;
            var doorStates = Fdp.Toolkit.Terrain.DoorStates.Of(repo);
            var knowledge = doorStates?.Knowledge ?? Fdp.Toolkit.Terrain.PlaceKnowledgeTable.Of(repo);   // ⭐ CE-4624 — with no terrain the view's table still answers
            var underFire = doorStates?.UnderFire ?? Fdp.Toolkit.Terrain.UnderFireTable.Of(repo);       // ⭐ CE-2468 UF-6 — the places under fire, by side
            var sight = doorStates?.UnderFireSight ?? Fdp.Toolkit.Terrain.UnderFireSight.Of(repo);

            // ⭐ CE-3227 (§7.3d-B G-2/G-4) — the tear gas this node holds; with gas on the map a node with no road graph still answers
            Span<GasFootprint> gas = stackalloc GasFootprint[MaxGasClouds + MaxShelledAreas];
            int gasCount = GasFootprints.Of(repo, gas.Slice(0, MaxGasClouds));
            // ⭐ CE-2468 UF-6 (§9b, K8) — the shelled areas the unit's SIDE knows (Indirect records), through the same seam. ⛔ SUPERSEDED: the node-level
            //   ShelledAreas.Footprints (CE-4583) — every side saw every shelling; ShelledAreas is now the detector that teaches the witness sides (UF-7)
            gasCount += ShelledFootprints(underFire, sidePlus1, gas.Slice(gasCount));
            if (!RoadGraphRouter.HasRoads(graph) && gasCount == 0 && knowledge.RecordCount == 0 && knowledge.MarkedCount == 0 && !sight.HasSide(sidePlus1)) return 0;   // ⭐ CE-4624 / CE-2468 — known mines or known enemy ground are reason enough

            var navmesh = repo.HasSingletonManaged<INavmeshProvider>() ? repo.GetSingletonManaged<INavmeshProvider>() : null;
            var query = new RouteQuery(start, end, roadUse, NavigationBackend.Auto, layer);
            Span<Vector3> route = stackalloc Vector3[RoutePlanner.MaxNavWaypoints * 3];
            int points;
            if (planner.Plan(in query, in graph, navmesh, doorStates, out _) != null)
            {
                var planned = planner.Points;
                points = Math.Min(planned.Count, route.Length);
                for (int i = 0; i < points; i++) route[i] = planned[i];
            }
            else
            {
                route[0] = start; route[1] = end; points = 2;   // nothing can plan it: the straight leg
            }
            // ⭐ CE-3081 (§10.7b A1/A6) — the node's cover map (the one the cover query reads) judges OpenGround; none ⇒ crossings only
            var cover = repo.HasSingletonManaged<ICoverProvider>() ? repo.GetSingletonManaged<ICoverProvider>() : null;
            int count = DangerAlongRouteClassifier.Classify(route.Slice(0, points), in graph, terrain, cover, sensor.SearchRadius, areas,
                gas.Slice(0, gasCount));
            // ⭐ CE-4624 MS-T3 (MR-9, KG-7, KR-9) — a minefield the unit's SIDE knows (suspected or mined cells) on the route: NO-ENTER, derived from the side's knowledge (no store)
            count += MinefieldAreas(repo, route.Slice(0, points), sidePlus1, knowledge, areas.Slice(count));
            // ⭐ CE-2468 UF-6 (§9b, K10) — ground a known enemy of the unit's SIDE covers (Direct records' sight grids) on the route
            count += CoveredByFireAreas(sight, route.Slice(0, points), sidePlus1, areas.Slice(count));
            MarkSeenFrom(repo, carrier, terrain, areas.Slice(0, count));   // ⭐ CE-3272 (Q94 B)
            return count;
        }

        /// <summary>⭐ CE-2468 UF-6 — at most this many shelled areas the side knows are judged per solve (the shelled areas' own capacity).</summary>
        public const int MaxShelledAreas = ShelledAreas.Capacity;

        /// <summary>⭐ CE-2468 UF-6 (K10) — at most this many <see cref="DangerAreaKind.CoveredByFire"/> areas per solve (the first the route meets, per record).</summary>
        public const int MaxCoveredByFireAreas = 4;

        /// <summary>
        /// ⭐ CE-2468 UF-6 (DESIGN_Minefields.md §9b, K8) — a <see cref="DangerAreaKind.Shelled"/> footprint (the record's circle) for every FRESH <see cref="Fdp.Toolkit.Terrain.UnderFireCause.Indirect"/>
        /// record side <paramref name="sidePlus1"/> − 1 holds; returns how many. The id is the record's subject (stable while it lives). No side ⇒ none. 0 B.
        /// </summary>
        public static int ShelledFootprints(Fdp.Toolkit.Terrain.UnderFireTable table, byte sidePlus1, Span<GasFootprint> into)
        {
            if (sidePlus1 == 0) return 0;
            int n = 0;
            for (int i = 0; i < table.Count && n < into.Length; i++)
            {
                ref readonly var e = ref table[i];
                if (e.SidePlus1 != sidePlus1 || e.Record.Cause != Fdp.Toolkit.Terrain.UnderFireCause.Indirect) continue;
                var p = e.Record.Point;
                var c = new Vector2(p.X, p.Y);
                into[n++] = new GasFootprint(e.Subject.GetHashCode(), c, c, e.Record.RangeM, p.Z - 10f, p.Z + 10f) { Kind = DangerAreaKind.Shelled };
            }
            return n;
        }

        /// <summary>
        /// ⭐ CE-2468 UF-6 (DESIGN_Minefields.md §9b, K10 — the <see cref="MinefieldAreas"/> shape) — one <see cref="DangerAreaKind.CoveredByFire"/> area per <c>Direct</c> grid of side
        /// <paramref name="sidePlus1"/> − 1 the route enters: its centre the entry point, the near handle the last sample before it, the far handle where the route leaves the covered
        /// ground (the route's end when it never does). The id is the record's subject. Returns how many it wrote (≤ <see cref="MaxCoveredByFireAreas"/>). 0 B.
        /// </summary>
        public static int CoveredByFireAreas(Fdp.Toolkit.Terrain.UnderFireSight sight, ReadOnlySpan<Vector3> route, byte sidePlus1, Span<DangerAreaDescriptor> into)
        {
            if (sidePlus1 == 0 || route.Length < 2 || !sight.HasSide(sidePlus1)) return 0;
            int n = 0, cap = Math.Min(into.Length, MaxCoveredByFireAreas);
            for (int gi = 0; gi < sight.GridCount && n < cap; gi++)
            {
                var (side, grid) = sight.Grid(gi);
                if (side != sidePlus1) continue;
                Vector3 safe = route[0], entry = default, exit = route[route.Length - 1];
                bool inside = false, done = false;
                float before = 0f, along = 0f;
                for (int i = 1; i < route.Length && !done; i++)
                {
                    var a = route[i - 1]; var b = route[i];
                    if (i > 1) before += Vector3.Distance(route[i - 2], a);
                    int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / MinefieldSampleMetres));
                    for (int k = 0; k <= steps; k++)
                    {
                        var p = Vector3.Lerp(a, b, k / (float)steps);
                        bool covered = grid.Covers(new Vector2(p.X, p.Y));
                        if (!inside)
                        {
                            if (covered) { inside = true; entry = p; along = before + Vector3.Distance(a, p); }
                            else safe = p;
                        }
                        else if (!covered) { exit = p; done = true; break; }
                    }
                }
                if (!inside) continue;
                uint id = unchecked((uint)sight.SubjectOf(gi).GetHashCode());
                into[n++] = new DangerAreaDescriptor
                {
                    FeatureId = id == 0 ? 1u : id, ThreatRating = 1f, Kind = DangerAreaKind.CoveredByFire,
                    Center = entry, ExtentsXY = new Vector2(grid.RangeM, grid.RangeM), AngleRad = 0f,
                    ZFloor = entry.Z - 2f, ZCeiling = entry.Z + 3f, NearSideHandle = safe, FarSideHandle = exit,
                    DistanceAlongRoute = along,
                };
            }
            return n;
        }

        /// <summary>The step along a route at which minefield knowledge is sampled (m).</summary>
        public const float MinefieldSampleMetres = 5f;

        [ThreadStatic] private static System.Collections.Generic.List<Vector2>? t_outline;
        [ThreadStatic] private static System.Collections.Generic.List<Fdp.Toolkit.Terrain.LaneItem>? t_lanes;

        /// <summary>
        /// ⭐ <c>CE-2443</c> (DESIGN_Minefields MF-10) — <paramref name="p"/> lies in a cleared lane of the field that side <paramref name="sidePlus1"/> − 1 knows (a lane
        /// record of its side): the lane is SUBTRACTED from the field, so walking it is no minefield ahead. ⚠ A marked field reads every cell known-mined
        /// (<see cref="Fdp.Toolkit.Terrain.PlaceKnowledgeTable"/>), lane cells included — the lane records are what say "clear here".
        /// </summary>
        public static bool InKnownLane(System.Collections.Generic.List<Fdp.Toolkit.Terrain.LaneItem> lanes, byte sidePlus1, Vector2 p)
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                var l = lanes[i];
                if (l.SidePlus1 != sidePlus1) continue;
                var d = l.B - l.A; float len2 = d.LengthSquared();
                float t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - l.A, d) / len2, 0f, 1f);
                if (Vector2.DistanceSquared(l.A + d * t, p) <= l.Width * l.Width * 0.25f) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-4624</c> MS-T3 (DESIGN_Minefields.md MR-9, KG-7) — one <see cref="DangerAreaKind.Minefield"/> area per field where <paramref name="route"/> first enters a cell
        /// side <paramref name="sidePlus1"/> − 1 knows of and does NOT know clear: its centre the entry point, its near handle the last safe sample before it (where to halt
        /// and call engineers). The planner prices such cells very high (KR-10), so a route here is one with no way round. Returns how many it wrote. Cold per solve (2 s).
        /// ⭐ CE-2443 (KG-7: "when it cannot (no way round, or the order goes through), the route's danger sensor reports a Minefield area") — measured on
        /// mf-clear-lane-and-cross: after a strike the side SUSPECTS only the cells round the blast; a route back in sidestepped them through cells it knew nothing of,
        /// so no area, no halt, no engineers — and a second mine. ⛔ SUPERSEDED: "a cell it knows as suspected or mined". A lane the side cleared (its lane record, MF-10) reports none — a marked field reads every cell known-mined, the lane included.
        /// </summary>
        public static int MinefieldAreas(EntityRepository repo, ReadOnlySpan<Vector3> route, byte sidePlus1, Fdp.Toolkit.Terrain.PlaceKnowledgeTable knowledge, Span<DangerAreaDescriptor> into)
        {
            if (sidePlus1 == 0 || into.Length == 0 || route.Length < 2 || !repo.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.Minefield>()) return 0;
            int side = sidePlus1 - 1, n = 0;
            var outline = t_outline ??= new System.Collections.Generic.List<Vector2>(16);
            var lanes = t_lanes ??= new System.Collections.Generic.List<Fdp.Toolkit.Terrain.LaneItem>(8);
            bool laneRecords = repo.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.MinefieldLanes>();
            foreach (var field in repo.Query().With<Fdp.Toolkit.Terrain.Minefield>().With<Fdp.Core.SimTransform>().Build())
            {
                if (n >= into.Length) break;
                if (!knowledge.KnowsAny(side, field) || !Fdp.Toolkit.Terrain.MinefieldKnowledgeOps.TryOutline(repo, field, outline, out var anchor)) continue;
                var grid = Fdp.Toolkit.Terrain.MinefieldKnowledgeOps.Grid(repo.GetComponentRO<Fdp.Toolkit.Terrain.Minefield>(field), anchor, outline);
                lanes.Clear();
                if (laneRecords && Fdp.Toolkit.Terrain.MinefieldIds.TryGuid(repo, field, out var fieldGuid)) Fdp.Toolkit.Terrain.MinefieldLanesSystem.LanesOf(repo, fieldGuid, lanes);
                Vector3 safe = route[0];
                bool found = false;
                float before = 0f;   // ⭐ the route's length up to segment i (behaviors, CE-4624 MS-B4: the "within N m" reaction reads DistanceAlongRoute)
                for (int i = 1; i < route.Length && !found; i++)
                {
                    var a = route[i - 1]; var b = route[i];
                    if (i > 1) before += Vector3.Distance(route[i - 2], a);
                    int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / MinefieldSampleMetres));
                    for (int k = 0; k <= steps; k++)
                    {
                        var p = Vector3.Lerp(a, b, k / (float)steps);
                        var p2 = new Vector2(p.X, p.Y);
                        if (Fdp.Toolkit.Terrain.MinePattern.Inside(outline, p2) && !InKnownLane(lanes, sidePlus1, p2)   // ⭐ CE-2443 (MF-10) — the side's own lane is a passage
                            && Fdp.Toolkit.Terrain.MinefieldKnowledgeOps.CellOf(grid, p2, out int c, out int r)
                            && knowledge.Knows(side, field, c, r) != Fdp.Toolkit.Terrain.MineCellKnowledge.KnownClear)   // ⭐ CE-2443 (KG-7) — any cell of a field the side KNOWS OF that it does not know CLEAR
                        {
                            into[n++] = new DangerAreaDescriptor
                            {
                                FeatureId = (uint)field.Index * 2654435761u, ThreatRating = 1f, Kind = DangerAreaKind.Minefield,
                                Center = p, ExtentsXY = new Vector2(grid.Cell * grid.Cols, grid.Cell * grid.Rows) * 0.5f, AngleRad = grid.OrientationDeg * (MathF.PI / 180f),
                                ZFloor = p.Z - 2f, ZCeiling = p.Z + 3f, NearSideHandle = safe, FarSideHandle = p,
                                DistanceAlongRoute = before + Vector3.Distance(a, p),
                            };
                            found = true;
                            break;
                        }
                        safe = p;
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// ⭐ <c>CE-3272</c> (Q94 B, R-3018 — 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §5.3) — which of the sensor's
        /// <see cref="EqsContextPoints"/> (its unit's remembered contacts) see each area: bit i when point i at eye height has sight of the
        /// area's centre on THIS node's terrain; every bit with no terrain (the brain's old "sight 1"). The brain rates from it and reads
        /// no geometry.
        /// </summary>
        public static void MarkSeenFrom(EntityRepository repo, Entity carrier, Fdp.Toolkit.World.IWorldQuery? terrain, Span<DangerAreaDescriptor> areas)
        {
            if (!repo.IsComponentTypeRegistered<EqsContextPoints>() || !repo.HasComponent<EqsContextPoints>(carrier))
            {
                for (int a = 0; a < areas.Length; a++) areas[a].SeenFrom = 0;
                return;
            }
            ref readonly var points = ref repo.GetComponentRO<EqsContextPoints>(carrier);
            int n = Math.Min(points.Count, EqsContextPoints.Capacity);
            for (int a = 0; a < areas.Length; a++)
            {
                var target = areas[a].Center + new Vector3(0f, 0f, Systems.DangerAreaSensorSystem.AreaHeight);
                ushort mask = 0;
                for (int i = 0; i < n; i++)
                {
                    var eye = points[i] + new Vector3(0f, 0f, Systems.DangerAreaSensorSystem.EyeHeight);
                    if (terrain == null || !terrain.SightBlocked(eye, target)) mask |= (ushort)(1 << i);
                }
                areas[a].SeenFrom = mask;
            }
        }

        /// <summary>Where the watched route ends — see the class remarks — and the unit's road use for it.</summary>
        public static bool TryRouteEnd(EntityRepository repo, Entity carrier, in EqsSensor sensor, out Vector3 end, out RoadUse roadUse)
        {
            var unit = EqsContext.Self(repo, carrier, sensor);
            roadUse = unit.IsNull ? RoadUse.Neutral : PathRequests.ResolveRoadUse(repo, unit, RoadUse.Unspecified);
            if ((sensor.ContextPointMask & EqsSensor.Point1Bit) != 0)
            {
                end = sensor.ContextPoint1;
                return true;
            }
            end = default;
            if (unit.IsNull || !repo.IsComponentTypeRegistered<NavigationIntent>() || !repo.HasComponent<NavigationIntent>(unit))
                return false;
            ref readonly var intent = ref repo.GetComponentRO<NavigationIntent>(unit);
            if (intent.Mode == NavigationMode.None) return false;
            end = Fdp.Toolkit.Navigation.NavigationDestination.Of(repo, unit, intent);   // ⭐ CE-1035 Q0b
            roadUse = PathRequests.ResolveRoadUse(repo, unit, intent.RoadUse);
            return true;
        }
    }
}
