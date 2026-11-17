using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ CE-10002 (AC-9a, RV-2; docs/designs/air-attack/DESIGN_Air_Combat.md §3) — THE LIVE THREAT. The motion node holds the guided
    /// rounds (they are not replicated), so once per tick it gathers the homing ones (target, position, velocity, seeker) and each aircraft
    /// reads the rounds aimed at IT into its replicated <see cref="FighterPilotState"/>: how many are inbound, the nearest's bearing, seeker,
    /// closure and live time to impact. The positions are the TICK-START ones (gathered before any aircraft of this tick moved).
    /// Serial (the motion node's <see cref="AirMotionSystem"/>); buffers grow, never shrink — no allocation in the steady state.
    /// </summary>
    public static class ThreatScan
    {
        /// <summary>Inside this (s to impact) the motion node breaks across the missile's path whatever the brain asked (AC-9, the reflex).</summary>
        public const float LastDitchS = 2.5f;

        // per thread (CLAUDE.md: no shared static scratch — a test host steps several worlds at once)
        [ThreadStatic] private static EntityRepository? _repo;
        [ThreadStatic] private static long _frame;
        [ThreadStatic] private static EntityQuery? _query;
        [ThreadStatic] private static Entity[]? _target;
        [ThreadStatic] private static Vector3[]? _pos, _vel;
        [ThreadStatic] private static GuidanceKind[]? _seeker;
        [ThreadStatic] private static int _count;
        // ⭐ CE-10003 (RV-6) — the mode-8 targets' TICK-START positions: the motion node is serial, so a target later in the loop has not
        //   moved yet and one earlier has — every fighter reads the state all of them started this tick with
        [ThreadStatic] private static EntityQuery? _fighters;
        [ThreadStatic] private static Entity[]? _tgt;
        [ThreadStatic] private static Vector3[]? _tgtPos, _tgtVel;
        [ThreadStatic] private static int _tgtCount;

        /// <summary>Gathers this tick's homing rounds once (later calls in the same tick reuse them).</summary>
        public static void Begin(EntityRepository repo)
        {
            if (_target == null) { _target = new Entity[16]; _pos = new Vector3[16]; _vel = new Vector3[16]; _seeker = new GuidanceKind[16]; }
            long frame = repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanagedRO<GlobalTime>().FrameNumber : long.MinValue + 1;
            if (ReferenceEquals(repo, _repo) && frame == _frame && frame != long.MinValue + 1) return;   // no clock (a rail) ⇒ gather every call
            if (!ReferenceEquals(repo, _repo)) { _query = null; _fighters = null; }
            _repo = repo; _frame = frame; _count = 0;
            GatherTargets(repo);
            if (!repo.IsComponentTypeRegistered<GuidedRound>()) return;
            _query ??= repo.Query().With<GuidedRound>().With<SimTransform>().With<SimVelocity>().Build();
            foreach (var e in _query)
            {
                ref readonly var g = ref repo.GetComponentRO<GuidedRound>(e);
                if (g.Guidance is not (GuidanceKind.InfraRed or GuidanceKind.Radar) || g.Target.IsNull || g.Phase == GuidedPhase.Lost) continue;
                if (_count == _target!.Length)
                {
                    Array.Resize(ref _target, _count * 2); Array.Resize(ref _pos!, _count * 2);
                    Array.Resize(ref _vel!, _count * 2); Array.Resize(ref _seeker!, _count * 2);
                }
                _target![_count] = g.Target;
                _pos![_count] = repo.GetComponentRO<SimTransform>(e).Position;
                _vel![_count] = repo.GetComponentRO<SimVelocity>(e).Linear;
                _seeker![_count] = g.Guidance;
                _count++;
            }
        }

        private static void GatherTargets(EntityRepository repo)
        {
            _tgtCount = 0;
            if (!repo.IsComponentTypeRegistered<AirCombatSteer>()) return;
            if (_tgt == null) { _tgt = new Entity[8]; _tgtPos = new Vector3[8]; _tgtVel = new Vector3[8]; }
            _fighters ??= repo.Query().With<AirCombatSteer>().Build();
            foreach (var e in _fighters)
            {
                var t = repo.GetComponentRO<AirCombatSteer>(e).Target;
                if (t.IsNull || !repo.IsAlive(t) || !repo.HasComponent<SimTransform>(t)) continue;
                bool known = false;
                for (int i = 0; i < _tgtCount && !known; i++) known = _tgt![i] == t;
                if (known) continue;
                if (_tgtCount == _tgt!.Length)
                {
                    Array.Resize(ref _tgt, _tgtCount * 2); Array.Resize(ref _tgtPos!, _tgtCount * 2); Array.Resize(ref _tgtVel!, _tgtCount * 2);
                }
                _tgt![_tgtCount] = t;
                _tgtPos![_tgtCount] = repo.GetComponentRO<SimTransform>(t).Position;
                _tgtVel![_tgtCount] = repo.HasComponent<SimVelocity>(t) ? repo.GetComponentRO<SimVelocity>(t).Linear : Vector3.Zero;
                _tgtCount++;
            }
        }

        /// <summary>⭐ CE-10003 (RV-6) — <paramref name="target"/>'s position and velocity as this tick STARTED (gathered by <see cref="Begin"/>);
        /// false when it is no mode-8 target (the caller reads it live).</summary>
        public static bool TickStart(Entity target, out Vector3 pos, out Vector3 vel)
        {
            for (int i = 0; i < _tgtCount; i++)
                if (_tgt![i] == target) { pos = _tgtPos![i]; vel = _tgtVel![i]; return true; }
            pos = default; vel = default;
            return false;
        }

        /// <summary>The rounds aimed at <paramref name="self"/> → <paramref name="pilot"/> (count, the nearest-in-time one's bearing, seeker,
        /// closure, time to impact). Returns the nearest's position (for the reflex), or false when none is inbound.</summary>
        public static bool Scan(Entity self, Vector3 pos, Vector3 vel, ref FighterPilotState pilot, out Vector3 threatPos, out Vector3 threatVel)
        {
            threatPos = default; threatVel = default;
            int n = 0; float best = float.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                if (_target![i] != self) continue;
                n++;
                var rel = pos - _pos![i];
                float d = rel.Length();
                float closure = d > 1e-3f ? Vector3.Dot(_vel![i] - vel, rel / d) : 0f;
                float tti = d / MathF.Max(closure, 1f);
                if (tti >= best) continue;
                best = tti;
                threatPos = _pos![i]; threatVel = _vel![i];
                var from = _pos[i] - pos;
                pilot.ThreatBearingDeg = (MathF.Atan2(from.X, from.Y) * 180f / MathF.PI + 360f) % 360f;
                pilot.ThreatSeeker = _seeker![i];
                pilot.ThreatClosureMps = closure;
                pilot.ThreatTtiS = tti;
            }
            pilot.Inbound = (byte)Math.Min(n, 255);
            if (n == 0) { pilot.ThreatTtiS = 0f; pilot.ThreatClosureMps = 0f; }
            return n > 0;
        }

        /// <summary>The reflex's direction: level, across the missile's line of flight, on the side the aircraft already turns to.</summary>
        public static Vector3 LastDitchDirection(Vector3 vel, Vector3 threatVel)
        {
            var m = new Vector3(threatVel.X, threatVel.Y, 0f);
            m = m.LengthSquared() > 1f ? Vector3.Normalize(m) : Vector3.UnitX;
            var across = new Vector3(-m.Y, m.X, 0f);
            return Vector3.Dot(across, vel) >= 0f ? across : -across;
        }
    }
}
