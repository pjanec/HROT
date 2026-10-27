using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-1410</c> (R-1042) — <c>DangerCrossing</c> (CE-3079 H5, docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.4) as a C#-built tree: node
    /// for node the retired <c>Assets/BTrees/Library/Tactics/DangerCrossing.btree.json</c>, its ids, titles and comments kept. Its contract
    /// stays <see cref="DangerCrossingParams"/>, written over the baked tuning by <see cref="DangerAreaNodes.ResolveDangerCrossing"/>.
    /// </summary>
    public static class DangerCrossing
    {
        /// <summary>The tree's block: <see cref="DangerCrossingConfig"/> first (the resolver writes it at offset 0), then each stateful node's
        /// working memory. Its constructor is the asset's <c>ParamsDefaultJson</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public DangerCrossingConfig Params;
            public DangerSensorState SensorState;
            public HoldShortState HoldState;
            public CrossState CrossState;

            public Blackboard()
            {
                Params = new DangerCrossingConfig
                {
                    sensor     = new DangerSensorParams(),
                    holdAhead  = new DangerAheadParams { MinThreat = 0.5f, WithinMetres = 60f, KindMask = 0 },
                    hold       = new HoldShortParams { MinThreat = 0.5f, Speed = 1.5f, ArrivalRadius = 1.5f },
                    crossAhead = new DangerAheadParams { MinThreat = 0f, WithinMetres = 15f, KindMask = 14 },
                    cross      = new CrossParams { Speed = 4.5f, ArrivalRadius = 1.5f },
                    walk       = new CgfNodes.MoveToLocationParams { X = 0f, Y = 0f, Speed = 1.5f, ArrivalRadius = 3f },
                };
                SensorState = default;
                HoldState = default;
                CrossState = default;
            }
        }

        // ⭐ The contract and its resolver live beside the nodes (DangerAreaNodes.ResolveDangerCrossing, [Behavior] there).
        [BTreeDefinition(BehaviorNames.DangerCrossing, AssetId = "a3079000-0000-0000-0000-000000000002", Curated = true, PublishesVariables = true, Group = "Library/Tactics",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Sequence(run => run
                .StatefulAction(bb => bb.Params.sensor, bb => bb.SensorState, DangerAreaNodes.EnsureSensor, Id("c3079100-0000-0000-0000-000000000003"))
                    .Label("Danger sensor: me → objective")
                    .Comment("Route source ③ (to a point): the route is ALWAYS unit → objective, never the unit's own move. The run's end releases it.")
                .ObserverSelector(next => next
                    .Sequence(holdShort => holdShort
                        .Condition(bb => bb.Params.holdAhead, DangerAreaNodes.DangerAhead, Id("c3079100-0000-0000-0000-000000000006"))
                            .Label("A threatened area within 60 m")
                        .ForceFailure(fallThrough => fallThrough
                            .StatefulAction(bb => bb.Params.hold, bb => bb.HoldState, DangerAreaNodes.HoldShort, Id("c3079100-0000-0000-0000-000000000007"))
                                .Label("Hold at its near side")
                                .Comment("Ends when the threat falls below MinThreat − 0.1, or no area is ahead."),
                            Id("c3079100-0000-0000-0000-000000000107"))
                            .Comment("a released hold falls through to the crossing / the walk, never ends the run"),
                        Id("c3079100-0000-0000-0000-000000000005"))
                        .Label("Hold short of a watched area")
                    .Sequence(rush => rush
                        .Condition(bb => bb.Params.crossAhead, DangerAreaNodes.DangerAhead, Id("c3079100-0000-0000-0000-000000000009"))
                            .Label("A crossing / intersection / open ground within 15 m")
                        .ForceFailure(fallThrough => fallThrough
                            .StatefulAction(bb => bb.Params.cross, bb => bb.CrossState, DangerAreaNodes.Cross, Id("c3079100-0000-0000-0000-000000000010"))
                                .Label("Cross: near → far side")
                                .Comment("Never re-crosses the area it just crossed (a stale answer can still list it)."),
                            Id("c3079100-0000-0000-0000-000000000110"))
                            .Comment("a finished crossing falls through to the walk, never ends the run"),
                        Id("c3079100-0000-0000-0000-000000000008"))
                        .Label("Rush across an exposed area")
                    .Action(bb => bb.Params.walk, CgfNodes.Action_WriteMoveToChannel, Id("c3079100-0000-0000-0000-000000000011"))
                        .Label("Walk to the objective")
                        .Comment("Success on arrival ends the run."),
                    Id("c3079100-0000-0000-0000-000000000004"))
                    .Label("React to the next area ahead")
                    .Comment("While a lower branch runs, the HIGHER guards are re-checked every tick; a running branch's own guard is not (HoldShort's hysteresis works because of that)."),
                Id("c3079100-0000-0000-0000-000000000002"))
                .Label("Watch the route, then go")
                .Comment("CE-3079 H5 — docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.4. The objective is set TWICE in the order's params: sensor.RouteTo (the route watched) and walk.X/Y (where it walks).");
    }
}
