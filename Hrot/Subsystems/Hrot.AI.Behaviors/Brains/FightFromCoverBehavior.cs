using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2331</c> (AQ98 A/B) — the parameters of <see cref="FightFromCoverBehavior.Run"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FightFromCoverParams
    {
        /// <summary>How long nothing must land near him, once in cover, before the reaction ends (s).</summary>
        public float QuietSeconds;
        /// <summary>The fight from cover (the library defaults).</summary>
        public PeekAndFireParams Peek;
        /// <summary>No cover anywhere: down in the open until a point comes (<c>TakeCover</c>, AQ96 C — the library defaults).</summary>
        public EqsTacticsParams Open;
        /// <summary>⭐ <c>CE-2357</c> (AQ102 E) — the flank (<c>EqsTacticsNodes.Flank</c>) a trained man runs once the enemy has been out of
        /// sight for <see cref="FlankAfterSeconds"/> (s).</summary>
        public EqsTacticsParams Flank;
        public float FlankAfterSeconds;
    }

    /// <summary>⭐ <c>CE-2331</c> — what <see cref="FightFromCoverBehavior.Run"/> keeps: the fight's own state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FightFromCoverState
    {
        public PeekAndFireState Peek;
        /// <summary>1 once <c>PeekAndFire</c> found no cover (it answered Failure): the rest of the run is <see cref="Open"/>.</summary>
        public byte InTheOpen;
        public EqsTacticsState Open;
        /// <summary>⭐ <c>CE-2357</c> — 1 while he flanks the remembered point, 2 while he closes in on it (the PeekAndFire half released);
        /// how many of those this run has tried.</summary>
        public byte Flanking, FlanksTried;
        public EqsTacticsState Flank;
        /// <summary>⭐ <c>CE-2357</c> — when the last escalation ended (sim s): the next waits another <c>FlankAfterSeconds</c>.</summary>
        public double EscalatedAt;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2331</c> / <c>CE-2332</c> (AQ98 A, B, C — R-2070; 📄 docs/blueprints/Architect_Question_98_Cover_Reaction_Fights_Back.md §3) —
    /// <c>FightFromCover</c>, the soldier's cover REACTION (<c>BasicInfantrySop</c>'s hit, near-miss, first-threat and enemy-in-sight rows): it
    /// FIGHTS from cover — the library <see cref="PeekAndFireNodes.PeekAndFire"/> (hide, expose, fire per the ROE, down when fired on) — and
    /// it ENDS once he is in cover and nothing has landed near him for <see cref="FightFromCoverParams.QuietSeconds"/>, so the task the
    /// reaction paused resumes and decides the fight. 📐 Why the end: the hide-only <c>TakeCover</c> lasted while a threat was remembered,
    /// which in a battle is always — 9 of 24 men spent 64–79 % of <c>pc-defend-house</c> hiding with the enemy in sight.
    /// </summary>
    public static class FightFromCoverBehavior
    {
        public const float DefaultQuietSeconds = 8f;

        /// <summary>The open-ground half: the shipped <c>TakeCover</c> asset's numbers, and it fires while it moves (the ROE gates every shot).</summary>
        public static readonly EqsTacticsParams OpenDefaults = new()
        {
            SearchRadius = 60f, MinRepositionMetres = 5f, Speed = 2.5f, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f, FireWhileMoving = 1,
        };

        /// <summary>⭐ <c>CE-2357</c> (AQ102 E) — the flank on the remembered point: within 20 m (the next cover or two — 📐 40 m sent a man round a
        /// house and past his enemy, <c>rf-house-obstacle</c>), at the rush, firing on the way when he sees him.</summary>
        public static readonly EqsTacticsParams FlankDefaults = new()
        {
            SearchRadius = 20f, MinRepositionMetres = 5f, Speed = 4f, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f, FireWhileMoving = 1,
        };

        /// <summary>⭐ AQ102 E — how long the enemy must be out of his sight before he flanks (s), and how many flanks one run tries.</summary>
        public const float DefaultFlankAfterSeconds = 10f;
        public const byte MaxFlanks = 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct FightFromCoverBlackboard
        {
            public FightFromCoverParams Fight;
            public FightFromCoverState State;
        }

        [BehaviorResolver(BehaviorNames.FightFromCover)]
        public static void Resolve(in FightFromCoverParamsJsonDto authored, ref FightFromCoverBlackboard block, EntityRepository world, Entity self)
        {
            block.Fight = new FightFromCoverParams
            {
                QuietSeconds = authored.QuietSeconds > 0f ? authored.QuietSeconds : DefaultQuietSeconds,
                Open = OpenDefaults,
                Flank = FlankDefaults,
                FlankAfterSeconds = DefaultFlankAfterSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.FightFromCover, Params = typeof(FightFromCoverParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.FightFromCover, Curated = true, Group = "Library/Tactics", ParamsType = typeof(FightFromCoverBlackboard))]
        public static BTreeBuilder<FightFromCoverBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<FightFromCoverBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Fight, bb => bb.State, Run);

        /// <summary>The fight from cover until it is QUIET in cover (Success, the reaction ends); <c>PeekAndFire</c>'s own end — nothing left
        /// to fight — ends it too. ⭐ No cover anywhere (<c>PeekAndFire</c> answers Failure after its <c>NoCoverSeconds</c>): the rest of
        /// the run is <c>TakeCover</c> — down in the open until a point comes, moving to it when one does (AQ96 C, <c>CE-2322</c>) — and it
        /// ends the same way, once he is not on the move and it has been quiet.</summary>
        [SharedAiAction]
        public static NodeStatus Run(ref FightFromCoverParams p, ref FightFromCoverState ws, Entity self, EntityRepository world)
        {
            if (ws.InTheOpen == 0)
            {
                // ⭐⭐ AQ102 E (CE-2357, R-2072) — the enemy out of sight for FlankAfterSeconds: a trained man FLANKS where he was LAST SEEN
                //   (a point side-on to it that sees it — never his live position), then fights from cover again there — instead of peeking
                //   at the same car for ever
                if (ws.Flanking != 0)
                {
                    // ⭐ CE-2357 — the escalations alternate: a flank on the remembered point, then closing in to a throw of it, then a flank
                    var flank = ws.Flanking == 2
                        ? EqsTacticsNodes.CloseInRemembered(ref p.Flank, ref ws.Flank, self, world)
                        : EqsTacticsNodes.FlankRemembered(ref p.Flank, ref ws.Flank, self, world);
                    if (flank == NodeStatus.Running) return NodeStatus.Running;
                    Release(ref p, ref ws, self, world);
                    ws.EscalatedAt = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
                    ws.Flanking = 0;
                    ws.Peek = default;   // a fresh fight from where the flank left him
                }
                else if (ShouldFlank(in p, in ws, self, world))
                {
                    StartFlank(ref p, ref ws, self, world);
                    return NodeStatus.Running;
                }
                if (InCover(in ws) && Quiet(world, self, p.QuietSeconds) && !WouldReturn(world, self))
                {
                    PeekAndFireNodes.Deactivate_PeekAndFire(ref p.Peek, ref ws.Peek, self, world);
                    return NodeStatus.Success;
                }
                var status = PeekAndFireNodes.PeekAndFire(ref p.Peek, ref ws.Peek, self, world);
                // ⭐ CE-2357 — PeekAndFire's own end (no LIVE threat: ~14 s unseen) while the enemy he saw is still remembered: he goes to
                //   look for him rather than idle where he hid. 📐 Live (rf-car-duel): both men ended their fights so and idled 28–33 s.
                if (status == NodeStatus.Success && MayFlank(in p, in ws, self, world) && WouldReturn(world, self))
                {
                    StartFlank(ref p, ref ws, self, world);
                    return NodeStatus.Running;
                }
                if (status != NodeStatus.Failure || !world.HasComponent<LocomotionChannel>(self)) return status;
                ws.InTheOpen = 1;   // no cover: PeekAndFire released its sensors, its move and its fire
            }
            if (ws.Open.Moving != 1 && Quiet(world, self, p.QuietSeconds) && !WouldReturn(world, self))
            {
                EqsTacticsNodes.Deactivate_TakeCover(ref p.Open, ref ws.Open, self, world);
                return NodeStatus.Success;
            }
            return EqsTacticsNodes.TakeCover(ref p.Open, ref ws.Open, self, world);
        }

        /// <summary>Leaving the reaction (a new hit, an order, the SOP's gate): whichever node runs releases its sensors, its move and its fire.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.FightFromCoverBehavior.Run")]
        public static void Deactivate_Run(ref FightFromCoverParams p, ref FightFromCoverState ws, Entity self, EntityRepository world)
        {
            if (ws.Flanking != 0) Release(ref p, ref ws, self, world);
            else if (ws.InTheOpen == 0) PeekAndFireNodes.Deactivate_PeekAndFire(ref p.Peek, ref ws.Peek, self, world);
            else EqsTacticsNodes.Deactivate_TakeCover(ref p.Open, ref ws.Open, self, world);
        }

        /// <summary>⭐ <c>CE-2357</c> (AQ102 E, J) — he is in cover; a hostile he has SEEN is remembered but not seen for
        /// <see cref="FightFromCoverParams.FlankAfterSeconds"/>; he is trained (Regular and up — militia only duck and fire blind, J);
        /// he holds no post (a posted man stays on it, DA-44); and this run has flanks left.</summary>
        public static bool ShouldFlank(in FightFromCoverParams p, in FightFromCoverState ws, Entity self, EntityRepository world)
        {
            if (!InCover(in ws) || !MayFlank(in p, in ws, self, world)) return false;
            if (ws.EscalatedAt > 0 && world.SimulationTime - ws.EscalatedAt < p.FlankAfterSeconds) return false;   // he only just moved
            if (!CoverFightConditions.EnemySeenAndRemembered(self, world)) return false;
            return !CoverFightConditions.SeenWithin(self, world, (uint)(p.FlankAfterSeconds * 60f));
        }

        /// <summary>⭐ <c>CE-2357</c> — he may flank at all: flanking on, flanks left this run, no post (DA-44), trained (J).</summary>
        public static bool MayFlank(in FightFromCoverParams p, in FightFromCoverState ws, Entity self, EntityRepository world)
            => p.FlankAfterSeconds > 0f && ws.FlanksTried < MaxFlanks && p.Peek.AnchorRadius <= 0f
               && ProficiencyOf.Tier(world, self) is not (ProficiencyTier.Untrained or ProficiencyTier.Militia)
               && NothingElseToDo(world, self);

        /// <summary>⭐ <c>CE-2357</c> — the fight paused nothing but his idle: an ORDERED task (a post, a move) waits for him where its order
        /// put him, so he never flanks away from it. 📐 Live (<c>rf-wall-hugger</c>): hit at their windows, two defenders' fights
        /// escalated three times and took them 50–59 m out of the house they were posted in.</summary>
        private static bool NothingElseToDo(EntityRepository world, Entity self)
        {
            var paused = BehaviorIngressSystem.PausedTaskOf(world, self);
            return paused == null || paused.BehaviorName == BehaviorNames.Idle;
        }

        private static void StartFlank(ref FightFromCoverParams p, ref FightFromCoverState ws, Entity self, EntityRepository world)
        {
            PeekAndFireNodes.Deactivate_PeekAndFire(ref p.Peek, ref ws.Peek, self, world);
            ws.Flank = default;
            ws.Flanking = ws.FlanksTried % 2 == 0 ? (byte)1 : (byte)2;   // flank, close in, flank …
            ws.FlanksTried++;
        }

        /// <summary>The running escalation's sensor and move go (each node releases its own site).</summary>
        private static void Release(ref FightFromCoverParams p, ref FightFromCoverState ws, Entity self, EntityRepository world)
        {
            if (ws.Flanking == 2) EqsTacticsNodes.Deactivate_CloseToThrow(ref p.Flank, ref ws.Flank, self, world);
            else EqsTacticsNodes.Deactivate_Flank(ref p.Flank, ref ws.Flank, self, world);
        }

        /// <summary>He is at his hide point or fighting from it (not choosing one, not on his way to one).</summary>
        public static bool InCover(in FightFromCoverState ws) => ws.Peek.Phase >= PeekPhase.Hidden;

        /// <summary>⭐ The quiet end exists so the paused TASK resumes; with no task to resume (none paused, or only <c>Idle</c>) and an
        /// enemy he has SEEN still remembered, ending would only let the SOP's enemy-in-sight row start the reaction again the moment he
        /// looks — the cover search restarting every few seconds. ⇒ he fights on; <c>PeekAndFire</c>'s own end (nothing left to fight —
        /// the enemy forgotten) still ends it.
        /// <para>⭐ <c>CE-1269</c> — "still remembered", not "in sight": the quiet end is checked while he HIDES, and a hidden man does
        /// not see. 📐 Measured live (<c>df-lone-soldier-reflex</c>): all three ends came through the quiet end with <c>EnemyInSight</c>
        /// false (phase Hidden / Expose), and the row restarted the reaction 0.1–0.2 s later, every 1–3 s.</para></summary>
        public static bool WouldReturn(EntityRepository world, Entity self)
        {
            if (!CoverFightConditions.EnemySeenAndRemembered(self, world)) return false;
            var paused = BehaviorIngressSystem.PausedTaskOf(world, self);
            return paused == null || paused.BehaviorName == BehaviorNames.Idle;
        }

        /// <summary>No hit and no near miss on him for <paramref name="seconds"/>.</summary>
        public static bool Quiet(EntityRepository world, Entity self, float seconds)
            => !RecentSensesOf.Within(world, self, SensorChange.Hit, seconds) && !RecentSensesOf.Within(world, self, SensorChange.NearMiss, seconds);
    }

    /// <summary>⭐ <c>CE-2332</c> (AQ98 C, R-2070) — the soldier SOP's enemy-in-sight row.</summary>
    public static class CoverFightConditions
    {
        /// <summary>How recently he must have SEEN the enemy (ticks, 1 s at 60 Hz; the clock perception stamps <c>LastSeenTick</c> with).</summary>
        public const uint InSightTicks = 60;

        private static readonly int IdleHash = BehaviorHash.FromName(BehaviorNames.Idle);

        /// <summary>A hostile he SAW (not only heard) within <see cref="InSightTicks"/>, alive and in the fight.</summary>
        [SharedAiCondition]
        public static bool EnemyInSight(Entity self, EntityRepository world) => SeenHostile(self, world, InSightTicks);

        /// <summary>⭐ <c>CE-1269</c> — a hostile he has SEEN (not only heard) and still remembers, however long ago: what the enemy-in-sight
        /// row will see again once he looks out of cover.</summary>
        public static bool EnemySeenAndRemembered(Entity self, EntityRepository world) => SeenHostile(self, world, uint.MaxValue);

        /// <summary>⭐ <c>CE-2357</c> — a hostile he SAW within <paramref name="ticks"/> (the clock perception stamps <c>LastSeenTick</c> with).</summary>
        public static bool SeenWithin(Entity self, EntityRepository world, uint ticks) => SeenHostile(self, world, ticks);

        private static unsafe bool SeenHostile(Entity self, EntityRepository world, uint withinTicks)
        {
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint now = PerceptionClock.Now(world);   // ⭐ the clock LastSeenTick is stamped in (CE-3292: sim steps), never SimulationTick
            for (int i = 0; i < mem.Count && i < PerceptionConstants.MaxTrackedTargets; i++)
            {
                if (mem.EntityIds[i] <= 0 || TargetMemory.IsAnonymous(in mem, i)) continue;
                if ((mem.Modalities[i] & (byte)SensorModality.Visual) == 0) continue;
                if (mem.LastSeenTick[i] <= now && now - mem.LastSeenTick[i] > withinTicks) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(e) || !Injury.IsInTheFight(world, e) || !KillZone.Hostile(world, self, e)) continue;
                return true;
            }
            return false;
        }

        /// <summary>⭐ <c>CE-3306</c> — his ROE lets him fire now (the aimed-fire executor's own gate): the enemy-in-sight row FIGHTS, so
        /// under Hold Fire it stands aside and the SOP's withdraw row decides (D5). 📐 Found once that row could fire at all (the
        /// perception-clock fix): under Hold Fire it took the man before the withdraw row's edge arrived.</summary>
        [SharedAiCondition]
        public static bool MayFire(Entity self, EntityRepository world) => AimAndFireExecutor.RoePermitsFire(world, self);

        /// <summary>His task slot is empty or runs only <c>Idle</c> (the SOP's do-when-idle): nobody gave him anything to do.</summary>
        [SharedAiCondition]
        public static bool NoTask(Entity self, EntityRepository world)
        {
            if (!world.HasComponent<BehaviorState>(self)) return true;
            int hash = world.GetComponentRO<BehaviorState>(self).ActiveBehaviorHash;
            return hash == 0 || hash == IdleHash;
        }
    }
}
