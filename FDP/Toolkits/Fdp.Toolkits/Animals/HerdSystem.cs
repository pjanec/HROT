using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Animals
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-4 (docs/designs/animals/DESIGN_Animals.md AN-1, AR-9) — every HERD, once a frame, on the node running its animals'
    /// brains, SERIALLY (before the brains' parallel phase reads it): its centre and lead from its living members; its mode (the most afraid member's,
    /// else REST at night, else calm); the next GRAZING spot when the herd has reached the last one — inside its home, and after a fright the
    /// candidate farthest from where the fright came from (it recovers to grass away from the road); and herd CONTAGION — a frightened member pulls
    /// the others' fear toward its own through THE crowd's rule (<c>CrowdRules.ContagionPull</c>), harder the tighter the species keeps together,
    /// and passes on where the fright came from so they run the same way. ⭐ The brains only READ <see cref="HerdState"/> (declared in
    /// <c>BrainReads</c>); the members' <see cref="Fear"/> is written here, serially, never inside the brains' phase. 0 B in the steady state.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class HerdSystem : IEcsModuleSystem
    {
        /// <summary>A herd member this far from a frightened one feels half its pull.</summary>
        public const float ContagionReachMetres = 40f;
        /// <summary>A herd catches a flight this many times faster than a crowd catches fear (× (0.5 + cohesion)): a cow beside a bolting one
        /// goes within about a second.</summary>
        public const float HerdGain = 2f;
        /// <summary>The herd has reached its grazing spot when its centre is this close.</summary>
        public const float ReachedMetres = 6f;
        /// <summary>A grazing spot with no home is this far from the centre at most.</summary>
        public const float RoamMetres = 40f;
        /// <summary>Grazing candidates weighed after a fright (the one farthest from it wins).</summary>
        public const int Candidates = 6;
        /// <summary>A fright this recent steers the next grazing spot away from it (s).</summary>
        public const double FrightMemorySeconds = 180.0;
        /// <summary>The sun below this (degrees) is night: a herd lies down.</summary>
        public const double NightSunDeg = -6.0;
        /// <summary>The night is looked at this often (s) — the sun moves slowly.</summary>
        public const double NightLookSeconds = 10.0;

        private struct Acc
        {
            public Vector3 Sum;
            public int Count;
            public Entity Lead;
            public AnimalMode Mode;
            public float MaxFear;
            public Entity Afraid;   // the most frightened member
        }

        private readonly Dictionary<Entity, Acc> _acc = new();   // grown once, cleared every frame (0 B in the steady state)
        private Entity[] _members = new Entity[64];
        private bool _night;
        private double _nextNightLook = double.NegativeInfinity;

        /// <summary>The last night verdict (a rail reads it).</summary>
        public bool Night => _night;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<HerdState>() || !repo.IsComponentTypeRegistered<HerdMember>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;
            if (now >= _nextNightLook) { _night = IsNight(repo, now); _nextNightLook = now + NightLookSeconds; }

            LinkToAreas(repo);   // ⭐ AB-9 — an area's arriving animals join its herd

            // ① the members, per herd
            _acc.Clear();
            int n = 0;
            bool fears = repo.IsComponentTypeRegistered<Fear>();
            foreach (var m in repo.Query().With<HerdMember>().With<SimTransform>().Build())
            {
                var herd = repo.GetComponentRO<HerdMember>(m).Herd;
                if (herd.IsNull || !repo.IsAlive(herd) || !repo.HasComponent<HerdState>(herd)) continue;
                if (!Fdp.Toolkit.Combat.Injury.IsInTheFight(repo, m)) continue;
                if (n == _members.Length) Array.Resize(ref _members, n * 2);
                _members[n++] = m;
                _acc.TryGetValue(herd, out var a);
                a.Sum += (Vector3)repo.GetComponentRO<SimTransform>(m).Position;
                if (a.Count++ == 0) a.Lead = m;
                if (repo.HasComponent<AnimalState>(m))
                {
                    var mode = repo.GetComponentRO<AnimalState>(m).Mode;
                    if (Rank(mode) > Rank(a.Mode)) a.Mode = mode;
                }
                if (fears && repo.HasComponent<Fear>(m))
                {
                    float f = repo.GetComponentRO<Fear>(m).Value;
                    if (f > a.MaxFear) { a.MaxFear = f; a.Afraid = m; }
                }
                _acc[herd] = a;
            }

            // ② each herd's state
            foreach (var h in repo.Query().With<HerdState>().Build())
            {
                ref var hs = ref repo.GetComponentRW<HerdState>(h);
                if (!_acc.TryGetValue(h, out var a) || a.Count == 0) { hs.Lead = Entity.Null; hs.Members = 0; continue; }
                hs.Centre = a.Sum / a.Count;
                hs.Lead = a.Lead;
                hs.Members = (ushort)Math.Min(a.Count, ushort.MaxValue);
                var was = hs.Mode;
                hs.Mode = a.Mode != AnimalMode.Calm ? a.Mode : (_night ? AnimalMode.Rest : AnimalMode.Calm);
                bool unset = hs.NextGrazing == Vector3.Zero;
                bool reached = Flat(hs.Centre - hs.NextGrazing) <= ReachedMetres;
                bool afterFlight = was is AnimalMode.Flee or AnimalMode.Recover;   // calm again after a run: a new spot, away from the fright
                if ((unset || reached || afterFlight) && hs.Mode == AnimalMode.Calm) hs.NextGrazing = PickGrazing(repo, h, in hs, now);
            }

            // ③ contagion through the crowd's rule
            if (!fears || deltaTime <= 0f) return;
            for (int i = 0; i < n; i++)
            {
                var m = _members[i];
                if (!repo.HasComponent<Fear>(m)) continue;
                var herd = repo.GetComponentRO<HerdMember>(m).Herd;
                if (!_acc.TryGetValue(herd, out var a) || a.Afraid == m || a.Afraid.IsNull || a.MaxFear <= 0f) continue;
                ref readonly var src = ref repo.GetComponentRO<Fear>(a.Afraid);
                float mine = repo.GetComponentRO<Fear>(m).Value;
                float cohesion = Cohesion(repo, m);
                float d = Flat((Vector3)repo.GetComponentRO<SimTransform>(m).Position - (Vector3)repo.GetComponentRO<SimTransform>(a.Afraid).Position);
                if (d > ContagionReachMetres * 2f) continue;
                float pull = Civilians.CrowdRules.ContagionPull(mine, src.Value, d, ContagionReachMetres, out float target);
                if (pull <= 0f) continue;
                pull *= HerdGain * (0.5f + cohesion);   // a herd follows a flight faster than a crowd; a tight flock (sheep) faster than loose cattle
                ref var f = ref repo.GetComponentRW<Fear>(m);
                f.Value = Civilians.CrowdRules.Contagion(f.Value, target, pull, deltaTime);
                if (src.HasFrightFrom != 0) { f.FrightFrom = src.FrightFrom; f.HasFrightFrom = 1; }
                f.LastFrightAt = (SimInstant)now;
            }
        }

        private readonly List<Entity> _unlinked = new();

        /// <summary>
        /// ⭐ <c>CE-4623</c> AB-9 — an animal POPULATED from an area (its <see cref="AnimalHome"/> is the area's, no herd yet) joins the area's herd: the
        /// area entity carrying a <see cref="HerdState"/> of its species and the same home centre. By VALUE, not by an entity id — the creation request
        /// may be served on another node, where the area has another handle.
        /// </summary>
        private void LinkToAreas(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<AnimalHome>() || !repo.IsComponentTypeRegistered<AnimalArea>() || !repo.IsComponentTypeRegistered<AnimalState>()) return;
            _unlinked.Clear();
            foreach (var e in repo.Query().With<AnimalHome>().With<AnimalState>().Without<HerdMember>().Build()) _unlinked.Add(e);
            if (_unlinked.Count == 0) return;
            for (int i = 0; i < _unlinked.Count; i++)
            {
                var e = _unlinked[i];
                var home = repo.GetComponentRO<AnimalHome>(e);
                var species = repo.GetComponentRO<AnimalState>(e).Species;
                foreach (var area in repo.Query().With<AnimalArea>().With<HerdState>().With<AnimalHome>().Build())
                {
                    if (repo.GetComponentRO<HerdState>(area).Species != species) continue;
                    if (Flat((Vector3)repo.GetComponentRO<AnimalHome>(area).Centre - (Vector3)home.Centre) > 0.5f) continue;
                    repo.AddComponent(e, new HerdMember { Herd = area });
                    repo.GetComponentRW<AnimalHome>(e).Area = area;
                    break;
                }
            }
        }

        private static int Rank(AnimalMode m) => m switch
        {
            AnimalMode.Flee => 4, AnimalMode.Alert => 3, AnimalMode.Recover => 2, AnimalMode.Guard => 1, _ => 0,
        };

        private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

        private static float Cohesion(EntityRepository repo, Entity m)
            => AnimalFear.TemperamentOf(repo, m)?.HerdCohesion ?? 0.5f;

        /// <summary>The next grazing spot: inside the herd's home (its own <see cref="AnimalHome"/>, else round its centre); after a recent fright,
        /// the candidate farthest from it. Deterministic: the herd's index and the sim time seed it.</summary>
        internal static Vector3 PickGrazing(EntityRepository repo, Entity herd, in HerdState hs, double now)
        {
            Vector3 centre = hs.Centre;
            float radius = RoamMetres;
            if (repo.IsComponentTypeRegistered<AnimalHome>() && repo.HasComponent<AnimalHome>(herd))
            {
                var home = repo.GetComponentRO<AnimalHome>(herd);
                if (home.Radius > 0f) { centre = home.Centre; radius = home.Radius; }
            }
            bool fright = false;
            Vector3 from = default;
            if (!hs.Lead.IsNull && repo.IsComponentTypeRegistered<Fear>() && repo.HasComponent<Fear>(hs.Lead))
            {
                var f = repo.GetComponentRO<Fear>(hs.Lead);
                if (f.HasFrightFrom != 0 && now - f.LastFrightAt <= FrightMemorySeconds) { fright = true; from = f.FrightFrom; }
            }
            var rng = SimRng.FromSim((int)herd.Index, 0x4623, (float)now);
            Vector3 best = centre;
            float bestScore = float.NegativeInfinity;
            int tries = fright ? Candidates : 1;
            for (int i = 0; i < tries; i++)
            {
                float ang = rng.NextSingle() * MathF.Tau;
                float r = MathF.Sqrt(rng.NextSingle()) * radius * 0.85f;
                var p = new Vector3(centre.X + MathF.Cos(ang) * r, centre.Y + MathF.Sin(ang) * r, centre.Z);
                float score = fright ? Flat(p - from) : 0f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        /// <summary>Night: the scenario clock's sun below <see cref="NightSunDeg"/> at the world's origin. No clock ⇒ never night.</summary>
        public static bool IsNight(EntityRepository repo, double simTime)
        {
            if (!ScenarioTime.TryClock(repo, out var clock)) return false;
            var utc = ScenarioTime.Utc(in clock, simTime);
            double lat = 0, lon = 0;
            if (repo.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                && repo.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>() is { HasFrame: true } geo)
                (lat, lon, _) = geo.ToGeodetic(Vector3.Zero);
            return SolarPosition.Of(utc, lat, lon).ElevationDeg < NightSunDeg;
        }
    }
}
