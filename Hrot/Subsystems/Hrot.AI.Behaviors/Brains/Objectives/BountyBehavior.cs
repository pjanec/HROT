using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S7 P-8b (📄 docs/DESIGN_Scenario_Rules.md §00.13 P-8) — <c>Bounty</c> on a man (<c>common.subject</c>). <b>Active ⇔ a bounty stands</b>: the linked <c>Count streak</c> measure's Value ≥ <c>minStreak</c> (default 5),
    /// he is in the fight, and the bounty is armed. His downing (a <see cref="ManDownedEvent"/> whose Victim is the subject — the ONE kill authority) while Active CLAIMS it: Pending, <c>FireCount</c> +1,
    /// <c>LastFired</c> = now, Reason "claimed by &lt;killer&gt;". It is armed again only after the linked streak has been below <c>minStreak</c> since the claim — one bounty per streak. Starts Pending.
    /// At the exercise close: Achieved if it still stands, else Skipped. A map / HUD marker is a READ of any Active <c>Bounty</c> status; <c>Count bountyKills</c> credits a kill on a man whose bounty is Active or claimed at this very time.
    /// </summary>
    public static class BountyBehavior
    {
        public const string Kind = BehaviorNames.Bounty;
        public const int DefaultMinStreak = 5;
        /// <summary>P-8b′ — how long (sim s) an Active bounty whose man left the fight waits for the <see cref="ManDownedEvent"/> that claims it (the event can arrive a frame or two after the man is seen down).</summary>
        public const double ClaimWaitSeconds = 3.0;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public EntityRef Streak; public int MinStreak;
            /// <summary>Working state (recorded): 0 after a claim until the streak is seen below the minimum.</summary>
            public byte Armed; public byte Started;
            /// <summary>P-8b′ — sim time his man was first seen out of the fight while the bounty stood (waiting for the downing event); <c>Waiting</c> says it is set.</summary>
            public SimInstant DownSince; public byte Waiting;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BountyBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.Bounty)]
        public static void Resolve(in BountyParamsJsonDto authored, ref BountyBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Streak = authored.Streak.Ref,
                MinStreak = authored.MinStreak is > 0 ? authored.MinStreak.Value : DefaultMinStreak, Armed = 1,
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            // Hold: this kind closes itself (its state is Pending between bounties, which the host would not evaluate)
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Hold);
            if (!f.Ready) return NodeStatus.Running;   // ⭐ CE-4568 — its components land at the merge; it begins next tick
            if (f.Reset) { b.Armed = 1; b.Started = 0; b.Waiting = 0; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            bool live = s.Closed == 0 && s.State is ObjectiveState.Active or ObjectiveState.Pending;
            if (live && ExerciseResult.PhaseOf(world) == ExercisePhase.Running)
            {
                var subject = b.Common.Subject.Resolve(world);
                bool wasActive = s.State == ObjectiveState.Active && b.Started != 0;
                if (wasActive && Claimed(world, subject, out var killer))
                {
                    s.FireCount++; s.LastFired = f.Now; b.Armed = 0; b.Waiting = 0;
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Pending, $"claimed by {NameOf(world, killer)}", f.Now);
                }
                else if (wasActive && !(!subject.IsNull && world.IsAlive(subject) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, subject)))
                {
                    // ⭐ P-8b′ — his man left the fight: the bounty STANDS until the downing event claims it (above, next ticks) or ClaimWaitSeconds pass
                    if (b.Waiting == 0) { b.Waiting = 1; b.DownSince = (SimInstant)f.Now; }
                    if (f.Now - b.DownSince >= ClaimWaitSeconds)
                    {
                        b.Waiting = 0;
                        ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Pending, "down, no claim", f.Now);
                    }
                    else ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, "down — awaiting the downing", f.Now);
                }
                else
                {
                    b.Waiting = 0;
                    var link = b.Streak.Resolve(world);
                    float streak = !link.IsNull && world.IsAlive(link) && world.HasComponent<ObjectiveStatus>(link) ? world.GetComponentRO<ObjectiveStatus>(link).Value : 0f;
                    if (streak < b.MinStreak) b.Armed = 1;
                    bool stands = b.Armed != 0 && streak >= b.MinStreak && !subject.IsNull && world.IsAlive(subject) && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, subject);
                    if (stands) ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"streak {streak:0} (bounty from {b.MinStreak})", f.Now);
                    else if (s.State != ObjectiveState.Pending || b.Started == 0 || s.Reason.Length == 0)
                        ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Pending, $"streak {streak:0} of {b.MinStreak}", f.Now);
                    b.Started = 1;
                }
            }
            else if (s.Closed == 0 && ExerciseResult.PhaseOf(world) != ExercisePhase.Running)
            {
                // the close: a bounty still standing is Achieved, otherwise Skipped (it never — or no longer — stood)
                ObjectiveHost.Settle(ref s, Kind, s.State == ObjectiveState.Active ? ObjectiveState.Achieved : ObjectiveState.Skipped, s.State == ObjectiveState.Active ? "still standing at the end" : "no bounty at the end", f.Now);
                ObjectiveRules.OnClose(ref s, CloseRule.Ordinary, f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        /// <summary>This frame's <see cref="ManDownedEvent"/> for <paramref name="subject"/>: the claim, and who downed him.</summary>
        private static bool Claimed(EntityRepository world, Entity subject, out Entity killer)
        {
            killer = Entity.Null;
            if (subject.IsNull || !world.Bus.IsRegistered<ManDownedEvent>()) return false;
            var downs = world.ReadEvents<ManDownedEvent>();
            for (int i = 0; i < downs.Length; i++)
                if (downs[i].Victim == subject) { killer = downs[i].Source.IsNull ? downs[i].OriginalSource : downs[i].Source; return true; }
            return false;
        }

        private static string NameOf(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && world.HasComponent<EntityInfo>(e) && world.GetComponentRO<EntityInfo>(e).Name.ToString() is { Length: > 0 } n ? n : "unknown";

        [Behavior(BehaviorNames.Bounty, Params = typeof(BountyParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Bounty, Curated = true, Group = "Library/Objectives", ParamsType = typeof(BountyBlackboard))]
        public static BTreeBuilder<BountyBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BountyBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
