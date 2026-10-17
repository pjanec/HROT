using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2237</c> (CIV-3, CV-5; docs/DESIGN_Civilians_And_Crowd_Control.md §5f) — one crowd: the civilians standing linked within
    /// <see cref="CrowdRules.LinkMetres"/> of each other, read out by <see cref="CrowdSystem"/> every frame. A BODILESS entity: no
    /// position component, no network id, never saved. ⛔ BRAIN-LOCAL like <see cref="CivilianState"/>: it lives on the node that runs its
    /// members' brains and no other node holds it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Crowd)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Crowd
    {
        /// <summary>The crowd's id — what each member's <see cref="CivilianState.CrowdId"/> holds. Never 0.</summary>
        public int Id;
        public int Count;
        /// <summary>The mean position of its members.</summary>
        public Vector3 Centroid;
        public float MeanFear, MeanAnger;
        /// <summary>The incident most of its members are drawn to; valid when <see cref="HasFocus"/> is 1.</summary>
        public Vector3 Focus;
        public byte HasFocus;
        /// <summary>Sim time (s) the crowd first formed.</summary>
        public double FormedAt;
    }

    /// <summary>⭐ <c>CE-2237</c> (§5f) — the crowd's numbers, one place.</summary>
    public static class CrowdRules
    {
        /// <summary>Two civilians this close are in the same crowd (chained: a crowd is every civilian linked to another).</summary>
        public const float LinkMetres = 15f;
        /// <summary>Fewer linked civilians than this are not a crowd.</summary>
        public const int MinMembers = 3;
        /// <summary>A calm civilian this close to an incident (a person hurt) walks over to look.</summary>
        public const float AttractMetres = 80f;
        /// <summary>An onlooker stands on a ring this far from the incident, plus up to <see cref="RingJitterMetres"/>.</summary>
        public const float RingMetres = 4f;
        public const float RingJitterMetres = 5f;
        /// <summary>A frightened member frightens the members of his crowd this close to him.</summary>
        public const float ContagionMetres = 20f;
        /// <summary>The most of a member's fear contagion passes on: a wave weakens with every hop and two neighbours cannot hold each
        /// other frightened (each fades below the other).</summary>
        public const float ContagionShare = 0.9f;
        /// <summary>How fast a man's fear rises toward what a frightened neighbour beside him shows (per second); half at the edge of
        /// <see cref="ContagionMetres"/>.</summary>
        public const float ContagionRate = 1.5f;
        /// <summary>⭐ <c>CE-2238</c> (§5i) — how fast an instigator raises the anger of a crowd member beside him (per second), half at the
        /// edge of <see cref="ContagionMetres"/>.</summary>
        public const float InstigateRate = 0.15f;
        /// <summary>⭐ <c>CE-2238</c> — how fast a man's anger fades with no instigator near him (per second).</summary>
        public const float AngerFade = 0.02f;

        /// <summary>The modes a member SHOWS fear in — the ones his neighbours catch it from.</summary>
        public static bool ShowsFear(CivilianMode mode)
            => mode is CivilianMode.Alarmed or CivilianMode.Panic or CivilianMode.Shelter or CivilianMode.Cower or CivilianMode.Dispersing;

        /// <summary>The modes a civilian may be drawn to an incident in (calm, and not hurt).</summary>
        public static bool MayLook(CivilianMode mode) => mode is CivilianMode.Routine or CivilianMode.Gather;
    }

    /// <summary>
    /// ⭐ <c>CE-2237</c> (CIV-3, CV-5; docs/DESIGN_Civilians_And_Crowd_Control.md §5f) — every frame, on the node that runs the civilians'
    /// brains, after <see cref="CivilianFearSystem"/> and before the brain tick: ① the incidents (a person whose injury is past Ok) and,
    /// for each calm civilian, the nearest within <see cref="CrowdRules.AttractMetres"/> (<see cref="CivilianState.Focus"/>, which his
    /// <c>CivilianHsm</c>'s Gather state walks to); ② the crowds — civilians linked within <see cref="CrowdRules.LinkMetres"/>, at least
    /// <see cref="CrowdRules.MinMembers"/> of them, each keeping the id most of its members had; ③ each crowd's <see cref="Crowd"/> entity
    /// (mean fear and anger, focus); ④ contagion: a member's fear rises toward what a frightened member of his crowd nearby shows;
    /// ⑤ <c>CE-2238</c> (§5i) anger: an instigator raises the anger of his crowd near him; with none near, anger fades.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class CrowdSystem : IEcsModuleSystem
    {
        private readonly List<Entity> _civs = new();
        private readonly List<Vector3> _at = new();
        private readonly List<Vector3> _incidents = new();
        private readonly List<Entity> _incidentOf = new();
        private int[] _parent = Array.Empty<int>();
        private readonly Dictionary<int, List<int>> _groups = new();
        private readonly Dictionary<int, int> _votes = new();
        private readonly Dictionary<int, Entity> _crowdEntity = new();
        private readonly HashSet<int> _live = new();
        private int _nextId = 1;

        /// <summary>The crowd entity of id <paramref name="crowdId"/> on this node, or <see cref="Entity.Null"/>.</summary>
        public Entity EntityOf(int crowdId) => _crowdEntity.TryGetValue(crowdId, out var e) ? e : Entity.Null;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<CivilianState>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;

            _civs.Clear();
            _at.Clear();
            foreach (var e in repo.Query().With<CivilianState>().With<SimTransform>().Build())
            {
                _civs.Add(e);
                _at.Add(repo.GetComponentRO<SimTransform>(e).Position);
            }

            Attract(repo);
            Link();
            Assign(repo, now);
            Spread(repo, deltaTime);
            Rouse(repo, deltaTime);
        }

        // ① incidents and who is drawn to them
        private void Attract(EntityRepository repo)
        {
            _incidents.Clear();
            _incidentOf.Clear();
            if (repo.IsComponentTypeRegistered<Health>() && repo.IsComponentTypeRegistered<InjuryThresholds>())
                foreach (var e in repo.Query().With<Health>().With<InjuryThresholds>().With<SimTransform>().Build())
                    if (Injury.StateOf(repo, e) != InjuryState.Ok)
                    {
                        _incidents.Add(repo.GetComponentRO<SimTransform>(e).Position);
                        _incidentOf.Add(e);
                    }

            float reach = CrowdRules.AttractMetres * CrowdRules.AttractMetres;
            for (int i = 0; i < _civs.Count; i++)
            {
                ref var mind = ref repo.GetComponentRW<CivilianState>(_civs[i]);
                mind.HasFocus = 0;
                if (!CrowdRules.MayLook(mind.Mode)) continue;
                float best = reach;
                for (int k = 0; k < _incidents.Count; k++)
                {
                    if (_incidentOf[k] == _civs[i]) continue;
                    float d = Flat2(_at[i], _incidents[k]);
                    if (d > best) continue;
                    best = d;
                    mind.Focus = _incidents[k];
                    mind.HasFocus = 1;
                }
            }
        }

        // ② who stands linked to whom (union-find over the pairs within LinkMetres)
        private void Link()
        {
            int n = _civs.Count;
            if (_parent.Length < n) _parent = new int[Math.Max(n, _parent.Length * 2)];
            for (int i = 0; i < n; i++) _parent[i] = i;
            float link = CrowdRules.LinkMetres * CrowdRules.LinkMetres;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (Flat2(_at[i], _at[j]) <= link) Union(i, j);

            foreach (var g in _groups.Values) g.Clear();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!_groups.TryGetValue(root, out var list)) _groups[root] = list = new List<int>();
                list.Add(i);
            }
        }

        // ③ each group of MinMembers or more is a crowd; it keeps the id most of its members had
        private void Assign(EntityRepository repo, double now)
        {
            _live.Clear();
            foreach (var group in _groups.Values)
            {
                if (group.Count == 0) continue;
                if (group.Count < CrowdRules.MinMembers)
                {
                    foreach (int i in group) repo.GetComponentRW<CivilianState>(_civs[i]).CrowdId = 0;
                    continue;
                }

                _votes.Clear();
                foreach (int i in group)
                {
                    int had = repo.GetComponentRO<CivilianState>(_civs[i]).CrowdId;
                    if (had != 0) _votes[had] = _votes.TryGetValue(had, out int v) ? v + 1 : 1;
                }
                int id = 0, most = 0;
                foreach (var (candidate, votes) in _votes)
                    if (!_live.Contains(candidate) && (votes > most || (votes == most && candidate < id))) { id = candidate; most = votes; }
                if (id == 0) id = _nextId++;
                _live.Add(id);

                var crowd = new Crowd { Id = id, Count = group.Count };
                int focusVotes = 0;
                foreach (int i in group)
                {
                    ref var mind = ref repo.GetComponentRW<CivilianState>(_civs[i]);
                    mind.CrowdId = id;
                    crowd.Centroid += _at[i];
                    crowd.MeanFear += mind.Fear;
                    crowd.MeanAnger += mind.Anger;
                    if (mind.HasFocus != 0)
                    {
                        if (focusVotes == 0) crowd.Focus = mind.Focus;
                        focusVotes++;
                    }
                }
                crowd.Centroid /= group.Count;
                crowd.MeanFear /= group.Count;
                crowd.MeanAnger /= group.Count;
                crowd.HasFocus = focusVotes * 2 >= group.Count ? (byte)1 : (byte)0;

                if (!_crowdEntity.TryGetValue(id, out var ce) || !repo.IsAlive(ce))
                {
                    ce = repo.CreateEntity();
                    crowd.FormedAt = now;
                    repo.AddComponent(ce, crowd);
                    Fdp.Toolkit.Scenario.DerivedParts.MarkNotSaved(repo, ce);
                    _crowdEntity[id] = ce;
                }
                else
                {
                    crowd.FormedAt = repo.GetComponentRO<Crowd>(ce).FormedAt;
                    repo.GetComponentRW<Crowd>(ce) = crowd;
                }
            }

            List<int>? gone = null;
            foreach (var (id, ce) in _crowdEntity)
                if (!_live.Contains(id)) (gone ??= new List<int>()).Add(id);
            if (gone != null)
                foreach (int id in gone)
                {
                    if (repo.IsAlive(_crowdEntity[id])) repo.DestroyEntity(_crowdEntity[id]);
                    _crowdEntity.Remove(id);
                }
        }

        // ④ contagion: toward ContagionShare of the most frightened member of his crowd nearby, faster the nearer he is
        private void Spread(EntityRepository repo, float dt)
        {
            if (dt <= 0f) return;
            float reach = CrowdRules.ContagionMetres * CrowdRules.ContagionMetres;
            foreach (var group in _groups.Values)
            {
                if (group.Count < CrowdRules.MinMembers) continue;
                foreach (int i in group)
                {
                    var me = repo.GetComponentRO<CivilianState>(_civs[i]);
                    float bestPull = 0f, bestTarget = 0f;
                    int source = -1;
                    foreach (int j in group)
                    {
                        if (j == i) continue;
                        ref readonly var other = ref repo.GetComponentRO<CivilianState>(_civs[j]);
                        if (!CrowdRules.ShowsFear(other.Mode)) continue;
                        float d2 = Flat2(_at[i], _at[j]);
                        if (d2 > reach) continue;
                        float target = CrowdRules.ContagionShare * other.Fear;
                        if (target <= me.Fear) continue;
                        float near = 1f - 0.5f * MathF.Sqrt(d2) / CrowdRules.ContagionMetres;
                        float pull = near * (target - me.Fear);
                        if (pull <= bestPull) continue;
                        bestPull = pull;
                        bestTarget = target;
                        source = j;
                    }
                    if (source < 0) continue;
                    ref var mind = ref repo.GetComponentRW<CivilianState>(_civs[i]);
                    mind.Fear = MathF.Min(bestTarget, mind.Fear + CrowdRules.ContagionRate * bestPull * dt);
                    ref readonly var src = ref repo.GetComponentRO<CivilianState>(_civs[source]);
                    if (src.HasFrightFrom != 0)
                    {
                        mind.FrightFrom = src.FrightFrom;
                        mind.HasFrightFrom = 1;
                    }
                }
            }
        }

        // ⑤ anger (CE-2238): an instigator's is 1; a member near one rises toward 1, faster the nearer; everyone else's fades
        private void Rouse(EntityRepository repo, float dt)
        {
            if (dt <= 0f) return;
            float reach = CrowdRules.ContagionMetres * CrowdRules.ContagionMetres;
            for (int i = 0; i < _civs.Count; i++)
            {
                ref var mind = ref repo.GetComponentRW<CivilianState>(_civs[i]);
                if (mind.Instigator != 0) { mind.Anger = 1f; continue; }
                float best = 0f;
                if (mind.CrowdId != 0 && _groups.TryGetValue(Find(i), out var group) && group.Count >= CrowdRules.MinMembers)
                    foreach (int j in group)
                    {
                        if (j == i || repo.GetComponentRO<CivilianState>(_civs[j]).Instigator == 0) continue;
                        float d2 = Flat2(_at[i], _at[j]);
                        if (d2 > reach) continue;
                        best = MathF.Max(best, 1f - 0.5f * MathF.Sqrt(d2) / CrowdRules.ContagionMetres);
                    }
                mind.Anger = best > 0f ? MathF.Min(1f, mind.Anger + CrowdRules.InstigateRate * best * dt)
                                       : MathF.Max(0f, mind.Anger - CrowdRules.AngerFade * dt);
            }
        }

        private int Find(int i)
        {
            while (_parent[i] != i) i = _parent[i] = _parent[_parent[i]];
            return i;
        }

        private void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) _parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        private static float Flat2(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
