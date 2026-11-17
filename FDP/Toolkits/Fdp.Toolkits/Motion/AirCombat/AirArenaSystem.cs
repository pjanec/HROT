using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ CE-10004 (air combat AC-10 / AC-11 / AC-12; docs/designs/air-attack/DESIGN_Air_Combat.md §3, §4b) — THE ARENA'S ONE WRITER and the
    /// brains' index of it. A SERIAL pre-pass on the brain node (<c>BrainPhase.PrepareToolkitFrame</c>, as <see cref="DroneJamming"/>): it
    /// stamps <see cref="AirArena.LastHitAt"/> when a fighter inside an arena lost health since the last frame (the stalemate clock), and
    /// indexes the live arenas so a fighter's brain in the parallel phase only READS them (<see cref="Containing"/>; R-4212: no lazy index
    /// mid-tick). No allocation once warm (the arrays grow, never shrink).
    /// </summary>
    public sealed class AirArenaSystem
    {
        private static readonly ConditionalWeakTable<EntityRepository, AirArenaSystem> s_of = new();

        private EntityQuery? _arenas, _fighters;
        private Entity[] _arena = new Entity[2];
        private AirArena[] _data = new AirArena[2];
        private Entity[] _fighter = new Entity[8];
        private float[] _health = new float[8];
        private int _fighterCount;

        /// <summary>Arenas live this frame.</summary>
        public int Count { get; private set; }

        public static AirArenaSystem Of(EntityRepository repo) => s_of.GetValue(repo, static _ => new AirArenaSystem());

        /// <summary>The serial pre-pass (the brain node, before the brain phase).</summary>
        public static void PrepareFrame(EntityRepository repo) => Of(repo).Rebuild(repo);

        private void Rebuild(EntityRepository repo)
        {
            Count = 0;
            if (!repo.IsComponentTypeRegistered<AirArena>()) return;
            _arenas ??= repo.Query().With<AirArena>().Build();
            foreach (var e in _arenas)
            {
                if (Count == _arena.Length) { Array.Resize(ref _arena, Count * 2); Array.Resize(ref _data, Count * 2); }
                _arena[Count] = e;
                _data[Count] = repo.GetComponentRO<AirArena>(e);
                Count++;
            }
            if (Count == 0 || !repo.IsComponentTypeRegistered<AirCombatState>() || !repo.IsComponentTypeRegistered<Health>()) return;
            SimInstant now = repo.HasSingleton<GlobalTime>() ? (SimInstant)repo.GetSingleton<GlobalTime>().TotalTime : SimInstant.Zero;
            _fighters ??= repo.Query().With<AirCombatState>().With<Health>().With<SimTransform>().Build();
            foreach (var f in _fighters)
            {
                float h = repo.GetComponentRO<Health>(f).Current;
                int slot = -1;
                for (int i = 0; i < _fighterCount && slot < 0; i++) if (_fighter[i] == f) slot = i;
                if (slot < 0)
                {
                    if (_fighterCount == _fighter.Length) { Array.Resize(ref _fighter, _fighterCount * 2); Array.Resize(ref _health, _fighterCount * 2); }
                    slot = _fighterCount++;
                    _fighter[slot] = f;
                    _health[slot] = h;
                    continue;
                }
                bool hit = h < _health[slot] - 1e-3f;
                _health[slot] = h;
                if (!hit) continue;
                int a = IndexContaining(repo.GetComponentRO<SimTransform>(f).Position);
                if (a < 0) continue;
                ref var arena = ref repo.GetComponentRW<AirArena>(_arena[a]);   // ⭐ the ONE writer of the stalemate clock
                arena.LastHitAt = now;
                _data[a].LastHitAt = now;
            }
        }

        private int IndexContaining(Vector3 at)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                float r = _data[i].RadiusM > 0f ? _data[i].RadiusM : AirArena.DefaultRadiusM;
                float d = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(_data[i].Centre.X, _data[i].Centre.Y)) / r;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best >= 0 && bestD <= 1.5f ? best : -1;   // the soft edge: a fighter just outside still belongs to it
        }

        /// <summary>The arena <paramref name="at"/> fights in (the nearest whose radius × 1.5 holds it; a soft edge), false when none. Reads the
        /// frame's index only — safe in the parallel brain phase.</summary>
        public static bool Containing(EntityRepository repo, Vector3 at, out Entity arena, out AirArena data)
        {
            var s = Of(repo);
            int i = s.IndexContaining(at);
            arena = i >= 0 ? s._arena[i] : Entity.Null;
            data = i >= 0 ? s._data[i] : default;
            return i >= 0;
        }
    }
}
