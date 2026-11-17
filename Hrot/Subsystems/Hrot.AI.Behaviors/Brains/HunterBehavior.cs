using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4649</c> PART D (AN-21g; docs/designs/animals/DESIGN_Animals.md §4c) — the HUNTER's standing orders. <c>HunterSop</c>: GAME in sight (an animal of a species his TYPE's <c>HuntingRules</c> names — read through the ONE
    /// seam <c>Animals.MayEngage</c>) → react <c>Hunt</c>; idle → nothing. <c>Hunt</c> shoots the nearest game he sees FROM WHERE HE STANDS through the ONE aimed step (<c>PostureNodes.Fire</c> — the weapon brain's gates, ROE and force
    /// level all apply) and ends when none is left in sight. ⛔ It names no person and no dog: <c>MayEngage</c> can name neither. (A soldier's <c>BasicInfantrySop</c> reaction, FightFromCover, takes cover in the open and never fires.)
    /// </summary>
    public static class HunterBehavior
    {
        /// <summary>How recently he must have SEEN the game (ticks, 2 s at 60 Hz).</summary>
        public const uint InSightTicks = 120;
        public const float CooldownSeconds = 0.8f;
        /// <summary>He shoots game inside this flat distance only (m): the 12-gauge's sporting range (its pellets are spent past 60 m) — a hunter waits for the dogs to bring the game to him.</summary>
        public const float EngageRangeMetres = 40f;

        [StructLayout(LayoutKind.Sequential)]
        public struct HuntBlackboard { public EngageState Engage; }

        /// <summary>Game he SAW within <see cref="InSightTicks"/> that is alive, in the fight and named by his rule.</summary>
        [SharedAiCondition]
        public static bool GameInSight(Entity self, EntityRepository world) => NearestGame(self, world, InSightTicks, out _);

        private static unsafe bool NearestGame(Entity self, EntityRepository world, uint withinTicks, out Entity game)
        {
            game = Entity.Null;
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(self)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint now = PerceptionClock.Now(world);
            float best = float.MaxValue;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            for (int i = 0; i < mem.Count && i < PerceptionConstants.MaxTrackedTargets; i++)
            {
                if (mem.EntityIds[i] <= 0 || TargetMemory.IsAnonymous(in mem, i)) continue;
                if ((mem.Modalities[i] & (byte)SensorModality.Visual) == 0) continue;
                if (mem.LastSeenTick[i] <= now && now - mem.LastSeenTick[i] > withinTicks) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(e) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, e) || !Fdp.Toolkit.Animals.Animals.MayEngage(world, self, e)) continue;
                var p = world.GetComponentRO<SimTransform>(e).Position;
                float dx = p.X - here.X, dy = p.Y - here.Y, d = dx * dx + dy * dy;
                if (d > EngageRangeMetres * EngageRangeMetres) continue;
                if (d < best) { best = d; game = e; }
            }
            return !game.IsNull;
        }

        [BehaviorResolver(BehaviorNames.Hunt)]
        public static void ResolveHunt(in NoParams authored, ref HuntBlackboard block, EntityRepository world, Entity self) => block = default;

        /// <summary>Shoots the nearest game in sight (the weapon brain decides the round, the hold and the force level); Success once none is in sight for a while.</summary>
        [SharedAiAction]
        public static NodeStatus Tick(ref EngageState ws, Entity self, EntityRepository world)
        {
            if (!NearestGame(self, world, InSightTicks, out var game)) return NodeStatus.Success;
            PostureNodes.Fire(world, self, ref ws, CooldownSeconds, 0, game);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.Hunt, Params = typeof(NoParams), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.Hunt, Curated = true, Group = "Library/Combat", ParamsType = typeof(HuntBlackboard))]
        public static BTreeBuilder<HuntBlackboard, BTreeContext> BuildHunt()
            => new BTreeBuilder<HuntBlackboard, BTreeContext>().Action(bb => bb.Engage, Tick);
    }

    /// <summary>⭐ <c>HunterSop</c> — see <see cref="HunterBehavior"/>.</summary>
    public static class HunterSop
    {
        [Behavior(BehaviorNames.HunterSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.HunterSop, AssetId = "a4649000-0000-0000-0000-000000000001", Curated = true, Group = "Library/Sop")]
        public static BTreeBuilder<byte, BTreeContext> Build()
            => new BTreeBuilder<byte, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(HunterBehavior.GameInSight, Id("c4649000-0000-0000-0000-000000000011"))
                        .Label("Game in sight (his rule's species, seen within 2 s)")
                    .React(BehaviorNames.Hunt, ReactionUrgency.Contact, Id("c4649000-0000-0000-0000-000000000012"))
                        .Label("React: Hunt")
                        .Comment("CE-4649 part D (AN-21g): shoots the game from where he stands; never a person, never a dog (Animals.MayEngage names neither)."),
                    Id("c4649000-0000-0000-0000-000000000010"))
                    .Label("Game → hunt")
                .DoWhenIdle(BehaviorNames.Idle, Id("c4649000-0000-0000-0000-000000000020"))
                    .Label("Idle: Idle"),
                Id("c4649000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins");
    }
}
