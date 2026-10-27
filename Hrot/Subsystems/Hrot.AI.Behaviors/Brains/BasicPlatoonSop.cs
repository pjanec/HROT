using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Squad.Command;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — <c>BasicPlatoonSop</c>, the platoon commander's standing orders (CE-2294 CC-D, Command Chain §6), as a
    /// C#-built tree: node for node the retired <c>Assets/BTrees/Library/Sop/BasicPlatoonSop.btree.json</c>, its ids, titles and comments
    /// kept. Each row reads the platoon's PICTURE — what its squads reported — never their state.
    /// </summary>
    public static class BasicPlatoonSop
    {
        /// <summary>The tree's tuning — the variables the asset's blackboard carried, with their defaults (its constructor, R-3002).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public SopSenseParams ReportReceived;
            public ElementReportedParams EnemyNear;

            public Blackboard()
            {
                ReportReceived = new SopSenseParams { Kind = SensorChange.ReportReceived, Seconds = 3f };
                EnemyNear      = new ElementReportedParams { MinSize = ElementSize.Team, WithinMetres = 400f, FreshSeconds = 30f };
            }
        }

        /// <summary>An SOP takes no params (CE-2132): its variables are baked tuning, and a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.BasicPlatoonSop)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.BasicPlatoonSop, typeof(NoParams).FullName!);

        [Behavior(BehaviorNames.BasicPlatoonSop, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.BasicPlatoonSop, AssetId = "a2294000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Sop",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Selector(rows => rows
                .Sequence(row => row
                    .Condition(PlatoonDefendBehavior.NotDefending, Id("c2284000-0000-0000-0000-000000000021"))
                        .Label("Not defending a position")
                    .Condition(PlatoonAttackBehavior.NotAttacking, Id("c2284000-0000-0000-0000-000000000022"))
                        .Label("Not attacking")
                    .Condition(SupportCommandBehavior.NotCommandingSupport, Id("c2284000-0000-0000-0000-000000000023"))
                        .Label("Not commanding support")
                    .Condition(bb => bb.ReportReceived, SopConditions.SensedFresh, Id("c2284000-0000-0000-0000-000000000024"))
                        .Label("A report arrived (fresh, 3 s)")
                    .Condition(UrbanMovementBehavior.FireFromBuildingToClear, Id("c2284000-0000-0000-0000-000000000025"))
                        .Label("Fired on from a building, to clear")
                    .React(BehaviorNames.ReactToFireFromBuilding, ReactionUrgency.Contact, Id("c2284000-0000-0000-0000-000000000026"))
                        .Label("React: ReactToFireFromBuilding")
                        .Comment("The squad fired on supports from where it stands; the next squad clears the building; the movement resumes after."),
                    Id("c2284000-0000-0000-0000-000000000020"))
                    .Label("Fired on from a building on the move → clear it")
                    .Comment("CE-2284 U-2 (UM-10, UM-11; DA-18): moving through town, a squad was fired on from a building (CE-2283 FiredOnFromStructure, its CONTACT in the picture) and DA-18 says clear — Untrained / Militia, or the building within 50 m of the route. Bypass = no row: the movement carries on.")
                .Sequence(row => row
                    .Condition(PlatoonDefendBehavior.NotDefending, Id("c2294000-0000-0000-0000-000000000014"))
                        .Label("Not defending a position")
                        .Comment("CE-2276 DA-55: a defending platoon's squads already hold their posts — holding them again would pull them off the posts and stop the defence's trigger.")
                    .Condition(PlatoonAttackBehavior.NotAttacking, Id("c2294000-0000-0000-0000-000000000015"))
                        .Label("Not attacking")
                        .Comment("CE-2277 AT-13: an attacking platoon expects contact — its base of fire and its control region answer it; holding every squad would stop the attack at its first SPOT.")
                    .Condition(SupportCommandBehavior.NotCommandingSupport, Id("c2294000-0000-0000-0000-000000000016"))
                        .Label("Not commanding support")
                        .Comment("CE-2314: a headquarters answers reports — holding it on a SPOT would order its aircraft and its reserve to hold.")
                    .Condition(bb => bb.ReportReceived, SopConditions.SensedFresh, Id("c2294000-0000-0000-0000-000000000011"))
                        .Label("A report arrived (fresh, 3 s)")
                    .Condition(bb => bb.EnemyNear, PictureConditions.ElementReported, Id("c2294000-0000-0000-0000-000000000012"))
                        .Label("An enemy element in the picture")
                    .React(BehaviorNames.PlatoonHold, ReactionUrgency.Contact, Id("c2294000-0000-0000-0000-000000000013"))
                        .Label("React: PlatoonHold")
                        .Comment("Both squads hold; the platoon's task waits in its state."),
                    Id("c2294000-0000-0000-0000-000000000010"))
                    .Label("Enemy element reported → hold")
                    .Comment("A squad reported an enemy element (team-size or larger, within 400 m, seen within 30 s): every squad holds until it is gone."),
                Id("c2294000-0000-0000-0000-000000000002"))
                .Label("Rows — first true wins")
                .Comment("The platoon commander's standing orders (CE-2294 CC-D, Command Chain §6): each row reads the platoon's PICTURE — what its squads reported — never their state. The reaction pauses the platoon's task; when it ends, the task resumes in its state.");
    }
}
