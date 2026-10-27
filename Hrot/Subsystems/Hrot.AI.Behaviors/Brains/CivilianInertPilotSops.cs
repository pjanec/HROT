using Fbt;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐⭐ CE-1410 (R-1042) — the three SOPs that carry no variables, as C#-built trees: node for node the retired
    //   Assets/BTrees/Library/Sop/{CivilianSop,InertSop,PilotSop}.btree.json, their ids, titles and comments kept. No blackboard, so no
    //   params region and no parse (as their assets had none) — the blackboard type is a byte nothing reads.

    /// <summary>⭐ <c>CivilianSop</c> — a civilian reacts under ANY task (CE-2322, AQ97 D2, R-2062); idle, he lives his routine.</summary>
    public static class CivilianSop
    {
        [Behavior(BehaviorNames.CivilianSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.CivilianSop, AssetId = "a2322000-0000-0000-0000-000000000002", Curated = true, PublishesVariables = true, Group = "Library/Sop")]
        public static BTreeBuilder<byte, BTreeContext> Build()
            => new BTreeBuilder<byte, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(CivilianBehavior.NotOnHisRoutine, Id("c2322002-0000-0000-0000-000000000011"))
                        .Label("Not on his routine (CivilianHsm)")
                    .Condition(CivilianBehavior.FrightenedOrHurt, Id("c2322002-0000-0000-0000-000000000012"))
                        .Label("Frightened past the alarm, or wounded")
                    .React(BehaviorNames.CivilianReact, ReactionUrgency.UnderFire, Id("c2322002-0000-0000-0000-000000000013"))
                        .Label("React: CivilianReact")
                        .Comment("Alarmed → panic → shelter / cower → calm: done, and his task restarts (R-199)."),
                    Id("c2322002-0000-0000-0000-000000000010"))
                    .Label("On another task, frightened or hurt → react")
                .DoWhenIdle(BehaviorNames.CivilianHsm, Id("c2322002-0000-0000-0000-000000000020"))
                    .Label("Idle: his routine (CivilianHsm)")
                    .Comment("An order finished: he goes back to his own life."),
                Id("c2322002-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("CE-2322 (AQ97 D2, R-2062): a civilian reacts under ANY task. His routine (CivilianHsm) keeps his fear itself, so the rows stand aside while it runs.");
    }

    /// <summary>⭐ <c>InertSop</c> — the explicit NONE (CE-2322, AQ97 C): no idle choice and no reactions.</summary>
    public static class InertSop
    {
        [Behavior(BehaviorNames.InertSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.InertSop, AssetId = "a2322000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Sop")]
        public static BTreeBuilder<byte, BTreeContext> Build()
            => new BTreeBuilder<byte, BTreeContext>()
            .Wait(3600f, Id("c2322001-0000-0000-0000-000000000002"))
                .Label("Nothing — no idle choice, no reactions")
                .Comment("CE-2322 (AQ97 C, R-2062): the explicit NONE. Once a type names a default SOP, an absent Sop key means that default; assign this to keep a unit doing exactly what it is told and nothing on its own (the old 'no SOP').");
    }

    /// <summary>⭐ <c>PilotSop</c> — the standing orders of a SPAWNED pilot (CE-1118 H-3, Airborne Insertion §6.5 D-23).</summary>
    public static class PilotSop
    {
        [Behavior(BehaviorNames.PilotSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.PilotSop, AssetId = "a1118000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Sop")]
        public static BTreeBuilder<byte, BTreeContext> Build()
            => new BTreeBuilder<byte, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(AirborneIndividualNodes.OnTheGround, Id("c1118000-0000-0000-0000-000000000011"))
                        .Label("On the ground (no descent, not aboard)")
                    .React(BehaviorNames.EvadeAndSignal, ReactionUrgency.Alert, Id("c1118000-0000-0000-0000-000000000012"))
                        .Label("React: EvadeAndSignal")
                        .Comment("Never ends on its own: he stays down."),
                    Id("c1118000-0000-0000-0000-000000000010"))
                    .Label("Down on the ground → evade")
                    .Comment("Out of his harness: away from the enemy, into cover, down, and wait for the rescue (Air Ops §4c / §9)."),
                Id("c1118000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("CE-1118 H-3 (Airborne Insertion §6.5 D-23): the standing orders of a SPAWNED pilot — nothing in a scenario gives him a task, so his type's SOP does.");
    }
}
