using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Perception.Survey;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2276</c> E-4 (DA-85…DA-91, R-2060; 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8d E-4) — pure: the
    /// platoon's battle positions over an AREA from one <c>BuildingSurvey</c> answer. One line squad per enterable building, ranked by its
    /// windows that look toward the engagement area and then by nearness to the centre; the squads left over take the lee of the area's
    /// solid obstacles, then stand across the area's middle bearing on open ground.
    /// </summary>
    public static class BattlePositions
    {
        /// <summary>DA-85 — an order this wide (m) is an area; a smaller one is one position (E-1).</summary>
        public const float AreaMetres = 40f;
        /// <summary>DA-87 — a building squad's radius: half its longest face plus this (m), at least <see cref="MinBuildingRadius"/>.</summary>
        public const float BuildingMarginMetres = 3f, MinBuildingRadius = 8f;
        /// <summary>DA-88 — an open-ground squad stands this far beyond an obstacle's half-length in its lee (m) · open-ground squads this far
        /// apart across the area (m) · each with this radius (m), so its own cover ask reaches the walls round it.</summary>
        public const float LeeMetres = 2f, OpenSpacingMetres = 40f, OpenRadius = 25f;
        /// <summary>DA-88 — an obstacle's half-length when the answer carries no faces for it (⚠ a solid building's layout has none) (m).</summary>
        public const float ObstacleHalfMetres = 4f;
        /// <summary>DA-86 — a window whose face looks within 72° of the bearing to the area's middle faces the area (the cosine).</summary>
        public const float FacingCos = 0.309f;
        private const byte WindowPortal = 1;

        /// <summary>One position: where the squad defends, how far round it its own asks look, the surveyed building it holds (−1 = open ground).</summary>
        public readonly record struct Position(Vector3 Point, float Radius, int Building);

        /// <summary>
        /// The positions of <paramref name="lines"/> line squads, best first: the ranked enterable buildings of <paramref name="answer"/>
        /// (null = none), then the lee of its solid obstacles, then open ground across the bearing toward <paramref name="areaMiddle"/>
        /// (null = no engagement area: across north). Open-ground points lie within <paramref name="radius"/> of <paramref name="centre"/>.
        /// </summary>
        public static Position[] Plan(WorldSurveyPayload? answer, Vector3 centre, float radius, Vector3? areaMiddle, int lines)
        {
            var result = new List<Position>(Math.Max(0, lines));
            foreach (var b in Rank(answer, centre, areaMiddle))
            {
                if (result.Count >= lines) break;
                result.Add(b);
            }
            foreach (var o in Obstacles(answer, centre, areaMiddle))
            {
                if (result.Count >= lines) break;
                result.Add(o);
            }
            float facing = areaMiddle is { } m ? LookAt.BearingTo(centre, m) : 0f;
            var across = Across(facing);
            bool centreFree = result.Count == 0;
            float reach = MathF.Max(0f, radius - OpenRadius * 0.5f);
            for (int k = 0; result.Count < lines; k++)
            {
                // the centre (only when nothing took a squad), then right, left, right … of it, 40 m a step, inside the area
                int step = centreFree ? (k + 1) / 2 : k / 2 + 1;
                float side = (centreFree ? k % 2 == 1 : k % 2 == 0) ? 1f : -1f;
                float off = MathF.Min(step * OpenSpacingMetres, reach);
                result.Add(new Position(centre + new Vector3(across.X, across.Y, 0f) * (side * off), OpenRadius, -1));
            }
            return result.ToArray();
        }

        /// <summary>DA-86, DA-87 — the enterable (non-solid) buildings of the answer as positions, best first.</summary>
        public static List<Position> Rank(WorldSurveyPayload? answer, Vector3 centre, Vector3? areaMiddle)
        {
            var ranked = new List<(Position P, int Windows, float Distance)>();
            if (answer == null) return new List<Position>();
            foreach (var b in answer.Buildings)
            {
                if (b.Solid) continue;
                var at = new Vector2(b.CentreX, b.CentreY);
                var faces = FacesOf(answer, b.Building);
                Vector2? toArea = Toward(areaMiddle, at);
                int windows = 0;
                foreach (var p in answer.Portals)
                {
                    if (p.Building != b.Building || p.Kind != WindowPortal) continue;
                    if (toArea is not { } dir) { windows++; continue; }
                    if (p.Face < 0 || p.Face >= faces.Count) continue;
                    var outward = new Vector2(faces[p.Face].OutX, faces[p.Face].OutY);
                    if (outward.LengthSquared() > 1e-6f && Vector2.Dot(Vector2.Normalize(outward), dir) >= FacingCos) windows++;
                }
                float floor = b.Levels.Count > 0 ? b.Levels[0] : centre.Z;
                float r = MathF.Max(MinBuildingRadius, Longest(faces) * 0.5f + BuildingMarginMetres);
                ranked.Add((new Position(new Vector3(at.X, at.Y, floor), r, b.Building), windows,
                            Vector2.Distance(at, new Vector2(centre.X, centre.Y))));
            }
            ranked.Sort((x, y) => x.Windows != y.Windows ? y.Windows.CompareTo(x.Windows) : x.Distance.CompareTo(y.Distance));
            return ranked.ConvertAll(x => x.P);
        }

        /// <summary>DA-88 — the solid obstacles of the answer (a shed, a wreck), nearest the centre first, each as a point in its lee: away
        /// from the area's middle (no area: south of it), half its longest face + <see cref="LeeMetres"/> out.</summary>
        public static List<Position> Obstacles(WorldSurveyPayload? answer, Vector3 centre, Vector3? areaMiddle)
        {
            var found = new List<(Position P, float Distance)>();
            if (answer == null) return new List<Position>();
            foreach (var b in answer.Buildings)
            {
                if (!b.Solid) continue;
                var at = new Vector2(b.CentreX, b.CentreY);
                var away = Toward(areaMiddle, at) is { } dir ? -dir : new Vector2(0f, -1f);
                float longest = Longest(FacesOf(answer, b.Building));
                float out_ = (longest > 0f ? longest * 0.5f : ObstacleHalfMetres) + LeeMetres;
                var lee = at + away * out_;
                found.Add((new Position(new Vector3(lee.X, lee.Y, centre.Z), OpenRadius, b.Building),
                           Vector2.Distance(at, new Vector2(centre.X, centre.Y))));
            }
            found.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            return found.ConvertAll(x => x.P);
        }

        private static List<SurveyFace> FacesOf(WorldSurveyPayload answer, int building)
        {
            var faces = new List<SurveyFace>();
            foreach (var f in answer.Faces) if (f.Building == building) faces.Add(f);
            return faces;
        }

        private static float Longest(List<SurveyFace> faces)
        {
            float longest = 0f;
            foreach (var f in faces) longest = MathF.Max(longest, Vector2.Distance(new Vector2(f.AX, f.AY), new Vector2(f.BX, f.BY)));
            return longest;
        }

        private static Vector2? Toward(Vector3? areaMiddle, Vector2 from)
            => areaMiddle is { } m && Vector2.DistanceSquared(new Vector2(m.X, m.Y), from) > 1e-4f
                ? Vector2.Normalize(new Vector2(m.X, m.Y) - from) : null;

        /// <summary>The unit vector across a compass bearing — to the right of a man facing it.</summary>
        private static Vector2 Across(float bearing)
        {
            float rad = (bearing + 90f) * (MathF.PI / 180f);
            return new Vector2(MathF.Sin(rad), MathF.Cos(rad));
        }
    }
}
