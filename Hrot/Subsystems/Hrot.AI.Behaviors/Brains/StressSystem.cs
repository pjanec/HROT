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
        public const float NearMiss = 0.08f, Hit = 0.25f, FriendDown = 0.2f;
        /// <summary>A friend down this close stresses him.</summary>
        public const float FriendDownMetres = 15f;
        /// <summary>Outnumbered: the enemy's share of the strength in play (<c>EnemyStrengthRatio</c>, 0–1) above this — 0.6 is the
        /// design's "enemy over 1.5 × his own" (1.5 ÷ 2.5), the input being a share, not a ratio.</summary>
        public const float OutnumberedShare = 0.6f;
        public const float OutnumberedPerSecond = 0.02f, AlonePerSecond = 0.01f;
        /// <summary>Alone: no living friend this close.</summary>
        public const float AloneMetres = 20f;
        public const float CalmPerSecond = 0.03f;
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
            if (!world.IsComponentTypeRegistered<StanceIntent>() || !world.HasComponent<StanceIntent>(e)) return false;
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
            s.LastStimulusAt = now;
        }

        /// <summary>One pass of the continuous terms over the time since the last: outnumbered and alone raise him, time calms him.</summary>
        public static void Step(EntityRepository world, Entity e, double now)
        {
            if (!Ensure(world, e, now)) return;
            ref var s = ref world.GetComponentRW<Stress>(e);
            double dt = Math.Clamp(now - s.LastStepAt, 0.0, MaxStepSeconds);
            s.LastStepAt = now;
            if (dt <= 0.0) return;
            var at = world.GetComponentRO<SimTransform>(e).Position;
            float up = 0f;
            if (EnemyShare(world, e) > OutnumberedShare) up += OutnumberedPerSecond;
            if (!UnderFire.FriendNear(world, e, at, AloneMetres)) up += AlonePerSecond;
            float calm = CalmPerSecond;
            if (InCover(world, e, at)) calm *= 2f;
            if (LeaderNear(world, e, at)) calm *= 2f;
            s.Level = Math.Clamp(s.Level + (float)dt * (up * Reach(world, e) - calm), 0f, 1f);
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
            if (!world.HasComponent<Stress>(e) || !Feels(world, e)) return;   // a man down (or no longer a combatant) breaks no more
            float breakAt = BreakLevel(world, e);
            var s = world.GetComponentRO<Stress>(e);
            SensorChange? edge = null;
            if (s.Broken == 0)
            {
                if (s.Level < breakAt) return;
                s.Broken = 1;
                s.CalmSince = 0;
                edge = SensorChange.StressBreak;
            }
            else if (s.Level < RecoverFraction * breakAt)
            {
                if (s.CalmSince <= 0) s.CalmSince = now;
                else if (now - s.CalmSince >= RecoverSeconds) { s.Broken = 0; s.CalmSince = 0; edge = SensorChange.StressRecovered; }
            }
            else s.CalmSince = 0;
            world.SetComponent(e, s);
            if (edge is { } what && world.Bus.IsRegistered<SensorChangedEvent>())
                world.Bus.Publish(new SensorChangedEvent { Unit = e, What = what });
            if (s.Broken != 0) StartBreak(world, e);
        }

        /// <summary>Starts his break unless it (or his surrender, or a grenade's reflex) already runs: he surrenders when his resolve is low
        /// and an enemy covers him, else he runs to a retreat point when he has one, else he goes to ground.</summary>
        public static void StartBreak(EntityRepository world, Entity e)
        {
            if (!world.HasComponent<BehaviorState>(e)) return;
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
            if (Retreat(world, e, out var to)) { dto.HasRetreat = true; dto.Retreat = to; }
            SopActions.React(world, e, Hrot.Map.Definitions.Behavior.BehaviorNames.Break, ReactionUrgency.Break, in dto);
        }

        /// <summary>TS-6 — he gives up: effective resolve under <see cref="SurrenderResolve"/> and a forces rifle on him (<see cref="Covering.By"/>).</summary>
        public static bool Surrenders(EntityRepository world, Entity e)
            => TemperamentOf.Effective(world, e, TemperamentTrait.Resolve) < SurrenderResolve && !Covering.By(world, e).IsNull;

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

        private static bool Ensure(EntityRepository world, Entity e, double now)
        {
            if (!world.IsComponentTypeRegistered<Stress>() || !world.HasComponent<SimTransform>(e)) return false;
            if (!Feels(world, e)) return false;
            if (!world.HasComponent<Stress>(e)) world.AddComponent(e, new Stress { LastStepAt = now });
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
    public sealed class StressSystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _people;

        public StressSystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Stress>() || !repo.IsComponentTypeRegistered<StanceIntent>()) return;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0.0;

            foreach (var evt in repo.ReadEvents<SensorChangedEvent>())
            {
                switch (evt.What)
                {
                    case SensorChange.NearMiss when Owned(repo, evt.Unit): StressModel.Add(repo, evt.Unit, StressModel.NearMiss, now); break;
                    case SensorChange.Hit when Owned(repo, evt.Unit): StressModel.Add(repo, evt.Unit, StressModel.Hit, now); break;   // a man down bleeds Hit edges: Feels refuses him
                    case SensorChange.SquadMemberDown: StressModel.FriendFell(repo, evt.Unit, evt.Target, now, _gateOnAuthority); break;
                }
            }

            _people ??= repo.Query().With<StanceIntent>().With<BehaviorState>().With<Health>().With<SimTransform>().Build();
            foreach (var e in _people)
            {
                if (!Owned(repo, e)) continue;
                StressModel.Step(repo, e, now);
                StressModel.Judge(repo, e, now);   // ⭐ CE-2272 (TS-6) — the break and the recovery
            }
        }

        private bool Owned(EntityRepository repo, Entity e)
            => !_gateOnAuthority || (repo.IsAlive(e) && repo.HasComponent<BehaviorState>(e) && repo.HasAuthority<BehaviorState>(e));
    }
}
