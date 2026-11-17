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
    /// ⭐⭐ <c>CE-9001</c> / <c>CE-9004</c> (C-4, C-6; 📄 docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md §3, §4.4) — per LEADER of a <see cref="FormationType.Column"/>
    /// formation (its <see cref="FormationController"/>), on the kinematic owner: records the leader's ACTUAL path into its own <see cref="ConvoyTrail"/>, gathers
    /// every follower's place and speed on it (its <see cref="FormationFollower"/> slot = its rank; a declared read — the followers write only their own
    /// <see cref="ConvoyProgress"/>), and caps the leader's speed while the column is stretched (string stability). Writes only the leader's own components.
    /// 0 B per steady frame (cached queries, fixed-size trail).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ConvoyLeadSystem : IEcsModuleSystem
    {
        /// <summary>The cap starts once the tail is this many gaps behind its place, and reaches 0 (the lead stops) at three times it.</summary>
        public const float SlackGaps = 1f;

        private EntityQuery? _leads;
        private EntityQuery? _followers;

        /// <summary>A column's spacing and minimum gap (m) when its <see cref="FormationParams"/> name none.</summary>
        public const float DefaultGap = 15f, DefaultMinGap = 7f;

        public static float GapOf(in FormationController c) => c.Params.Spacing > 0f ? c.Params.Spacing : DefaultGap;
        /// <summary>The minimum gap: half the spacing (no field of its own — <c>FormationController</c>'s bytes are in 75 scenario goldens).</summary>
        public static float MinGapOf(in FormationController c) => c.Params.Spacing > 0f ? c.Params.Spacing * 0.5f : DefaultMinGap;

        /// <summary>⭐ C-6 — the lead's speed cap as a fraction of its speed, from the tail's lag behind its place: 1 up to one gap of slack, 0 (stop) at three.
        /// ONE formula for both measures of the lag — the trail's arc (the kinematic owner, mode 2) and the chords (<see cref="ChordLag"/>, the traffic node, mode 1).</summary>
        public static float CapFactor(float lag, float gap)
        {
            if (gap <= 0f) return 1f;
            float slack = SlackGaps * gap;
            return Math.Clamp(1f - (lag - slack) / (2f * slack), 0f, 1f);
        }

        /// <summary>
        /// ⭐ <c>CE-9008</c> (C-6, mode 1) — the tail's lag of <paramref name="lead"/>'s road column measured on a node WITHOUT the trail (the traffic node): the
        /// straight chords lead → slot 1 → … → the tail, minus the tail's slot × the gap. A chord is never longer than the path, so on a bend this reads a
        /// little SHORT (the cap starts a little later) — the slack absorbs it. False when nobody follows <paramref name="lead"/>. 0 B (a stack buffer).
        /// </summary>
        public static bool ChordLag(EntityRepository repo, EntityQuery followers, Entity lead, float gap, out float lag)
        {
            lag = 0f;
            if (!repo.HasComponent<SimTransform>(lead)) return false;
            Span<Vector2> at = stackalloc Vector2[ConvoyTrail.MaxRanks];
            uint valid = 1u;
            int tail = 0;
            var lp = repo.GetComponentRO<SimTransform>(lead).Position;
            at[0] = new Vector2(lp.X, lp.Y);
            foreach (var e in followers)
            {
                if (e == lead || !repo.HasComponent<SimTransform>(e)) continue;
                ref readonly var f = ref repo.GetComponentRO<FormationFollower>(e);
                int slot = f.SlotIndex;
                if (f.Leader != lead || f.IsInFormation == 0 || slot <= 0 || slot >= ConvoyTrail.MaxRanks) continue;
                var p = repo.GetComponentRO<SimTransform>(e).Position;
                at[slot] = new Vector2(p.X, p.Y);
                valid |= 1u << slot;
                if (slot > tail) tail = slot;
            }
            if (tail == 0) return false;
            float along = 0f;
            var prev = at[0];
            for (int k = 1; k <= tail; k++)
            {
                if ((valid & (1u << k)) == 0) continue;
                along += Vector2.Distance(prev, at[k]);
                prev = at[k];
            }
            lag = MathF.Max(0f, along - tail * gap);
            return true;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            // registered with the kinematic components (KinematicComponentRegistry) — never lazily mid-tick (D-26)
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<FormationController>() || !repo.IsComponentTypeRegistered<FormationFollower>()
                || !repo.IsComponentTypeRegistered<ConvoyTrail>() || !repo.IsComponentTypeRegistered<ConvoyProgress>()) return;
            _leads ??= repo.Query().With<FormationController>().With<SimTransform>().Build();
            _followers ??= repo.Query().With<FormationFollower>().With<SimTransform>().Build();

            foreach (var lead in _leads)
            {
                ref readonly var c = ref repo.GetComponentRO<FormationController>(lead);
                // ⭐ every vehicle carries a default controller (VehicleKinematicsTkbTranslator) — it LEADS a column only while a follower names it
                if (c.Type != FormationType.Column || !HasFollower(repo, lead))
                {
                    if (repo.HasComponent<ConvoyTrail>(lead)) repo.RemoveComponent<ConvoyTrail>(lead);   // the column dissolved
                    continue;
                }
                if (!repo.HasComponent<ConvoyTrail>(lead)) repo.AddComponent(lead, new ConvoyTrail());
                ref var trail = ref repo.GetComponentRW<ConvoyTrail>(lead);
                var p = repo.GetComponentRO<SimTransform>(lead).Position;
                var p2 = new Vector2(p.X, p.Y);
                trail.Record(p2);
                Gather(repo, lead, p2, GapOf(in c), ref trail);
            }
        }

        private bool HasFollower(EntityRepository repo, Entity lead)
        {
            foreach (var e in _followers!)
            {
                if (e == lead) continue;
                ref readonly var f = ref repo.GetComponentRO<FormationFollower>(e);
                if (f.IsInFormation != 0 && f.SlotIndex != 0 && f.Leader == lead) return true;   // a road column's slot names its leader explicitly
            }
            return false;
        }

        private void Gather(EntityRepository repo, Entity lead, Vector2 leadPos, float gap, ref ConvoyTrail trail)
        {
            unsafe
            {
                trail.RankValid = 0; trail.LastRank = 0;
                // the leader's own place: the newest point plus how far it has gone past it
                int newest = trail.Count - 1;
                trail.RankS[0] = trail.NewestS + Vector2.Distance(leadPos, trail.Point(newest));
                trail.RankV[0] = repo.HasComponent<VehicleState>(lead) ? repo.GetComponentRO<VehicleState>(lead).Speed : 0f;
                trail.RankX[0] = leadPos.X; trail.RankY[0] = leadPos.Y;
                trail.RankValid = 1u;
                foreach (var e in _followers!)
                {
                    if (e == lead) continue;
                    ref readonly var f = ref repo.GetComponentRO<FormationFollower>(e);
                    int rank = f.SlotIndex;
                    if (f.IsInFormation == 0 || rank == 0 || rank >= ConvoyTrail.MaxRanks || !repo.HasComponent<ConvoyProgress>(e)
                        || f.Leader != lead) continue;
                    trail.RankS[rank] = repo.GetComponentRO<ConvoyProgress>(e).ArcS;
                    trail.RankV[rank] = repo.HasComponent<VehicleState>(e) ? repo.GetComponentRO<VehicleState>(e).Speed : 0f;
                    var ep = repo.GetComponentRO<SimTransform>(e).Position;
                    trail.RankX[rank] = ep.X; trail.RankY[rank] = ep.Y;
                    trail.RankValid |= 1u << rank;
                    if (rank > trail.LastRank) trail.LastRank = (byte)rank;
                }
                // ⭐ C-6 — the tail's lag behind its place; the leader slows past one gap of slack and stops at three
                trail.TailLag = 0f; trail.SpeedCapFactor = 1f;
                if (trail.LastRank > 0 && trail.HasRank(trail.LastRank) && gap > 0f)
                {
                    float lag = (float)(trail.RankS[0] - trail.RankS[trail.LastRank]) - trail.LastRank * gap;
                    trail.TailLag = MathF.Max(0f, lag);
                    trail.SpeedCapFactor = CapFactor(lag, gap);
                }
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-9001</c> / <c>CE-9004</c> (C-4) — per FOLLOWER of a <see cref="FormationType.Column"/> formation, on the kinematic owner: finds its own place on the
    /// leader's trail (searched near its last place, so a trail that crosses itself never makes it jump), its predecessor's place and speed (from the leader's
    /// trail), and drives the trail at <see cref="FormationParams.Spacing"/> ALONG THE PATH behind it: speed = the predecessor's + a P-term on the gap error, 0
    /// at or under half the spacing (along the path, and in metres plus its stopping distance), steered to a point a lookahead ahead of
    /// itself ON the trail. Writes only its own <see cref="FormationTarget"/>, <see cref="ConvoyProgress"/> and <see cref="NavState"/>
    /// (<see cref="KinematicsMode.Column"/>); a vehicle that left the column goes back to <see cref="KinematicsMode.None"/>.
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

        private EntityQuery? _followers;
        private EntityQuery? _progress;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<FormationFollower>() || !repo.IsComponentTypeRegistered<FormationController>()
                || !repo.IsComponentTypeRegistered<ConvoyProgress>() || !repo.IsComponentTypeRegistered<ConvoyTrail>() || !repo.IsComponentTypeRegistered<FormationTarget>()) return;

            // a vehicle no longer in a column (its slot removed, or its leader no longer leads a column): back to no mode, its place forgotten
            _progress ??= repo.Query().With<ConvoyProgress>().Build();
            foreach (var e in _progress)
            {
                if (InColumn(repo, e, out _)) continue;
                if (repo.HasComponent<NavState>(e) && repo.GetComponentRO<NavState>(e).Mode == KinematicsMode.Column)
                {
                    ref var nav = ref repo.GetComponentRW<NavState>(e);
                    nav.Mode = KinematicsMode.None; nav.TargetSpeed = 0f;
                }
                repo.RemoveComponent<ConvoyProgress>(e);
            }

            _followers ??= repo.Query().With<FormationFollower>().With<SimTransform>().With<NavState>().Build();
            foreach (var e in _followers)
            {
                if (!InColumn(repo, e, out var lead) || !repo.HasComponent<ConvoyTrail>(lead)) continue;
                ref readonly var trail = ref repo.GetComponentRO<ConvoyTrail>(lead);
                if (trail.Count == 0) continue;
                ref readonly var c = ref repo.GetComponentRO<FormationController>(lead);
                Follow(repo, e, repo.GetComponentRO<FormationFollower>(e).SlotIndex, ConvoyLeadSystem.GapOf(in c), ConvoyLeadSystem.MinGapOf(in c), in trail);
            }
        }

        /// <summary>Is <paramref name="e"/> in a ROAD column: an active slot (≥ 1) with an explicit <see cref="FormationFollower.Leader"/>, alive and leading a <see cref="FormationType.Column"/>?</summary>
        public static bool InColumn(EntityRepository repo, Entity e, out Entity lead)
        {
            lead = Entity.Null;
            if (!repo.HasComponent<FormationFollower>(e)) return false;
            ref readonly var f = ref repo.GetComponentRO<FormationFollower>(e);
            if (f.IsInFormation == 0 || f.SlotIndex == 0 || f.Leader.IsNull) return false;   // a commander-led slot is FormationTargetSystem's
            lead = f.Leader;
            return repo.IsAlive(lead) && repo.HasComponent<FormationController>(lead) && repo.GetComponentRO<FormationController>(lead).Type == FormationType.Column;
        }

        private static void Follow(EntityRepository repo, Entity e, int rank, float spacing, float minSpacing, in ConvoyTrail trail)
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
            int ahead = rank - 1;
            while (ahead > 0 && !trail.HasRank(ahead)) ahead--;
            double predS = trail.RankArc(ahead);
            float predV = trail.RankSpeed(ahead);
            int ranksAhead = rank - ahead;
            float gap = MathF.Max(spacing, minSpacing) * ranksAhead;
            float minGap = minSpacing * ranksAhead;

            float along = (float)(predS - prog.ArcS);
            float top = repo.HasComponent<VehicleParams>(e) ? repo.GetComponentRO<VehicleParams>(e).MaxSpeedFwd : 15f;
            // ⭐ never closer than the minimum gap — along the path AND in metres (a predecessor that reversed or cut back is nearer than its arc says)
            float cur0 = repo.HasComponent<VehicleState>(e) ? MathF.Max(0f, repo.GetComponentRO<VehicleState>(e).Speed) : 0f;
            float decel = repo.HasComponent<VehicleParams>(e) && repo.GetComponentRO<VehicleParams>(e).MaxDecel > 0f ? repo.GetComponentRO<VehicleParams>(e).MaxDecel : 6f;
            bool tooClose = along <= minGap || Vector2.Distance(p, trail.RankPos(ahead)) <= minSpacing + cur0 * cur0 / (2f * decel);   // + its stopping distance
            float speed = tooClose ? 0f : Math.Clamp(predV + GapGain * (along - gap), 0f, MathF.Max(top, 0.1f));

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
