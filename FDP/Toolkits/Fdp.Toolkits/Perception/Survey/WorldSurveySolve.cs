using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Perception.Survey
{
    /// <summary>
    /// ⭐ <c>CE-3249</c> S2 — the muscle's half: one <see cref="WorldSurveyRequest"/> answered against the world AS IT IS NOW (R-2037) —
    /// sight through <see cref="IWorldQuery"/> (walls, open windows, door states, clouds), buildings through the derived
    /// <see cref="BuildingLayout"/>, door states read live. Pure: the caller (the EQS solver) publishes the answer.
    /// 📄 docs/DESIGN_World_Survey_Sensor.md §5.
    /// </summary>
    public static class WorldSurveySolve
    {
        /// <summary>A ray is stopped where what it crossed lets less than this through (the terrain's sight threshold).</summary>
        public const float SightThreshold = 0.5f;

        /// <summary>The work units a request costs (<c>EqsCost</c>: a sensor 100, a sight test 4).</summary>
        public static int CostOf(WorldSurveyRequest request) => request.Kind switch
        {
            WorldSurveyKind.VisibilityFan => 100 + 4 * Math.Min(request.Origins.Count, WorldSurveyRequest.MaxOrigins)
                                                 * Math.Clamp(request.Rays, 1, WorldSurveyRequest.MaxRays),
            WorldSurveyKind.BuildingSurvey => 100 + 16 * Math.Max(1, request.Buildings.Count),
            WorldSurveyKind.PointSurvey => 100 + 4 * Math.Min(request.Points.Count, WorldSurveyRequest.MaxPoints),
            _ => 0,
        };

        /// <summary>Answer <paramref name="request"/> in <paramref name="view"/>'s world. A view with no world answers empty.</summary>
        public static WorldSurveyPayload Solve(ISimulationView view, WorldSurveyRequest request, List<TraceCrossing>? scratch = null)
        {
            var payload = new WorldSurveyPayload { Kind = request.Kind };
            var query = WorldQuery.Of(view);
            var world = view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            switch (request.Kind)
            {
                case WorldSurveyKind.VisibilityFan:
                    if (query != null) Fan(query, request, payload, scratch ?? new List<TraceCrossing>());
                    break;
                case WorldSurveyKind.BuildingSurvey:
                    if (world != null) Buildings(view, world, request, payload);
                    break;
                case WorldSurveyKind.PointSurvey:
                    if (query != null) Points(query, world, request, payload);
                    break;
            }
            return payload;
        }

        // ── VisibilityFan ─────────────────────────────────────────────────────────────────────────────────────────────

        private static void Fan(IWorldQuery query, WorldSurveyRequest request, WorldSurveyPayload payload, List<TraceCrossing> crossings)
        {
            int origins = Math.Min(request.Origins.Count, WorldSurveyRequest.MaxOrigins);
            int rays = Math.Clamp(request.Rays, 1, WorldSurveyRequest.MaxRays);
            payload.Truncated |= request.Origins.Count > origins || request.Rays > rays;
            float range = Math.Max(0.1f, request.Range);
            for (int o = 0; o < origins; o++)
            {
                var origin = request.Origins[o];
                var eye = new Vector3(origin.X, origin.Y, origin.Z + request.EyeHeight);
                for (int r = 0; r < rays; r++)
                {
                    float t = rays == 1 ? 0.5f : r / (float)(rays - 1);
                    float bearing = origin.Bearing - origin.HalfArc + 2f * origin.HalfArc * t;
                    var to = eye + new Vector3(MathF.Cos(bearing), MathF.Sin(bearing), 0f) * range;
                    crossings.Clear();
                    query.Trace(eye, to, TracePurpose.Sight, crossings);
                    var ray = new SurveyRay { Origin = o, Bearing = bearing, Free = range, Stop = SurveyStop.StopNone };
                    float through = 1f;
                    foreach (var c in crossings)
                    {
                        through *= c.Loss;
                        if (through >= SightThreshold) continue;
                        ray.Free = c.T * range;
                        ray.Stop = c.ClosedBarrier ? SurveyStop.Structure : c.Obscurant ? SurveyStop.Obscurant : SurveyStop.Terrain;
                        ray.StopTopZ = c.TopZ;
                        break;
                    }
                    payload.Rays.Add(ray);
                }
            }
        }

        // ── BuildingSurvey ────────────────────────────────────────────────────────────────────────────────────────────

        private static void Buildings(ISimulationView view, TerrainWorld world, WorldSurveyRequest request, WorldSurveyPayload payload)
        {
            var chosen = new List<int>();
            foreach (int b in request.Buildings)
                if (b >= 0 && b < world.Buildings.Count && !chosen.Contains(b)) chosen.Add(b);
            if (request.AreaRadius > 0f)
            {
                var c = new Vector2(request.AreaX, request.AreaY);
                for (int b = 0; b < world.Buildings.Count; b++)
                    if (!chosen.Contains(b) && Meets(world.Buildings[b].Footprint, c, request.AreaRadius)) chosen.Add(b);
            }
            if (chosen.Count > WorldSurveyRequest.MaxBuildings)
            {
                payload.Truncated = true;
                chosen.RemoveRange(WorldSurveyRequest.MaxBuildings, chosen.Count - WorldSurveyRequest.MaxBuildings);
            }

            var doors = world.Doors.Count > 0 ? DoorStates.Of(view, world) : null;
            foreach (int b in chosen)
            {
                var layout = BuildingLayoutCache.Of(world, b);
                var building = world.Buildings[b];
                var centre = Centre(building.Footprint);
                payload.Buildings.Add(new SurveyBuilding
                {
                    Building = b, Label = building.Label, Solid = building.Solid, Levels = new List<float>(layout.Levels),
                    RoofLevel = layout.RoofLevel, CentreX = centre.X, CentreY = centre.Y,
                });
                for (int i = 0; i < layout.Rooms.Count; i++)
                {
                    var r = layout.Rooms[i];
                    payload.Rooms.Add(new SurveyRoom
                    {
                        Building = b, Index = i, Level = r.Level, FloorZ = r.FloorZ, CentreX = r.Centre.X, CentreY = r.Centre.Y, Area = r.Area,
                        Indoors = r.Indoors, Corners = new[] { r.C0.X, r.C0.Y, r.C1.X, r.C1.Y, r.C2.X, r.C2.Y, r.C3.X, r.C3.Y },
                    });
                }
                foreach (var p in layout.Portals)
                    payload.Portals.Add(new SurveyPortal
                    {
                        Building = b, Kind = (byte)p.Kind, RoomA = p.RoomA, RoomB = p.RoomB, X = p.Centre.X, Y = p.Centre.Y, Z = p.Centre.Z,
                        Width = p.Width, SillZ = p.SillZ, HeadZ = p.HeadZ, Walkable = p.Walkable, Face = p.Face,
                        IsDoor = p.Door >= 0,
                        DoorState = p.Door >= 0 ? (byte)(doors != null ? doors[p.Door] : world.Doors[p.Door].Initial) : (byte)0,
                    });
                foreach (var f in layout.Faces)
                    payload.Faces.Add(new SurveyFace
                    {
                        Building = b, Level = f.Level, AX = f.A.X, AY = f.A.Y, BX = f.B.X, BY = f.B.Y, OutX = f.Outward.X, OutY = f.Outward.Y,
                        BaseZ = f.BaseZ, TopZ = f.TopZ,
                    });
            }
        }

        // ── PointSurvey ───────────────────────────────────────────────────────────────────────────────────────────────

        private static void Points(IWorldQuery query, TerrainWorld? world, WorldSurveyRequest request, WorldSurveyPayload payload)
        {
            int n = Math.Min(request.Points.Count, WorldSurveyRequest.MaxPoints);
            payload.Truncated |= request.Points.Count > n;
            for (int i = 0; i < n; i++)
            {
                var q = request.Points[i];
                var p = new SurveyPoint
                {
                    X = q.X, Y = q.Y, Z = q.Z,
                    GroundZ = query.GroundHeightAt(q.X, q.Y),
                    SurfaceZ = query.SurfaceZ(q.X, q.Y, q.Z),
                };
                int b = world != null ? BuildingLayoutCache.BuildingAt(world, new Vector2(q.X, q.Y)) : -1;
                if (b >= 0)
                {
                    var layout = BuildingLayoutCache.Of(world!, b);
                    p.Building = b;
                    p.RoofZ = world!.Buildings[b].StoreyZ.Count > 0 ? world.Buildings[b].StoreyZ[^1] : 0f;
                    p.Level = layout.LevelAt(q.Z);
                    p.Room = layout.RoomAt(new Vector2(q.X, q.Y), p.Level);
                    p.Indoors = p.Room >= 0 && layout.Rooms[p.Room].Indoors;
                }
                payload.Points.Add(p);
            }
        }

        private static Vector2 Centre(Vector2[] footprint)
        {
            var s = Vector2.Zero;
            foreach (var p in footprint) s += p;
            return footprint.Length == 0 ? s : s / footprint.Length;
        }

        private static bool Meets(Vector2[] footprint, Vector2 c, float radius)
        {
            if (footprint.Length == 0) return false;
            if (PolygonMath.Contains(footprint, c)) return true;
            for (int i = 0, j = footprint.Length - 1; i < footprint.Length; j = i++)
            {
                var a = footprint[j]; var b = footprint[i]; var ab = b - a;
                float t = ab.LengthSquared() < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(c - a, ab) / ab.LengthSquared(), 0f, 1f);
                if (Vector2.Distance(c, a + ab * t) <= radius) return true;
            }
            return false;
        }
    }
}
