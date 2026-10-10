using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-3117</c> (R-226) — mirrors the combat logs into the RECORDED trace singletons the map draws: new and ended shots of
    /// <see cref="ShotLog"/> into <see cref="ShotTraces"/>, new bursts of <see cref="DetonationLog"/> into <see cref="DetonationTraces"/>.
    /// The logs stay the only producers (and keep the text the routes serve); this copies what the map needs, stamped with sim time,
    /// so a replay seek restores what the map showed. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// <para>⚠ Writes go through <c>SetSingletonUnmanaged</c>, never the ref from <c>GetSingletonUnmanaged</c>: only the former moves
    /// the version, and a delta frame records a singleton only when its version moved (<c>RecorderSystem.RecordSingletons</c>).</para>
    /// <para>Runs after <see cref="BallisticsSystem"/> in the combat module's post-simulation list, so it is off during replay with
    /// the systems that feed it — a restored ring is never topped up from a live log.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class CombatTraceSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            if (ShotLog.Peek(repo) is { } shots) MirrorShots(repo, shots, now);
            if (DetonationLog.Peek(repo) is { } bursts) MirrorDetonations(repo, bursts, now);
        }

        /// <summary>Copies new shots and the end of shots that were still in flight; writes only when something changed.</summary>
        public static void MirrorShots(EntityRepository repo, ShotLog log, double now)
        {
            bool has = repo.HasSingletonUnmanaged<ShotTraces>();
            if (!has && log.Count == 0) return;
            var t = has ? repo.GetSingletonUnmanaged<ShotTraces>() : default;   // a COPY — written back with Set below
            bool changed = false;
            var slots = t.SlotsRW();

            for (int i = 0; i < t.Count; i++)
            {
                ref var s = ref slots[i];
                if (s.Outcome != ShotOutcome.InFlight || log.At(s.Seq) is not { } r || r.Outcome == ShotOutcome.InFlight) continue;
                Fill(ref s, r, s.Time);
                changed = true;
            }

            long from = Math.Max(t.NextSeq, log.Count - ShotTraces.Capacity);
            for (long seq = from; seq < log.Count; seq++)
            {
                if (log.At(seq) is not { } r) continue;
                ref var s = ref slots[t.Next];
                s = default;
                Fill(ref s, r, now);
                t.Next = (t.Next + 1) % ShotTraces.Capacity;
                t.Count = Math.Min(t.Count + 1, ShotTraces.Capacity);
                changed = true;
            }
            if (t.NextSeq != log.Count) { t.NextSeq = log.Count; changed = true; }
            if (changed) repo.SetSingletonUnmanaged(in t);
        }

        private static void Fill(ref ShotTrace s, ShotRecord r, double time)
        {
            s.Seq = r.Seq;
            s.Time = time;
            s.Muzzle = r.Muzzle;
            s.Outcome = r.Outcome;
            s.HasEnd = (byte)(r.EndPoint.HasValue ? 1 : 0);
            s.End = r.EndPoint ?? r.Aim;
            int n = Math.Min(r.Crossings.Count, ShotTraces.CrossingsPerShot);
            s.CrossingCount = (byte)n;
            for (int c = 0; c < n; c++)
                s.Crossings[c] = new ShotTraceCrossing { At = r.Crossings[c].At, Passed = (byte)(r.Crossings[c].Passed ? 1 : 0) };
        }

        /// <summary>Copies new bursts (a burst is complete when it is logged).</summary>
        public static void MirrorDetonations(EntityRepository repo, DetonationLog log, double now)
        {
            bool has = repo.HasSingletonUnmanaged<DetonationTraces>();
            if (!has && log.Count == 0) return;
            var t = has ? repo.GetSingletonUnmanaged<DetonationTraces>() : default;
            if (t.NextSeq == log.Count) return;
            var slots = t.SlotsRW();

            for (long seq = Math.Max(t.NextSeq, log.Count - DetonationTraces.Capacity); seq < log.Count; seq++)
            {
                if (log.At(seq) is not { } r) continue;
                ref var d = ref slots[t.Next];
                d = default;
                d.Seq = r.Seq;
                d.Time = now;
                d.Burst = r.Burst;
                d.FragmentRadius = r.FragmentRadius;
                d.BlastInjuryRadius = r.BlastInjuryRadius;
                d.TargetCount = Math.Min(r.Effects.Count, DetonationTraces.TargetsPerBurst);
                for (int i = 0; i < d.TargetCount; i++)
                {
                    var e = r.Effects[i];
                    d.Targets[i] = new DetonationTarget { At = e.At, Exposure = e.FragmentExposure, Damage = e.TotalDamage };
                }
                d.RayCount = Math.Min(r.Rays.Count, DetonationTraces.RaysPerBurst);
                for (int i = 0; i < d.RayCount; i++)
                {
                    var ray = r.Rays[i];
                    d.Rays[i] = new DetonationRay
                    {
                        To = ray.To, Transmission = ray.Transmission,
                        HasStop = (byte)(ray.StopAt.HasValue ? 1 : 0), StopAt = ray.StopAt ?? ray.To,
                    };
                }
                t.Next = (t.Next + 1) % DetonationTraces.Capacity;
                t.Count = Math.Min(t.Count + 1, DetonationTraces.Capacity);
            }
            t.NextSeq = log.Count;
            repo.SetSingletonUnmanaged(in t);
        }
    }
}
