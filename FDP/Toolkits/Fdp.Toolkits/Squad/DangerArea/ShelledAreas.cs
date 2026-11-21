using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Tkb.Parameters;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐⭐ <c>CE-4583</c> (AQ101 §R D, R-2071 E-4, R-4227) — <b>the shelled area</b>: where bursts keep landing. Each burst adds heat at its point,
    /// merged with a point within <see cref="MergeM"/>; <see cref="BurstsToShell"/> bursts within <see cref="WindowSeconds"/> make that point
    /// SHELLED, and it cools — and is forgotten — <see cref="CoolSeconds"/> after its last burst. A shelled point is a
    /// <see cref="DangerAreaKind.Shelled"/> circle of <see cref="RadiusM"/> for the danger-area solve (<see cref="Footprints"/>), the way a tear-gas
    /// cloud is (<see cref="GasFootprints"/>, CE-3227): the march's sensor sees it on its route and the crossing decision holds or crosses.
    /// <para>⚠ Held on the node that SOLVES the danger areas (the Muscle, with the navmesh) — not in the brain's squad pool, as AQ101 §R D first said:
    /// the solve runs where the navmesh is, the bursts are local there (the Muscle runs the ballistics; remote ones arrive as events), and the
    /// gas precedent is node-level the same way. Per world, beside <c>DetonationLog</c> (no component id: a restore forgets at most
    /// <see cref="CoolSeconds"/> of shelling). 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md (the danger-area table).</para>
    /// </summary>
    public sealed class ShelledAreas
    {
        /// <summary>A burst this close (m) to a point's centre adds to it.</summary>
        public const float MergeM = 15f;
        /// <summary>How many bursts make a point shelled …</summary>
        public const int BurstsToShell = 3;
        /// <summary>… within this many seconds.</summary>
        public const double WindowSeconds = 60.0;
        /// <summary>A point with no burst for this long (s) is no longer shelled, and is forgotten.</summary>
        public const double CoolSeconds = 120.0;
        /// <summary>The shelled area's radius (m): the merge radius and a margin — a man at its edge is already under it.</summary>
        public const float RadiusM = MergeM + 10f;
        /// <summary>How many points are kept (the oldest goes first).</summary>
        public const int Capacity = 8;
        /// <summary>⭐ CE-2468 UF-7 (DESIGN_Minefields.md §9b, K8) — a side with a live unit this close (m, 2-D) to a shelled point WITNESSES it (a burst is heard far).</summary>
        public const float WitnessMetres = 1500f;
        /// <summary>⭐ CE-2468 UF-7 — while a point stays shelled, its witnesses are taught again this often (s): a side that comes within range learns it; a refresh
        /// carries <c>At</c> = the last BURST (K6), so a side that already knows it changes nothing.</summary>
        public const double RefreshSeconds = 20.0;

        /// <summary>One point of heat.</summary>
        public struct Point
        {
            public int Id;
            public Vector3 Centre;
            public int Bursts;
            /// <summary>The newest burst times (s), newest first; NaN = none.</summary>
            public double T0, T1, T2;
            public bool Shelled;
            /// <summary>⭐ UF-7 — the sim time its witnesses were last taught (NaN = never) and the burst time they were taught.</summary>
            public double TaughtAt, TaughtT0;
        }

        private static readonly ConditionalWeakTable<EntityRepository, ShelledAreas> s_byWorld = new();

        /// <summary>The world's shelled areas (created on first use).</summary>
        public static ShelledAreas For(EntityRepository world) => s_byWorld.GetValue(world, _ => new ShelledAreas());

        /// <summary>The world's shelled areas, or <see langword="null"/> when no burst was ever noted.</summary>
        public static ShelledAreas? Peek(EntityRepository world) => s_byWorld.TryGetValue(world, out var s) ? s : null;

        private readonly Point[] _points = new Point[Capacity];
        private int _count, _nextId = 1;

        /// <summary>The points held now.</summary>
        public ReadOnlySpan<Point> Points => _points.AsSpan(0, _count);

        /// <summary>A burst at <paramref name="at"/> at sim time <paramref name="now"/> (s).</summary>
        public void AddBurst(Vector3 at, double now)
        {
            int best = -1;
            float bestD = MergeM;
            for (int i = 0; i < _count; i++)
            {
                float d = Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(_points[i].Centre.X, _points[i].Centre.Y));
                if (d <= bestD) { bestD = d; best = i; }
            }
            if (best < 0)
            {
                if (_count == Capacity) RemoveAt(OldestIndex());
                best = _count++;
                _points[best] = new Point { Id = _nextId++, Centre = at, T0 = double.NaN, T1 = double.NaN, T2 = double.NaN, TaughtAt = double.NaN, TaughtT0 = double.NaN };
            }
            ref var p = ref _points[best];
            p.Bursts++;
            p.Centre += (at - p.Centre) / Math.Min(p.Bursts, 255);   // the bursts' mean
            p.T2 = p.T1; p.T1 = p.T0; p.T0 = now;
            if (!double.IsNaN(p.T2) && p.T0 - p.T2 <= WindowSeconds) p.Shelled = true;
        }

        /// <summary>Forgets every point whose last burst is more than <see cref="CoolSeconds"/> old.</summary>
        public void Cool(double now)
        {
            for (int i = _count - 1; i >= 0; i--)
                if (now - _points[i].T0 > CoolSeconds) RemoveAt(i);
        }

        /// <summary>
        /// Writes a <see cref="DangerAreaKind.Shelled"/> footprint for every shelled point into <paramref name="into"/>; returns how many. No
        /// world (or no shelling) ⇒ 0. The footprint's id is the point's (stable while it stays shelled).
        /// </summary>
        public static int Footprints(EntityRepository repo, Span<GasFootprint> into)
        {
            var s = Peek(repo);
            if (s == null || into.Length == 0) return 0;
            int n = 0;
            for (int i = 0; i < s._count && n < into.Length; i++)
            {
                ref readonly var p = ref s._points[i];
                if (!p.Shelled) continue;
                var c = new Vector2(p.Centre.X, p.Centre.Y);
                into[n++] = new GasFootprint(p.Id, c, c, RadiusM, p.Centre.Z - 10f, p.Centre.Z + 10f) { Kind = DangerAreaKind.Shelled };
            }
            return n;
        }

        /// <summary>
        /// ⭐⭐ CE-2468 UF-7 (DESIGN_Minefields.md §9b, K6, K8; closes CE-4626 — the shelled area had no side) — teach every WITNESS side (a live unit within
        /// <see cref="WitnessMetres"/>) each shelled point that is new, has a new burst, or is due its <see cref="RefreshSeconds"/> refresh:
        /// <see cref="Fdp.Toolkit.Terrain.UnderFireEvents.LearnIndirect"/> with <c>At</c> = the point's last burst. Serial; 0 B (one index walk per point taught).
        /// </summary>
        public void Teach(EntityRepository repo, double now)
        {
            if (_count == 0 || !repo.IsComponentTypeRegistered<EntityInfo>() || !repo.Bus.IsRegistered<Fdp.Toolkit.Terrain.UnderFireLearned>()) return;
            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _points[i];
                if (!p.Shelled) continue;
                bool fresh = p.TaughtT0 != p.T0 || double.IsNaN(p.TaughtAt) || now - p.TaughtAt >= RefreshSeconds;
                if (!fresh) continue;
                p.TaughtAt = now; p.TaughtT0 = p.T0;
                ulong lo = 0, hi = 0;   // the witness sides (ForceId 0..127)
                var c = new Vector2(p.Centre.X, p.Centre.Y);
                foreach (var e in repo.Query().With<EntityInfo>().With<SimTransform>().Build())
                {
                    int side = (int)repo.GetComponentRO<EntityInfo>(e).ForceId;
                    if (side > 127 || ((side < 64 ? lo >> side : hi >> (side - 64)) & 1UL) != 0) continue;
                    var q = repo.GetComponentRO<SimTransform>(e).Position;
                    if (Vector2.DistanceSquared(new Vector2(q.X, q.Y), c) > WitnessMetres * WitnessMetres || !Fdp.Toolkit.Combat.CombatLife.IsAlive(repo, e)) continue;
                    if (side < 64) lo |= 1UL << side; else hi |= 1UL << (side - 64);
                }
                for (int side = 0; side < 128; side++)
                    if (((side < 64 ? lo >> side : hi >> (side - 64)) & 1UL) != 0)
                        Fdp.Toolkit.Terrain.UnderFireEvents.LearnIndirect(repo, side, p.Centre, RadiusM, (SimInstant)p.T0);
            }
        }

        private int OldestIndex()
        {
            int oldest = 0;
            for (int i = 1; i < _count; i++) if (_points[i].T0 < _points[oldest].T0) oldest = i;
            return oldest;
        }

        private void RemoveAt(int i)
        {
            _points[i] = _points[--_count];
            _points[_count] = default;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4583</c> — notes every burst with an area effect (a local or a remote <see cref="DetonationNotification"/> whose warhead has one)
    /// into <see cref="ShelledAreas"/>, and cools the old points. Serial, on the node that solves the danger areas; no allocation per frame.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ShelledAreaSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<DetonationNotification>()) return;
            double now = Fdp.Toolkit.Time.SimClock.Of(repo).TotalTime;
            var events = repo.ReadEvents<DetonationNotification>();
            var db = events.Length > 0 && repo.HasSingletonManaged<ITkbDatabase>() ? repo.GetSingletonManaged<ITkbDatabase>() : null;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.Ammo == 0 || db == null) continue;
                var (warhead, _, _) = ParameterResolver.Warhead(db, evt.Ammo);
                if (warhead == null || !warhead.HasAreaEffect) continue;   // a bullet, or a smoke / gas canister with no burst
                ShelledAreas.For(repo).AddBurst(new Vector3(evt.HitX, evt.HitY, evt.HitZ), now);
            }
            var shelled = ShelledAreas.Peek(repo);
            shelled?.Teach(repo, now);   // ⭐ CE-2468 UF-7 — the witness sides learn it (the danger solve reads THEIR knowledge, UF-6)
            shelled?.Cool(now);
        }
    }
}
