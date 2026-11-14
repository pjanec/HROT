using System.Numerics;
using Fdp.Core;

namespace Hrot.UI.Common.Audio
{
    /// <summary>
    /// ⭐ <c>CE-1216</c> (docs/DESIGN_Entity_Control.md §8k S-2; extracted from <see cref="SoundSources"/>, A2) — ROUNDS PASSING AN EAR:
    /// each round in flight (a <c>BallisticProjectile</c> or a replicated <c>LightweightRound</c>) whose path between two frames comes within
    /// <see cref="PassDistance"/> of the ear passes it ONCE; the ear's own rounds, and a round born beside the ear (a muzzle), never do. The
    /// audio plays the crack from it; the first-person station darkens the screen's edges from it — one rule, two listeners, each with its
    /// own instance (each remembers the rounds it has seen).
    /// </summary>
    public sealed class RoundPasses
    {
        /// <summary>How near a round must pass the ear (m).</summary>
        public const float PassDistance = 5f;

        private readonly Dictionary<Entity, Vector3> _last = new();
        private readonly HashSet<Entity> _passed = new();

        /// <summary>Forgets every round.</summary>
        public void Clear() { _last.Clear(); _passed.Clear(); }

        /// <summary>One frame: <paramref name="onPass"/> (the closest point, the distance) for each round that passes <paramref name="ear"/>
        /// now; <paramref name="own"/>'s rounds are skipped. Returns how many passed.</summary>
        public int Scan(EntityRepository w, Entity own, Vector3 ear, Action<Vector3, float>? onPass = null)
        {
            bool ballistic = w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
            bool light = w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>();
            if (!ballistic && !light) { Clear(); return 0; }
            var seen = new HashSet<Entity>();
            int passes = 0;
            if (ballistic) foreach (var e in w.Query().With<Fdp.Toolkit.Combat.Components.BallisticProjectile>().With<SimTransform>().Build())
                {
                    if (w.GetComponentRO<Fdp.Toolkit.Combat.Components.BallisticProjectile>(e).Shooter == own && !own.IsNull) continue;   // his own rounds leave him
                    passes += Round(w, e, ear, seen, onPass);
                }
            if (light) foreach (var e in w.Query().With<Fdp.Toolkit.Replication.Components.LightweightRound>().With<SimTransform>().Build())
                    passes += Round(w, e, ear, seen, onPass);
            foreach (var gone in new List<Entity>(_last.Keys)) if (!seen.Contains(gone)) { _last.Remove(gone); _passed.Remove(gone); }
            return passes;
        }

        private int Round(EntityRepository w, Entity e, Vector3 ear, HashSet<Entity> seen, Action<Vector3, float>? onPass)
        {
            seen.Add(e);
            var at = w.GetComponentRO<SimTransform>(e).Position;
            if (!_last.TryGetValue(e, out var before))
            {
                _last[e] = at;
                if (Vector3.Distance(at, ear) < PassDistance) _passed.Add(e);   // born at the ear: his own, or a muzzle beside him
                return 0;
            }
            _last[e] = at;
            if (_passed.Contains(e)) return 0;
            var (point, d) = Spatializer.Closest(before, at, ear);
            if (d >= PassDistance) return 0;
            _passed.Add(e);
            onPass?.Invoke(point, d);
            return 1;
        }
    }
}
