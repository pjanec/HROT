using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Squad;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2270</c> TS-B (R-2034 TS-4) — how a combatant's <see cref="Stress"/> rises and falls, in ONE place. Every stimulus is
    /// scaled by his nerve as obedience tempers it, <c>× (1 − 0.5 × effective Nerve)</c>: a round passing him, a hit, a friend falling near
    /// him, being outnumbered, being alone; it calms <see cref="CalmPerSecond"/>, twice as fast in cover and twice again with his leader
    /// near. 📄 docs/DESIGN_Temperament_And_Stress.md §4 TS-4, TS-7.
    /// </summary>
    public static class StressModel
    {
        public const float Hit = 0.25f, FriendDown = 0.2f;
        /// <summary>⭐⭐ <c>CE-2470</c> (Temperament §7.8, user 2026-11-21: <i>"if every single bullet counts then machine gun burst could break the guy too early"</i> ·
        /// <i>"soldier are usually trained so they shoudl not break so easily"</i>) — incoming fire raises him at this rate (before his nerve) while it goes on — a
        /// round passed him within <see cref="FireHoldsSeconds"/> — half in cover; the calming runs as before. ⇒ fire alone PINS (the aim spoil, UnderFire) but breaks
        /// only the unsteady or a man alone in the open for long: a regular in the open, alone, ~80 s; in cover or with his leader near, never by fire alone;
        /// militia in the open ~26 s. What breaks a trained man is fire PLUS a hit, a friend down, being alone or outnumbered. ⛔ SUPERSEDED: a near miss
        /// 0.08 per round (TS-4) — ~14 rounds broke a regular, one burst.</summary>
        public const float FirePerSecond = 0.06f;
        /// <summary>The fire "goes on" while a round passed him this recently (s) — longer than a slow automatic rifle's gap (1.3 s, ts-fire-discipline).</summary>
        public const double FireHoldsSeconds = 2.0;
        /// <summary>A friend down this close stresses him.</summary>
        public const float FriendDownMetres = 15f;
        /// <summary>Outnumbered: the enemy's share of the strength in play (<c>EnemyStrengthRatio</c>, 0–1) above this — 0.6 is the
        /// design's "enemy over 1.5 × his own" (1.5 ÷ 2.5), the input being a share, not a ratio.</summary>
        public const float OutnumberedShare = 0.6f;
        public const float OutnumberedPerSecond = 0.02f, AlonePerSecond = 0.01f;
        /// <summary>Alone: no living friend this close.</summary>
        public const float AloneMetres = 20f;
        public const float CalmPerSecond = 0.03f;
        /// <summary>⭐ <c>CE-7008</c> (City Battle §3a — "suppression wears off") — nothing has hit, passed or fallen near him for this long (s):
        /// the calm doubles. Without it a man outnumbered and alone, nerve 0, never calmed (+0.03 / s against −0.03 / s) — pinned for ever
        /// though nobody was firing at him any more.</summary>
        public const double QuietSeconds = 30.0;
        /// <summary>⭐ <c>CE-2376</c> (AQ104 F, DF-1) — an enemy heard this close (m), this recently (s), not in sight: his stress rises this much
        /// a second × (1 − effective Nerve) — "walking around behind a wall".</summary>
        public const float HeardCloseMetres = 10f, HeardClosePerSecond = 0.03f;
        public const double HeardFreshSeconds = 3.0;
        /// <summary>⭐ <c>CE-2376</c> (DF-2, measured re-tune) — fire on a man IN COVER counts this share (CE-2470: of the rate).</summary>
        public const float NearMissInCover = 0.5f;
        /// <summary>In cover: a cover point this close that faces his nearest remembered threat.</summary>
        public const float CoverMetres = 1.5f;
        /// <summary>His leader this close calms him twice as fast.</summary>
        public const float LeaderMetres = 20f;
        /// <summary>How much of a stimulus his nerve turns away: at nerve 1, half.</summary>
        public const float NerveShield = 0.5f;
        /// <summary>The longest gap one pass integrates (a pause, a skipped frame) — the clock never jumps stress.</summary>
        public const double MaxStepSeconds = 1.0;

        /// <summary>⭐ TS-7 — a combatant PERSON with a brain, in the fight: a soldier, an insurgent, a policeman. ⛔ Not a civilian (his own
        /// model), not a vehicle (no stance), not a man who is down.</summary>
        public static bool Feels(EntityRepository world, Entity e)
        {
            if (!world.IsAlive(e) || !world.HasComponent<BehaviorState>(e) || !world.HasComponent<Health>(e)) return false;
            if (!Fdp.Toolkit.Behavior.People.IsPersonBody(world, e)) return false;   // ⭐ C2 CC-6 — a person body (a stance, not an animal)
            if (world.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.CivilianState>() && world.HasComponent<Fdp.Toolkit.Civilians.CivilianState>(e)) return false;
            return Armament.IsCombatant(world, e) && Injury.IsInTheFight(world, e);
        }

        /// <summary>His level now; 0 with no <see cref="Stress"/>.</summary>
        public static float LevelOf(ISimulationView view, Entity e) => Stresses.LevelOf(view, e);   // ⭐ CE-1309 — one copy, beside Stress

        /// <summary>The share of a stimulus that reaches him.</summary>
        public static float Reach(EntityRepository world, Entity e)
            => 1f - NerveShield * TemperamentOf.Effective(world, e, TemperamentTrait.Nerve);

        /// <summary>A stimulus of <paramref name="amount"/> (before his nerve) at <paramref name="now"/>.</summary>
        public static void Add(EntityRepository world, Entity e, float amount, double now)
        {
            if (!Ensure(world, e, now)) return;
            ref var s = ref world.GetComponentRW<Stress>(e);
            s.Level = Math.Clamp(s.Level + amount * Reach(world, e), 0f, 1f);
            s.LastStimulusAt = (SimInstant)now;
        }

        /// <summary>One pass of the continuous terms over the time since the last: outnumbered and alone raise him, time calms him.</summary>
        public static void Step(EntityRepository world, Entity e, double now)
        {
            if (!Ensure(world, e, now)) return;
            StepCore(world, e, now);
        }

        /// <summary>⭐ P-1 — <see cref="Step"/> for a man who already HAS Stress (the parallel body: no lazy add — the pre-pass added it).</summary>
        public static void StepExisting(EntityRepository world, Entity e, double now)
        {
            if (!world.HasComponent<Stress>(e) || !world.HasComponent<SimTransform>(e) || !Feels(world, e)) return;
            StepCore(world, e, now);
        }

        private static void StepCore(EntityRepository world, Entity e, double now)
        {
            ref var s = ref world.GetComponentRW<Stress>(e);
            double dt = Math.Clamp(now - s.LastStepAt, 0.0, MaxStepSeconds);
            s.LastStepAt = (SimInstant)now;
            if (dt <= 0.0) return;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            float up = 0f;
            if (EnemyShare(world, e) > OutnumberedShare) up += OutnumberedPerSecond;
            if (!UnderFire.FriendNear(world, e, at, AloneMetres)) up += AlonePerSecond;
            bool inCover = InCover(world, e, at);
            if (UnderFireNow(in s, now)) up += inCover ? FirePerSecond * NearMissInCover : FirePerSecond;   // ⭐ CE-2470 — fire is a rate, not a count
            float calm = CalmPerSecond;
            if (inCover) calm *= 2f;
            if (LeaderNear(world, e, at)) calm *= 2f;
            if (now - s.LastStimulusAt > QuietSeconds) calm *= 2f;   // ⭐ CE-7008 — the fire has stopped: it wears off
            // ⭐ CE-2376 (AQ104 F, DF-1) — an enemy heard close and unseen: no calming while he is there, and the approved rise on top
            //   (0.03 / s × (1 − nerve) alone never beats the 0.03 / s calming — measured on the rail, so the calm stops)
            float heard = 0f;
            if (HeardCloseUnseen(world, e, at)) { heard = HeardClosePerSecond * (1f - TemperamentOf.Effective(world, e, TemperamentTrait.Nerve)); calm = 0f; }
            s.Level = Math.Clamp(s.Level + (float)dt * (up * Reach(world, e) + heard - calm), 0f, 1f);
        }

        /// <summary>⭐ The friend-down stimulus: <paramref name="down"/> fell — every man of his squad in the fight within
        /// <see cref="FriendDownMetres"/> of him.</summary>
        public static void FriendFell(EntityRepository world, Entity commander, Entity down, double now, bool gateOnAuthority)
        {
            if (!world.HasComponent<UnitRoster>(commander) || !world.HasComponent<SimTransform>(down)) return;
            var where = world.GetComponentRO<SimTransform>(down).Position;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(commander);
            for (int i = 0; i < roster.Count && i < UnitRoster.Capacity; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || m.Equals(down) || !world.IsAlive(m) || !world.HasComponent<SimTransform>(m)) continue;
                if (gateOnAuthority && !(world.HasComponent<BehaviorState>(m) && world.HasAuthority<BehaviorState>(m))) continue;
                if (Flat(world.GetComponentRO<SimTransform>(m).Position - where).Length() > FriendDownMetres) continue;
                Add(world, m, FriendDown, now);
            }
        }

        /// <summary>The enemy's share of the strength in play, as the <c>EnemyStrengthRatio</c> input reads it (CE-3054 C).</summary>
        public static float EnemyShare(EntityRepository world, Entity e)
        {
            if (!world.HasComponent<TargetMemory>(e)) return 0f;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(e);
            if (mem.Count == 0) return 0f;
            float enemy = 0f;
            for (int i = 0; i < mem.Count; i++) enemy += ThreatDanger.OfSlot(world, e, in mem, i);
            float total = enemy + ThreatDanger.OwnStrength(world, e);
            return total <= 0f ? 0f : Math.Clamp(enemy / total, 0f, 1f);
        }

        /// <summary>His squad leader (the member who leads, <see cref="SquadLeadership.Leader"/>), in the fight, within
        /// <see cref="LeaderMetres"/> — not himself.</summary>
        public static bool LeaderNear(EntityRepository world, Entity e, Vector3 at)
        {
            if (!world.IsComponentTypeRegistered<UnitSubordinate>() || !world.HasComponent<UnitSubordinate>(e)) return false;
            var commander = world.GetComponentRO<UnitSubordinate>(e).Commander;
            if (commander.IsNull || !world.IsAlive(commander)) return false;
            if (!world.IsComponentTypeRegistered<SquadLeadership>() || !world.HasComponent<SquadLeadership>(commander)) return false;
            var leader = world.GetComponentRO<SquadLeadership>(commander).Leader;
            if (leader.IsNull || leader.Equals(e) || !world.IsAlive(leader) || !Injury.IsInTheFight(world, leader)) return false;
            if (!world.HasComponent<SimTransform>(leader)) return false;
            return Flat(world.GetComponentRO<SimTransform>(leader).Position - at).Length() <= LeaderMetres;
        }

        // ── ⭐ CE-2272 (TS-6) — the break ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The lowest level a break needs, whatever his nerve: a man with none still breaks only once something has happened.</summary>
        public const float BreakFloor = Stresses.BreakFloor;
        /// <summary>He recovers when his level stays under this share of his nerve …</summary>
        public const float RecoverFraction = 0.6f;
        /// <summary>… for this long (s).</summary>
        public const double RecoverSeconds = 10.0;
        /// <summary>A broken man surrenders when his effective resolve is under this and an enemy covers him.</summary>
        public const float SurrenderResolve = 0.3f;
        /// <summary>How far he looks for somewhere to run to (m) · how much farther from the threat it must be than he is (m).</summary>
        public const float RetreatMetres = 30f, RetreatGainMetres = 3f;

        /// <summary>⭐ <c>CE-2365</c> / AQ103 H — he is NERVOUS from this share of his break level up (the band the fidget and the head share).</summary>
        public const float NervousFrom = Stresses.NervousFrom;

        /// <summary>⭐ <c>CE-2365</c> / AQ103 H — how nervous he is: 0 below <see cref="NervousFrom"/> × his break level, rising to 1 at it.
        /// 0 with no <see cref="Stress"/> (a civilian, a vehicle, a calm man).</summary>
        public static float Nervousness(EntityRepository world, Entity e) => Stresses.Nervousness(world, e);

        /// <summary>In cover: a cover point within <see cref="CoverMetres"/> of <paramref name="at"/> that faces his nearest remembered threat
        /// (any, when he knows none).</summary>
        public static bool InCover(EntityRepository world, Entity e, Vector3 at)
            => UnderFire.CoverNear(world, e, at, CoverMetres, 0f, out _, out _);

        /// <summary>Is he broken now?</summary>
        public static bool IsBroken(ISimulationView view, Entity e) => Stresses.IsBroken(view, e);

        /// <summary>The level at which he breaks: his effective nerve (TS-2), never under <see cref="BreakFloor"/>.</summary>
        public static float BreakLevel(EntityRepository world, Entity e) => Stresses.BreakLevel(world, e);

        /// <summary>
        /// ⭐ TS-6 — the edge: at <see cref="BreakLevel"/> he breaks (<see cref="SensorChange.StressBreak"/>); broken, under
        /// <see cref="RecoverFraction"/> × it for <see cref="RecoverSeconds"/> he recovers (<see cref="SensorChange.StressRecovered"/>).
        /// While broken his break runs — started again whenever it is not (a grenade's reflex first).
        /// </summary>
        public static void Judge(EntityRepository world, Entity e, double now)
        {
            bool judged = JudgeCompute(world, e, now, out var edge, out bool broken);
            if (judged) JudgeApply(world, e, edge, broken);
            // ⭐ CE-3462 (D-6) — independent of the break: a CALM man is judged nothing (JudgeCompute is false under his break line) and can still be cornered
            if (!broken && CorneredCandidate(world, e)) GiveUpIfCornered(world, e);
        }

        /// <summary>⭐ P-1 (the parallel experiment) — the part of <see cref="Judge"/> that writes only <paramref name="e"/>'s own Stress: the
        /// break / recovery decision. False when there is nothing to judge. Safe in a parallel batch.</summary>
        public static bool JudgeCompute(EntityRepository world, Entity e, double now, out SensorChange? edge, out bool broken)
        {
            edge = null; broken = false;
            if (!world.HasComponent<Stress>(e) || !Feels(world, e)) return false;   // a man down (or no longer a combatant) breaks no more
            // ⭐⭐ CE-4652 — an InertSop man ("no reactions", CE-2322) is never JUDGED broken: his level still rises (it shows), but being broken also
            //   ends his aimed fire elsewhere — 📐 live: with only the Break reaction blocked, uf-squad's two hostile shooters (stress 0.99, Broken) still
            //   fired 1 and 0 rounds in 30 s
            if (IsInertSop(world, e)) return false;
            float breakAt = BreakLevel(world, e);
            var s = world.GetComponentRO<Stress>(e);
            if (s.Broken == 0)
            {
                if (s.Level < breakAt) return false;
                // ⭐ D-11 A (CE-4666, user-approved 2026-11-19) — a man MOVING TO HIS ORDERED POST takes the near misses as "go to cover": his
                //   post is his cover, so the break waits until he is there or his move has stalled (his level stays — he breaks on arrival
                //   if it is still over the line)
                if (HeadingToPost(world, e)) return false;
                s.Broken = 1;
                s.CalmSince = (SimInstant)0;
                edge = SensorChange.StressBreak;
            }
            else if (s.Level < RecoverFraction * breakAt)
            {
                if (s.CalmSince <= 0) s.CalmSince = (SimInstant)now;
                else if (now - s.CalmSince >= RecoverSeconds) { s.Broken = 0; s.CalmSince = (SimInstant)0; edge = SensorChange.StressRecovered; }
            }
            else s.CalmSince = (SimInstant)0;
            world.SetComponent(e, s);
            broken = s.Broken != 0;
            return true;
        }

        /// <summary>⭐ P-1 — the part of <see cref="Judge"/> that touches the bus and another system's state (the reaction): SERIAL.</summary>
        public static void JudgeApply(EntityRepository world, Entity e, SensorChange? edge, bool broken)
        {
            if (edge is { } what && world.Bus.IsRegistered<SensorChangedEvent>())
                world.Bus.Publish(new SensorChangedEvent { Unit = e, What = what });
            if (broken) StartBreak(world, e);
        }

        public static void StartBreak(EntityRepository world, Entity e)
        {
            if (!world.HasComponent<BehaviorState>(e)) return;
            // ⭐⭐ CE-4652 / CE-3461 — InertSop is "the explicit NONE: no idle choice and no reactions" (CE-2322, AQ97 C) ⇒ no break either: his stress
            //   still rises (it shows), but nothing takes his task off him. 📐 Measured live: the scenarios' hostile shooters (InertSop, on an authored
            //   CombatPosture / FireAtPoint) were fired back at, broke within 2–5 s and fell silent after 0–2 rounds — so the militia, the squad's movers,
            //   never met the sustained fire those scenarios are about (ts-fire-discipline, uf-squad-under-fire).
            if (IsInertSop(world, e)) return;
            var task = world.GetComponentRO<BehaviorState>(e);
            if (task.Origin == BehaviorOrigin.Reaction && task.ActiveBehaviorHash != BehaviorIds.None
                && (task.Urgency == ReactionUrgency.Grenade || task.Urgency == ReactionUrgency.Break)) return;   // the grenade first; his break runs
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.NoteReactionCause(world, e, (byte)SensorChange.StressBreak);
            if (Surrenders(world, e))
            {
                SopActions.React(world, e, Hrot.Map.Definitions.Behavior.BehaviorNames.Surrender, ReactionUrgency.Break,
                                 new Hrot.Map.Definitions.Behavior.SurrenderParamsJsonDto { Seconds = 0f });
                return;
            }
            var dto = new Hrot.Map.Definitions.Behavior.BreakParamsJsonDto();
            if ((Flees(world, e) && Flee(world, e, out var to)) || PostRetreat(world, e, out to) || Retreat(world, e, out to)) { dto.HasRetreat = true; dto.Retreat = to; }   // ⭐ CE-2376 (DS-3) · D-11 A
            SopActions.React(world, e, Hrot.Map.Definitions.Behavior.BehaviorNames.Break, ReactionUrgency.Break, in dto);
        }

        private static readonly int s_inertSop = BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.InertSop);

        /// <summary>⭐ CE-4652 — his SOP is <c>InertSop</c> (no reactions).</summary>
        public static bool IsInertSop(EntityRepository world, Entity e)
            => world.IsComponentTypeRegistered<SopState>() && world.HasComponent<SopState>(e) && world.GetComponentRO<SopState>(e).SopHash == s_inertSop;

        /// <summary>⭐ <c>CE-3462</c> (D-6, user-approved; TS-6 row) — a CORNERED man is a rifle this close (m).</summary>
        public const float CorneredMetres = 5f;

        /// <summary>⭐ <c>CE-3462</c> (D-6) — the part of the cornered rule that reads only HIM (parallel-safe): resolve under
        /// <see cref="SurrenderResolve"/>, not InertSop, and no break / grenade / surrender reaction already running.</summary>
        public static bool CorneredCandidate(EntityRepository world, Entity e)
        {
            if (!world.HasComponent<BehaviorState>(e) || !world.HasComponent<Stress>(e) || !Feels(world, e) || IsInertSop(world, e)) return false;
            if (TemperamentOf.Effective(world, e, TemperamentTrait.Resolve) >= SurrenderResolve) return false;
            var task = world.GetComponentRO<BehaviorState>(e);
            return !(task.Origin == BehaviorOrigin.Reaction && task.ActiveBehaviorHash != BehaviorIds.None
                     && (task.Urgency == ReactionUrgency.Grenade || task.Urgency == ReactionUrgency.Break));
        }

        /// <summary>⭐⭐ <c>CE-3462</c> (D-6, user-approved; TS-6 row) — a low-resolve man with a forces rifle on him at close range
        /// (<see cref="Covering.By"/> within <see cref="CorneredMetres"/>) GIVES UP at once, without waiting for his stress to break him: a room
        /// entry is too fast for the near-miss stress to reach his break line (📐 ts-house-defenders: the Waverer peaked at 0.11 and was shot).
        /// SERIAL (it reads the other men's weapon postures).</summary>
        public static void GiveUpIfCornered(EntityRepository world, Entity e)
        {
            if (Covering.By(world, e, CorneredMetres).IsNull) return;
            Fdp.Toolkit.Behavior.Diagnostics.BrainJournalOf.NoteReactionCause(world, e, (byte)SensorChange.StressBreak);
            SopActions.React(world, e, Hrot.Map.Definitions.Behavior.BehaviorNames.Surrender, ReactionUrgency.Break,
                             new Hrot.Map.Definitions.Behavior.SurrenderParamsJsonDto { Seconds = 0f });
        }

        /// <summary>TS-6 — he gives up: effective resolve under <see cref="SurrenderResolve"/> and a forces rifle on him (<see cref="Covering.By"/>).</summary>
        public static bool Surrenders(EntityRepository world, Entity e)
            => TemperamentOf.Effective(world, e, TemperamentTrait.Resolve) < SurrenderResolve && !Covering.By(world, e).IsNull;

        /// <summary>⭐ <c>CE-2376</c> (AQ104 C, DS-3) — panic: a man this low on effective Resolve who breaks (and does not give up) FLEES — out
        /// of the house or the area, away from the enemy — not to the nearest cover.</summary>
        public const float FleeResolve = 0.4f;
        public const float FleeMetres = 60f, FleeGainMetres = 15f;

        public static bool Flees(EntityRepository world, Entity e)
            => TemperamentOf.Effective(world, e, TemperamentTrait.Resolve) < FleeResolve;

        /// <summary>⭐ DS-3 — where he flees: a cover point within <see cref="FleeMetres"/> at least <see cref="FleeGainMetres"/> farther from his
        /// nearest remembered threat than he is.</summary>
        public static bool Flee(EntityRepository world, Entity e, out Vector3 to)
        {
            to = default;
            if (!world.HasComponent<SimTransform>(e)) return false;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            if (!UnderFire.NearestThreat(world, e, at, out var threat)) return false;
            if (!UnderFire.CoverNear(world, e, at, FleeMetres, FleeGainMetres, out var cover, out _)) return false;
            if (Flat(cover - threat).Length() < Flat(at - threat).Length() + FleeGainMetres) return false;
            to = cover;
            return true;
        }

        /// <summary>⭐ D-11 A — a moving man under this (m/s) has STALLED: his move no longer carries him to his post.</summary>
        public const float StalledMps = 0.3f;

        private static readonly int s_combatPosture = BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.CombatPosture);

        /// <summary>⭐ D-11 A — his ordered post: he runs a superior's <c>CombatPosture</c> (the squad's post order, <c>SquadManeuverRunner.Hold</c>)
        /// and its move is active — the move's destination is the post. Own components only (safe in a parallel batch).</summary>
        public static unsafe bool OrderedPost(EntityRepository world, Entity e, out Vector3 post, out bool running)
        {
            post = default; running = false;
            if (!world.HasComponent<BehaviorState>(e) || !world.HasComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(e)) return false;
            ref readonly var task = ref world.GetComponentRO<BehaviorState>(e);
            if (task.Origin != BehaviorOrigin.Superior || task.ActiveBehaviorHash != s_combatPosture) return false;
            ref readonly var channel = ref world.GetComponentRO<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(e);
            if (channel.ActiveAction != Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo) return false;
            fixed (byte* p = channel.Params) post = ((Fdp.Toolkit.Navigation.MoveToParams*)p)->Destination;
            running = channel.Status == Fbt.NodeStatus.Running;
            return true;
        }

        /// <summary>⭐ D-11 A — on his way to his ordered post and still getting there (not arrived, not stalled).</summary>
        public static bool HeadingToPost(EntityRepository world, Entity e)
        {
            if (!OrderedPost(world, e, out _, out bool running) || !running) return false;
            return Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, e, out _, out var v) && v.X * v.X + v.Y * v.Y >= StalledMps * StalledMps;
        }

        /// <summary>⭐ D-11 A — a broken man's retreat point is FIRST his own ordered post, when it is behind cover and he is not on it already.</summary>
        public static bool PostRetreat(EntityRepository world, Entity e, out Vector3 to)
        {
            to = default;
            if (!OrderedPost(world, e, out var post, out _) || !world.HasComponent<SimTransform>(e)) return false;
            if (Flat(post - world.GetComponentRO<SimTransform>(e).Position).Length() < RetreatGainMetres || !InCover(world, e, post)) return false;
            to = post;
            return true;
        }

        /// <summary>Somewhere to run: a cover point within <see cref="RetreatMetres"/> that faces his nearest remembered threat and lies at
        /// least <see cref="RetreatGainMetres"/> farther from it than he does. None when he knows no threat.</summary>
        public static bool Retreat(EntityRepository world, Entity e, out Vector3 to)
        {
            to = default;
            if (!world.HasComponent<SimTransform>(e)) return false;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            if (!UnderFire.NearestThreat(world, e, at, out var threat)) return false;
            if (!UnderFire.CoverNear(world, e, at, RetreatMetres, RetreatGainMetres, out var cover, out _)) return false;
            if (Flat(cover - threat).Length() < Flat(at - threat).Length() + RetreatGainMetres) return false;
            to = cover;
            return true;
        }

        /// <summary>⭐ <c>CE-2470</c> — a round passed him at <paramref name="now"/>: he is under fire (the rate runs in <see cref="Step"/>), and it is a stimulus
        /// (the calm's quiet clock restarts). ⛔ No jump in his level — a burst is the same as one round.</summary>
        public static void FireAt(EntityRepository world, Entity e, double now)
        {
            if (!Ensure(world, e, now)) return;
            ref var s = ref world.GetComponentRW<Stress>(e);
            s.LastFireAt = (SimInstant)now;
            s.LastStimulusAt = (SimInstant)now;
        }

        /// <summary>⭐ <c>CE-2470</c> — a round passed him within <see cref="FireHoldsSeconds"/>.</summary>
        public static bool UnderFireNow(in Stress s, double now) => (double)s.LastFireAt > 0.0 && now - s.LastFireAt <= FireHoldsSeconds;

        /// <summary>⭐ <c>CE-2376</c> (AQ104 F, DF-1) — someone heard within <see cref="HeardCloseMetres"/> in the last
        /// <see cref="HeardFreshSeconds"/>, whom he does not see now (an unknown sound, or a known enemy out of sight), not a neutral.</summary>
        public static unsafe bool HeardCloseUnseen(EntityRepository world, Entity e, Vector3 at)
        {
            if (!world.HasComponent<TargetMemory>(e)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(e);
            uint now = Fdp.Toolkit.Perception.PerceptionClock.Now(world);
            double spf = world.HasSingleton<GlobalTime>() && world.GetSingleton<GlobalTime>().FrameNumber > 0
                ? world.GetSingleton<GlobalTime>().TotalTime / world.GetSingleton<GlobalTime>().FrameNumber : 1.0 / 60.0;
            for (int i = 0; i < mem.Count; i++)
            {
                if ((mem.Modalities[i] & (byte)SensorModality.Acoustic) == 0) continue;
                // ⭐⭐ CE-4666 — weapon fire is not "someone walking behind the wall": a shot carries hundreds of metres and is heard FROM the last
                //   opening on its way (AQ106 F), so its remembered place is his own window, not the shooter. 📐 live pc-defend-house: rifles
                //   189 m off, heard through the house's opening 7 m from the reserve ⇒ no calming, +0.022 / s, every defender broke by 121 s.
                //   Fire already has its own stimuli (near miss, friend down, outnumbered).
                if (IsFireClass(mem.SourceClass[i])) continue;
                uint seen = mem.LastSeenTick[i];
                if (seen > now || (now - seen) * spf > HeardFreshSeconds) continue;
                var where = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                if (Vector3.Distance(where, at) > HeardCloseMetres) continue;
                if (TargetMemory.IsAnonymous(in mem, i)) return true;
                var enemy = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(enemy) || ThreatDanger.IsNeutral(world, enemy) || ThreatDanger.IsOutOfTheFight(world, enemy)) continue;
                if (!SightNow.Sees(world, e, enemy)) return true;
            }
            return false;
        }

        /// <summary>⭐ CE-4666 — a heard class that is weapon fire (small arms, a heavy weapon, an explosion, a flame roar): never the heard-close term.</summary>
        public static bool IsFireClass(byte sourceClass)
            => sourceClass is (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.SmallArms or (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.HeavyWeapon
                           or (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Explosion
                           or (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Flame;   // ⭐ CE-4635 — a flamethrower's roar is weapon fire

        internal static bool Ensure(EntityRepository world, Entity e, double now)
        {
            if (!world.IsComponentTypeRegistered<Stress>() || !world.HasComponent<SimTransform>(e)) return false;
            if (!Feels(world, e)) return false;
            if (!world.HasComponent<Stress>(e)) world.AddComponent(e, new Stress { LastStepAt = (SimInstant)now });
            return true;
        }

        private static Vector3 Flat(Vector3 v) => new(v.X, v.Y, 0f);
    }

    /// <summary>
    /// ⭐ <c>CE-2270</c> TS-B — the brain node's stress pass: last frame's sensing edges (a near miss, a hit, a squad member down) and the
    /// continuous terms, for every combatant this node runs. After perception and the squad merge, before the brain tick, so the damping
    /// inputs (TS-5) and the break (TS-6) read this frame's level. 📄 docs/DESIGN_Temperament_And_Stress.md §3.
    /// </summary>
    // ⭐ CE-1309 (ui, cross-lane) — R-3036: every system declares its tick policy; without it the scheduler throws on the first frame of
    //   every host running CgfLogicPack (measured: the editor crashed at once). Stress integrates sim time, as its siblings do.
    [TickMode(TickPolicy.WhenTimeAdvances)]
    // ⭐ backend (cross-lane, 2026-10-26) — the phase it runs in: CgfLogicPack lists it with the simulation systems, like its siblings. Without
    //   it SystemScheduler.RegisterSystem throws ("must have [UpdateInPhase]") and the editor harness failed at boot.
    [UpdateInPhase(SystemPhase.Simulation)]
    public sealed class StressSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _people;

        public StressSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        /// <summary>⭐ P-5 (the parallel experiment, E-4) — each man's CONTINUOUS step (the drift, the calming) runs once per this many sim
        /// seconds on his phase (<see cref="CadenceGate.DueOnGrid"/>); 0 = every frame (the default). Stimuli still land at once. ✅ The step
        /// already integrates the time since his last step (<c>Stress.LastStepAt</c>), so a slower step loses no time. Experiment knob:
        /// <c>HROT_STRESS_PERIOD</c>.</summary>
        [field: Fdp.Core.NotState("an experiment knob (`HROT_STRESS_PERIOD`), fixed at start-up (exp/perf)")]
        public double StepPeriod { get; set; } = double.TryParse(EnvVars.Read(EnvVars.HrotStressPeriod),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0.0;
        [Fdp.Core.WorldDerived("the previous frame's sim time for the step cadence; empty (−∞) or a later time makes every man due once (`CadenceGate.DueOnGrid`: now ≤ prev ⇒ due), and the step integrates from his own `Stress.LastStepAt` — nothing lost (exp/perf)")]
        private double _prevNow = double.NegativeInfinity;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Stress>() || !repo.IsComponentTypeRegistered<StanceIntent>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;

            foreach (var evt in repo.ReadEvents<SensorChangedEvent>())
            {
                switch (evt.What)
                {
                    case SensorChange.NearMiss when Owned(repo, evt.Unit): StressModel.FireAt(repo, evt.Unit, now); break;   // ⭐ CE-2470 — a rate while the fire goes on
                    case SensorChange.Hit when Owned(repo, evt.Unit): StressModel.Add(repo, evt.Unit, StressModel.Hit, now); break;   // a man down bleeds Hit edges: Feels refuses him
                    case SensorChange.SquadMemberDown: StressModel.FriendFell(repo, evt.Unit, evt.Target, now, _gateOnAuthority); break;
                }
            }

            _people ??= repo.Query().With<StanceIntent>().With<BehaviorState>().With<Health>().With<SimTransform>().Build();
            double prev = _prevNow; _prevNow = now;
            double period = StepPeriod;
            if (Fdp.Core.Jobs.ParallelEntities.Enabled)
            {
                // ⭐ P-1 (DESIGN_Parallel_Simulation §5; the parallel experiment E-3) — ① SERIAL: the men we own, and Stress ADDED where it is
                //   missing (a structural change is not thread-safe); ② PARALLEL: each man steps and judges his OWN Stress; ③ SERIAL, in batch
                //   order: the edges published and the breaks started. Same order as the serial loop below.
                int n = 0;
                foreach (var e in _people)
                {
                    if (!Owned(repo, e)) continue;
                    if (!CadenceGate.DueOnGrid(prev, now, period, e.Index) && repo.HasComponent<Stress>(e)) continue;   // ⭐ P-5
                    StressModel.Ensure(repo, e, now);
                    if (n == _list.Length) Array.Resize(ref _list, Math.Max(64, _list.Length * 2));
                    _list[n++] = e;
                }
                Fdp.Core.Jobs.ParallelEntities.Run(repo, _list, n, new StressJob { Repo = repo, Now = now });
                return;
            }
            foreach (var e in _people)
            {
                if (!Owned(repo, e)) continue;
                if (!CadenceGate.DueOnGrid(prev, now, period, e.Index) && repo.HasComponent<Stress>(e)) continue;   // ⭐ P-5
                StressModel.Step(repo, e, now);
                StressModel.Judge(repo, e, now);   // ⭐ CE-2272 (TS-6) — the break and the recovery
            }
        }

        [Fdp.Core.FrameScratch("scratch — refilled from 0 each run; the parallel job reads only [0, n)")]
        private Entity[] _list = new Entity[64];

        private struct StressJob : Fdp.Core.Jobs.IEntityBatchJob, Fdp.Core.Jobs.IStableReads
        {
            // ⭐ N-11 (D-6) — read on other men, written by no one while stress steps (measured: the scale-rig survey)
            public void DeclareStableReads(Fdp.Core.Jobs.PhaseReadSet reads) => reads
                .Unmanaged<Fdp.Toolkit.Replication.Components.TkbIdentity>()
                .Unmanaged<StanceIntent>()
                .Unmanaged<Fdp.Toolkit.Creatures.Demeanour>()    // a man's surrender / hands-up gesture (written by his brain, not here)
                // ⭐ CE-2471 — "a friend near" (UnderFire.FriendNear, the frame grid's late check): his side, his health, where he stands — none
                //   written while stress steps (stress writes only each man's own Stress). 📐 StressSystemTests threw under HROT_READ_CHECK=1
                .Unmanaged<SimTransform>()
                .Unmanaged<EntityInfo>()
                .Unmanaged<Health>()
                .Unmanaged<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
            public EntityRepository Repo;
            public double Now;
            public void Execute(Entity e, int index, Fdp.Core.Jobs.BatchContext ctx)
            {
                StressModel.StepExisting(Repo, e, Now);
                bool judged = StressModel.JudgeCompute(Repo, e, Now, out var edge, out bool broken);
                // ⭐ CE-3462 (D-6) — Value 2: not broken, but a cornered candidate (his own state only, judged or not — a calm man too); the rifle
                //   check runs serially below
                bool cornered = !broken && StressModel.CorneredCandidate(Repo, e);
                if (!judged && !cornered) return;
                if (edge != null || broken || cornered) ctx.Defer(e, edge is { } w ? (int)w : -1, broken ? 1f : cornered ? 2f : 0f);
            }
            public void ApplyDeferred(EntityRepository repo, in Fdp.Core.Jobs.Deferred d)
            {
                StressModel.JudgeApply(repo, d.Entity, d.Code >= 0 ? (SensorChange)d.Code : null, d.Value == 1f);
                if (d.Value == 2f) StressModel.GiveUpIfCornered(repo, d.Entity);
            }
        }

        private bool Owned(EntityRepository repo, Entity e)
            => !_gateOnAuthority || (repo.IsAlive(e) && repo.HasComponent<BehaviorState>(e) && repo.HasAuthority<BehaviorState>(e));
    }
}
