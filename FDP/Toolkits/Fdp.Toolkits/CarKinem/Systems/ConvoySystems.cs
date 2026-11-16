using System;
using System.Numerics;
using CarKinem.Convoy;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> (C-4, C-6; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md §3, §4.4) — per road-convoy LEAD (a <see cref="ConvoyMember"/> of rank 0), on the
    /// kinematic owner: records the lead's ACTUAL path into its own <see cref="ConvoyTrail"/>, gathers every member's place and speed on it (a declared read of
    /// the members — they write only their own <see cref="ConvoyProgress"/>), and caps the lead's speed while the column is stretched (string stability: the
    /// tail never falls further back). Writes only the lead's own components. 0 B per steady frame (cached query, fixed-size trail).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ConvoyLeadSystem : IEcsModuleSystem
    {
        /// <summary>The cap starts once the tail is this many gaps behind its place, and reaches 0 (the lead stops) at three times it.</summary>
        public const float SlackGaps = 1f;

        private EntityQuery? _members;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            // registered with the kinematic components (KinematicComponentRegistry) — never lazily mid-tick (D-26)
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ConvoyMember>() || !repo.IsComponentTypeRegistered<ConvoyTrail>()
                || !repo.IsComponentTypeRegistered<ConvoyProgress>()) return;
            _members ??= repo.Query().With<ConvoyMember>().With<SimTransform>().Build();

            foreach (var lead in _members)
            {
                ref readonly var m = ref repo.GetComponentRO<ConvoyMember>(lead);
                if (m.Rank != 0) continue;
                if (!repo.HasComponent<ConvoyTrail>(lead)) repo.AddComponent(lead, new ConvoyTrail());
                ref var trail = ref repo.GetComponentRW<ConvoyTrail>(lead);
                var p = repo.GetComponentRO<SimTransform>(lead).Position;
                var p2 = new Vector2(p.X, p.Y);
                trail.Record(p2);
                Gather(repo, lead, p2, m.Gap, ref trail);
            }
        }

        private void Gather(EntityRepository repo, Entity lead, Vector2 leadPos, float gap, ref ConvoyTrail trail)
        {
            unsafe
            {
                trail.RankValid = 0; trail.LastRank = 0;
                // the lead's own place: the newest point plus how far it has gone past it
                int newest = trail.Count - 1;
                trail.RankS[0] = trail.NewestS + Vector2.Distance(leadPos, trail.Point(newest));
                trail.RankV[0] = repo.HasComponent<VehicleState>(lead) ? repo.GetComponentRO<VehicleState>(lead).Speed : 0f;
                trail.RankValid = 1u;
                foreach (var e in _members!)
                {
                    if (e == lead) continue;
                    ref readonly var m = ref repo.GetComponentRO<ConvoyMember>(e);
                    if (m.Lead != lead || m.Rank == 0 || m.Rank >= ConvoyTrail.MaxRanks || !repo.HasComponent<ConvoyProgress>(e)) continue;
                    trail.RankS[m.Rank] = repo.GetComponentRO<ConvoyProgress>(e).ArcS;
                    trail.RankV[m.Rank] = repo.HasComponent<VehicleState>(e) ? repo.GetComponentRO<VehicleState>(e).Speed : 0f;
                    trail.RankValid |= 1u << m.Rank;
                    if (m.Rank > trail.LastRank) trail.LastRank = m.Rank;
                }
                // ⭐ C-6 — the tail's lag behind its place; the lead slows past one gap of slack and stops at three
                trail.TailLag = 0f; trail.SpeedCapFactor = 1f;
                if (trail.LastRank > 0 && trail.HasRank(trail.LastRank) && gap > 0f)
                {
                    float lag = (float)(trail.RankS[0] - trail.RankS[trail.LastRank]) - trail.LastRank * gap;
                    trail.TailLag = MathF.Max(0f, lag);
                    float slack = SlackGaps * gap;
                    trail.SpeedCapFactor = Math.Clamp(1f - (lag - slack) / (2f * slack), 0f, 1f);
                }
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> (C-4) — per road-convoy FOLLOWER, on the kinematic owner: finds its own place on the lead's trail (searched near its last place, so a
    /// trail that crosses itself never makes it jump), its predecessor's place and speed (from the lead's trail — the lead's system gathered them), and drives
    /// the trail at <see cref="ConvoyMember.Gap"/> ALONG THE PATH behind it: speed = the predecessor's + a P-term on the gap error, 0 at or under
    /// <see cref="ConvoyMember.MinGap"/>, steered to a point a lookahead ahead of itself ON the trail. Writes only its own <see cref="FormationTarget"/>,
    /// <see cref="ConvoyProgress"/> and <see cref="NavState"/> (<see cref="KinematicsMode.Column"/>); a vehicle that left the convoy goes back to
    /// <see cref="KinematicsMode.None"/>.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ConvoyFollowSystem : IEcsModuleSystem
    {
        /// <summary>The gap controller's gain (1/s): a gap error of 10 m adds 5 m/s.</summary>
        public const float GapGain = 0.5f;
        /// <summary>A follower is on the trail once it is this close to it (m).</summary>
        public const float JoinDistance = 4f;
        /// <summary>The search window around the last place: behind · ahead (m).</summary>
        public const float WindowBack = 15f, WindowAhead = 60f;

        private EntityQuery? _members;
        private EntityQuery? _progress;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<ConvoyMember>() || !repo.IsComponentTypeRegistered<ConvoyProgress>()
                || !repo.IsComponentTypeRegistered<ConvoyTrail>() || !repo.IsComponentTypeRegistered<FormationTarget>()) return;

            // a vehicle whose membership ended (its behaviour removed ConvoyMember): back to no mode, its place forgotten
            _progress ??= repo.Query().With<ConvoyProgress>().Build();
            foreach (var e in _progress)
            {
                if (repo.HasComponent<ConvoyMember>(e)) continue;
                if (repo.HasComponent<NavState>(e) && repo.GetComponentRO<NavState>(e).Mode == KinematicsMode.Column)
                {
                    ref var nav = ref repo.GetComponentRW<NavState>(e);
                    nav.Mode = KinematicsMode.None; nav.TargetSpeed = 0f;
                }
                repo.RemoveComponent<ConvoyProgress>(e);
            }

            _members ??= repo.Query().With<ConvoyMember>().With<SimTransform>().With<NavState>().Build();
            foreach (var e in _members)
            {
                ref readonly var m = ref repo.GetComponentRO<ConvoyMember>(e);
                if (m.Rank == 0 || !repo.IsAlive(m.Lead) || !repo.HasComponent<ConvoyTrail>(m.Lead)) continue;
                ref readonly var trail = ref repo.GetComponentRO<ConvoyTrail>(m.Lead);
                if (trail.Count == 0) continue;
                Follow(repo, e, in m, in trail);
            }
        }

        private static void Follow(EntityRepository repo, Entity e, in ConvoyMember m, in ConvoyTrail trail)
        {
            var p3 = repo.GetComponentRO<SimTransform>(e).Position;
            var p = new Vector2(p3.X, p3.Y);
            if (!repo.HasComponent<ConvoyProgress>(e)) repo.AddComponent(e, new ConvoyProgress { ArcS = trail.OldestS - Vector2.Distance(p, trail.Point(0)) });
            ref var prog = ref repo.GetComponentRW<ConvoyProgress>(e);

            // where am I on the trail?
            if (prog.OnTrail == 0)
            {
                float toStart = Vector2.Distance(p, trail.Point(0));
                prog.ArcS = trail.OldestS - toStart;
                if (toStart <= JoinDistance && trail.Count >= 2) prog.OnTrail = 1;
            }
            else if (trail.Project(p, prog.ArcS - WindowBack, prog.ArcS + WindowAhead, out double arc, out _))
                prog.ArcS = arc;
            if (prog.ArcS < trail.OldestS - 1e3) prog.ArcS = trail.OldestS - Vector2.Distance(p, trail.Point(0));   // the trail rolled past a far-behind vehicle

            // my predecessor: the rank ahead (the lead's system gathered it; a missing rank ⇒ the next one ahead)
            int ahead = m.Rank - 1;
            while (ahead > 0 && !trail.HasRank(ahead)) ahead--;
            double predS = trail.RankArc(ahead);
            float predV = trail.RankSpeed(ahead);
            int ranksAhead = m.Rank - ahead;
            float gap = MathF.Max(m.Gap, m.MinGap) * ranksAhead;
            float minGap = m.MinGap * ranksAhead;

            float along = (float)(predS - prog.ArcS);
            float top = repo.HasComponent<VehicleParams>(e) ? repo.GetComponentRO<VehicleParams>(e).MaxSpeedFwd : 15f;
            float speed = along <= minGap ? 0f : Math.Clamp(predV + GapGain * (along - gap), 0f, MathF.Max(top, 0.1f));

            // the pursuit point: a lookahead ahead of me ON the trail, never past the predecessor's place less the minimum gap
            float cur = repo.HasComponent<VehicleState>(e) ? repo.GetComponentRO<VehicleState>(e).Speed : 0f;
            float look = Math.Clamp(2f + 0.4f * MathF.Max(cur, speed), 3f, 8f);   // short: a pursuit point far ahead cuts a corner (measured 3.3 m off at 11 m, 90° at 10 m/s)
            double aim = prog.OnTrail == 0 ? trail.OldestS : Math.Min(prog.ArcS + look, Math.Max(prog.ArcS, predS - minGap * 0.5));
            var (target, dir) = trail.Sample(aim);
            if (prog.OnTrail == 0) target = trail.Point(0);

            var ft = new FormationTarget { TargetPosition = target, TargetHeading = dir, TargetSpeed = speed, IsValid = 1 };
            if (repo.HasComponent<FormationTarget>(e)) repo.GetComponentRW<FormationTarget>(e) = ft; else repo.AddComponent(e, ft);
            ref var nav = ref repo.GetComponentRW<NavState>(e);
            if (nav.Mode != KinematicsMode.Column) { nav.Mode = KinematicsMode.Column; nav.TargetSpeed = 0f; nav.HasArrived = 0; nav.IsBlocked = 0; }
        }
    }
}
