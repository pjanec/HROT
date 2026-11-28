using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐⭐ <c>CE-4684</c> (DESIGN_LLM_Commander §5c C-4) — what HROT can MEASURE about each named place, said once in the seat's static block so the model
/// chooses between options instead of doing geometry: which other places see it, its cover, whether a vehicle can reach it, and the minutes on foot to
/// its nearest neighbours. Computed ONCE at seat open (places do not move) from HROT's own services — the sight perception uses
/// (<see cref="EqsTerrainSight"/>, one source), the navmesh (<see cref="INavmeshProvider.PathCost"/>, metres) and the cover points
/// (<see cref="ICoverProvider"/>). ⛔ No terrain / no navmesh ⇒ that fact is left out, never guessed.
/// </summary>
public static class TerrainFacts
{
    /// <summary>An eye (a soldier standing) and what he looks at (a man's chest) above the ground (m).</summary>
    public const float EyeHeight = 1.7f, AimHeight = 1.0f;
    /// <summary>A building place (its centre inside a solid) is looked at from the first standable point out from it, stepping this far, up to this far (m).</summary>
    public const float StepMetres = 2f, SearchMetres = 24f;
    /// <summary>Cover points within this of a place count for it (m): ≥ 4 covered, 1–3 partial, 0 open.</summary>
    public const float CoverRadius = 15f;
    /// <summary>A vehicle reaches a place when the vehicle navmesh lies within this of it (m).</summary>
    public const float VehicleReach = 15f;
    /// <summary>A squad on foot, tactical pace (m/s) — travel minutes = path metres / this.</summary>
    public const float FootSpeed = 1.4f;
    /// <summary>Minutes on foot are given to this many nearest places.</summary>
    public const int Neighbours = 3;

    /// <summary>The facts for the seat's places in <paramref name="world"/> (name → one line); empty when the world has no terrain and no navmesh.</summary>
    public static Dictionary<string, string> Of(EntityRepository world, Gazetteer places)
    {
        var query = Fdp.Toolkit.World.WorldQuery.Of(world);
        var terrain = EqsTerrainSight.World(world);
        var sight = query != null ? EqsTerrainSight.Sight(world, null) : null;
        var nav = world.HasSingletonManaged<INavmeshProvider>() ? world.GetSingletonManaged<INavmeshProvider>() : null;
        var cover = world.HasSingletonManaged<ICoverProvider>() ? world.GetSingletonManaged<ICoverProvider>() : null;
        return Compute(places.Places,
                       p => query != null && EqsTerrainSight.TryPlace(query, p, 0f, out var placed) ? placed.Z : null,
                       sight, nav, cover, p => terrain != null && EqsTerrainSight.InsideSolid(terrain, p));
    }

    /// <summary>
    /// The facts from the given services (a rail passes fakes). <paramref name="ground"/>: the ground height where a man can stand, else null (inside a
    /// solid); <paramref name="insideBuilding"/>: a point inside a building footprint.
    /// </summary>
    public static Dictionary<string, string> Compute(IReadOnlyList<(string Name, Vector2 At)> places, Func<Vector2, float?> ground, ILosService? sight,
                                                     INavmeshProvider? nav, ICoverProvider? cover, Func<Vector2, bool> insideBuilding)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (places.Count == 0 || (sight == null && nav == null && cover == null)) return facts;

        // where a man stands AT each place: a spot he can stand on is the place itself (⛔ never a ring around it — 📌 the first build sampled a 6 m
        //   ring and read the objective, behind the Low Wall, as "seen from the house" through a ring point in front of the wall); a building's
        //   centre is inside it ⇒ the nearest standable points around its walls, in eight directions
        var samples = new List<Vector3>[places.Count];
        for (int i = 0; i < places.Count; i++)
        {
            samples[i] = new List<Vector3>(8);
            var c = places[i].At;
            if (ground(c) is { } cz) { samples[i].Add(new Vector3(c, cz)); continue; }
            foreach (var d in Around)
                for (float r = StepMetres; r <= SearchMetres; r += StepMetres)
                    if (ground(c + d * r) is { } z) { samples[i].Add(new Vector3(c + d * r, z)); break; }
        }

        var coverBuf = new CoverPoint[64];
        for (int i = 0; i < places.Count; i++)
        {
            var sb = new StringBuilder();
            var (name, at) = places[i];
            if (sight != null)
            {
                var seenFrom = new List<string>();
                for (int j = 0; j < places.Count; j++)
                    if (j != i && Sees(sight, samples[j], samples[i])) seenFrom.Add(places[j].Name);
                sb.Append("seen from: ").Append(seenFrom.Count == 0 ? "none" : string.Join(", ", seenFrom));
            }
            if (insideBuilding(at)) Clause(sb).Append("cover: covered (a building)");
            else if (cover != null)
            {
                int n = cover.GetCoverPointsInRadius(at, CoverRadius, coverBuf);
                Clause(sb).Append("cover: ").Append(n >= 4 ? "covered" : n >= 1 ? "partial" : "open");
            }
            if (nav != null)
            {
                var c3 = Stand(samples[i], at);
                bool reach = nav.ProjectToNavmesh(c3, out var snapped, (uint)NavLayerMask.Vehicle)
                             && Vector2.Distance(new Vector2(snapped.X, snapped.Y), at) <= VehicleReach;
                Clause(sb).Append("vehicles: ").Append(reach ? "yes" : "no");
                var near = new List<(int J, float D)>();
                for (int j = 0; j < places.Count; j++) if (j != i) near.Add((j, Vector2.Distance(at, places[j].At)));
                near.Sort((a, b) => a.D.CompareTo(b.D));
                var foot = new StringBuilder();
                for (int k = 0; k < near.Count && k < Neighbours; k++)
                {
                    int j = near[k].J;
                    float metres = nav.PathCost(c3, Stand(samples[j], places[j].At), (uint)NavLayerMask.Infantry);
                    foot.Append(foot.Length > 0 ? ", " : "").Append(places[j].Name).Append(' ')
                        .Append(metres >= float.MaxValue / 2 ? "no path" : Minutes(metres) + " min");
                }
                if (foot.Length > 0) Clause(sb).Append("on foot: ").Append(foot);
            }
            if (sb.Length > 0) facts[name] = sb.ToString();
        }
        return facts;
    }

    private static readonly Vector2[] Around =
    {
        Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY,
        Vector2.Normalize(new Vector2(1, 1)), Vector2.Normalize(new Vector2(1, -1)), Vector2.Normalize(new Vector2(-1, 1)), Vector2.Normalize(new Vector2(-1, -1)),
    };

    private static StringBuilder Clause(StringBuilder sb) => sb.Length > 0 ? sb.Append(" · ") : sb;

    private static Vector3 Stand(List<Vector3> samples, Vector2 at) => samples.Count > 0 ? samples[0] : new Vector3(at, 0f);

    /// <summary>Does a man standing at any of <paramref name="from"/>'s points see a man at any of <paramref name="to"/>'s?</summary>
    private static bool Sees(ILosService sight, List<Vector3> from, List<Vector3> to)
    {
        for (int a = 0; a < from.Count; a++)
            for (int b = 0; b < to.Count; b++)
                if (sight.HasLineOfSight(from[a] + new Vector3(0, 0, EyeHeight), to[b] + new Vector3(0, 0, AimHeight))) return true;
        return false;
    }

    /// <summary>Minutes, to the half minute (at least 0.5).</summary>
    public static string Minutes(float metres)
    {
        double m = Math.Max(0.5, Math.Round(metres / FootSpeed / 60.0 * 2.0, MidpointRounding.AwayFromZero) / 2.0);
        return m.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
