using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;
using static Hrot.AI.Behaviors.Brains.TreeNodeIds;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐⭐ CE-1410 (R-1042) — the library tactics (Assets/BTrees/Library/Tactics) as C#-built trees: node for node the retired
    //   {TakeCover,FallBack,Flank,FiringPosition,Sentry}.btree.json, their ids, titles and comments kept. A tree whose one Input was its
    //   params DTO keeps that contract: the DTO first in its blackboard (offset 0), the asset's ParamsDefaultJson as its constructor, and
    //   the order's JSON overlaid flat on top (BehaviorParams.OverlayFlatAt — what the asset's generated parse did). Each stateful node's
    //   working memory is a field of the block, one per node, as the asset's per-node slots were (the CombatPosture port's form, R-266).
    //   (DangerCrossing.cs and the hill-attack demo trees follow the same rules.)

    /// <summary>⭐ <c>TakeCover</c> — CE-2092 (docs/DESIGN_Eqs_Consuming_Behaviours.md §2): take cover from the top threat.</summary>
    public static class TakeCover
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public EqsTacticsParams Params;
            public EqsTacticsState State;

            public Blackboard()
            {
                Params = new EqsTacticsParams { SearchRadius = 60f, MinRepositionMetres = 5f, Speed = 2.5f, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f };
                State = default;
            }
        }

        /// <summary>The order's JSON IS <see cref="EqsTacticsParams"/>, flat, over the defaults (CE-2133).</summary>
        [BehaviorResolver(BehaviorNames.TakeCover)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.OverlayFlatAt<EqsTacticsParams>(json, memory, capacity);

        [Behavior(BehaviorNames.TakeCover, Params = typeof(EqsTacticsParams))]
        [BTreeDefinition(BehaviorNames.TakeCover, AssetId = "a2092000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Tactics",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .StatefulAction(bb => bb.Params, bb => bb.State, EqsTacticsNodes.TakeCover, Id("c2092000-0000-0000-0000-000000000002"))
                .Label("Take cover from the top threat")
                .Comment("CE-2092 — docs/DESIGN_Eqs_Consuming_Behaviours.md §2. Running while the unit remembers a threat: its own FindCoverFromTarget sensor pointed at the top threat; each new answer moves the unit only when the best point is MinRepositionMetres or more from the current goal. Success when nothing is left to hide from.");
    }

    /// <summary>⭐ <c>FallBack</c> — CE-2093 (docs/DESIGN_Eqs_Consuming_Behaviours.md §2): fall back from the top threat.</summary>
    public static class FallBack
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public EqsTacticsParams Params;
            public EqsTacticsState State;

            public Blackboard()
            {
                Params = new EqsTacticsParams { SearchRadius = 80f, MinRepositionMetres = 0f, Speed = 3.5f, ArrivalRadius = 1.5f, ScoreDeltaThreshold = 0.05f };
                State = default;
            }
        }

        /// <summary>The order's JSON IS <see cref="EqsTacticsParams"/>, flat, over the defaults (CE-2133).</summary>
        [BehaviorResolver(BehaviorNames.FallBack)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.OverlayFlatAt<EqsTacticsParams>(json, memory, capacity);

        [Behavior(BehaviorNames.FallBack, Params = typeof(EqsTacticsParams))]
        [BTreeDefinition(BehaviorNames.FallBack, AssetId = "a2093000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Tactics",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .StatefulAction(bb => bb.Params, bb => bb.State, EqsTacticsNodes.FallBack, Id("c2093000-0000-0000-0000-000000000002"))
                .Label("Fall back from the top threat")
                .Comment("CE-2093 — docs/DESIGN_Eqs_Consuming_Behaviours.md §2. One move to a FindSafeRetreatPoint answer (hidden from the top threat, far from it); Success on arrival.");
    }

    /// <summary>⭐ <c>Flank</c> — CE-2108 (docs/DESIGN_Eqs_Consuming_Behaviours.md §9): move to a flank on the top threat, then fire.</summary>
    public static class Flank
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public EqsTacticsParams Tactics;
            public EngageParams Engage;
            public EqsTacticsState TacticsState;
            public EngageState EngageState;

            public Blackboard()
            {
                Tactics = new EqsTacticsParams
                {
                    SearchRadius = 40f, MinRepositionMetres = 5f, Speed = 3f, ArrivalRadius = 1.5f, ScoreDeltaThreshold = 0.05f,
                    FireCooldownSeconds = 1f, FireWhileMoving = 0,
                };
                Engage = new EngageParams { CooldownSeconds = 1f };
                TacticsState = default;
                EngageState = default;
            }
        }

        /// <summary>No params (CE-2132): a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.Flank)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.Flank, typeof(NoParams).FullName!);

        [Behavior(BehaviorNames.Flank, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.Flank, AssetId = "a2108000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Tactics",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Sequence(run => run
                .StatefulAction(bb => bb.Tactics, bb => bb.TacticsState, EqsTacticsNodes.Flank, Id("c2108000-0000-0000-0000-000000000003"))
                    .Label("Move to a flank on the top threat")
                    .Comment("CE-2108 — docs/DESIGN_Eqs_Consuming_Behaviours.md §9. FindFlankingPosition (sees the threat, side-on); re-positions while moving; Success on arrival. Fails when no threat is identified.")
                .StatefulAction(bb => bb.Engage, bb => bb.EngageState, PostureNodes.Engage, Id("c2108000-0000-0000-0000-000000000004"))
                    .Label("Engage the top threat")
                    .Comment("Fires at the top threat until the parent ends the run (the ROE gates every shot)."),
                Id("c2108000-0000-0000-0000-000000000002"))
                .Label("Move to a flank on the top threat, then fire");
    }

    /// <summary>⭐ <c>FiringPosition</c> — CE-2108 (docs/DESIGN_Eqs_Consuming_Behaviours.md §9): move to a firing position, then fire.</summary>
    public static class FiringPosition
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public EqsTacticsParams Tactics;
            public EngageParams Engage;
            public EqsTacticsState TacticsState;
            public EngageState EngageState;

            public Blackboard()
            {
                Tactics = new EqsTacticsParams
                {
                    SearchRadius = 30f, MinRepositionMetres = 5f, Speed = 2.5f, ArrivalRadius = 1f, ScoreDeltaThreshold = 0.05f,
                    FireCooldownSeconds = 1f, FireWhileMoving = 1,
                };
                Engage = new EngageParams { CooldownSeconds = 1f };
                TacticsState = default;
                EngageState = default;
            }
        }

        /// <summary>No params (CE-2132): a payload with fields is refused.</summary>
        [BehaviorResolver(BehaviorNames.FiringPosition)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.RequireNoStoredFields(json, BehaviorNames.FiringPosition, typeof(NoParams).FullName!);

        [Behavior(BehaviorNames.FiringPosition, Params = typeof(NoParams))]
        [BTreeDefinition(BehaviorNames.FiringPosition, AssetId = "a2108000-0000-0000-0000-000000000002", Curated = true, PublishesVariables = true, Group = "Library/Tactics",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Sequence(run => run
                .StatefulAction(bb => bb.Tactics, bb => bb.TacticsState, EqsTacticsNodes.FiringPosition, Id("c2108000-0000-0000-0000-000000000103"))
                    .Label("Move to a firing position on the top threat")
                    .Comment("CE-2108 — docs/DESIGN_Eqs_Consuming_Behaviours.md §9. FindOpenFiringPosition (near, sees the threat, higher / less exposed); fires on the way (FireWhileMoving = 1); Success on arrival.")
                .StatefulAction(bb => bb.Engage, bb => bb.EngageState, PostureNodes.Engage, Id("c2108000-0000-0000-0000-000000000104"))
                    .Label("Engage the top threat")
                    .Comment("Fires at the top threat until the parent ends the run (the ROE gates every shot)."),
                Id("c2108000-0000-0000-0000-000000000102"))
                .Label("Move to a firing position on the top threat, then fire");
    }

    /// <summary>⭐ <c>Sentry</c> — CE-3079 H2 (docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.5): watch until a dangerous contact is near, then
    /// hold on and end. No fire node, no SOP: it never shoots.</summary>
    public static class Sentry
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public ThreatCountParams Params;

            public Blackboard() => Params = new ThreatCountParams { Count = 1, MinDanger = 0.5f, LiveOnly = false, WithinMetres = 90f };
        }

        /// <summary>The order's JSON IS <see cref="ThreatCountParams"/>, flat, over the defaults (CE-2133).</summary>
        [BehaviorResolver(BehaviorNames.Sentry)]
        public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
            => BehaviorParams.OverlayFlatAt<ThreatCountParams>(json, memory, capacity);

        [Behavior(BehaviorNames.Sentry, Params = typeof(ThreatCountParams))]
        [BTreeDefinition(BehaviorNames.Sentry, AssetId = "a3079000-0000-0000-0000-000000000001", Curated = true, PublishesVariables = true, Group = "Library/Tactics",
            ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> Build()
            => new BTreeBuilder<Blackboard, BTreeContext>()
            .Sequence(run => run
                .UntilSuccess(watch => watch
                    .Condition(bb => bb.Params, SensorNodes.ThreatsAtLeast, Id("c3079000-0000-0000-0000-000000000004"))
                        .Label("A dangerous contact within range"),
                    Id("c3079000-0000-0000-0000-000000000006"))
                    .Comment("Keep watching until a threat is near.")
                .Wait(15f, Id("c3079000-0000-0000-0000-000000000005"))
                    .Label("Wait(15s)")
                    .Comment("The kernel Wait takes a constant; 15 s is §10.5's value (see §10.5 as-built: WaitSeconds is not a param)."),
                Id("c3079000-0000-0000-0000-000000000002"))
                .Label("Watch, then hold on")
                .Comment("CE-3079 H2 — docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.5. Ends ITSELF (Success) once a dangerous contact is within WithinMetres and the wait has run, so a mission task running it hands over on BehaviorFinished. No fire node, no SOP: it never shoots.");
    }
}
